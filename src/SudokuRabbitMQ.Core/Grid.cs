using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace SudokuRabbitMQ.Core;

/// <summary>
/// The 81 cells of one game and the 27 units that propagate their constraints, talking on the grid's own 27 unit
/// streams (ADR 0004). A replay gives a game a fresh grid and disposes the old one, which deletes its streams.
/// </summary>
internal sealed class Grid : IAsyncDisposable
{
    private readonly Guid _id = Guid.NewGuid();
    private readonly IConnection _connection;
    private readonly Switchboard _switchboard = new();
    private readonly CellWorker[,] _cells = new CellWorker[9, 9];
    private readonly UnitWorker[] _units;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<MoveOutcome>> _replies = new();
    private Mailbox? _front; // where moves are sent from and their replies arrive

    private Grid(IConnection connection)
    {
        _connection = connection;
        for (var row = 1; row <= 9; row++)
        {
            for (var column = 1; column <= 9; column++)
            {
                _cells[row - 1, column - 1] = new CellWorker(row, column, _switchboard);
            }
        }

        _units = [.. Units(UnitKind.Row, cell => cell.Row), .. Units(UnitKind.Column, cell => cell.Column), .. Units(UnitKind.Box, cell => cell.Box)];

        // Units 1–9 of a kind, each with its nine cells row by row.
        IEnumerable<UnitWorker> Units(UnitKind kind, Func<CellWorker, int> number) =>
            _cells.Cast<CellWorker>()
                .GroupBy(number)
                .OrderBy(unit => unit.Key)
                .Select(unit => new UnitWorker(_id, kind, unit.Key, [.. unit.Select(cell => (cell.Row, cell.Column))], _switchboard));
    }

    /// <summary>Declares the grid's 27 unit streams and starts its 108 workers reading them.</summary>
    public static async Task<Grid> StartAsync(IConnection connection)
    {
        var grid = new Grid(connection);
        try
        {
            await grid.StartAsync();
            return grid;
        }
        catch
        {
            await grid.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// The game's steps stream, where the grid publishes each move followed by the steps of its cascade. A grid built
    /// for a replay has none until it is filled and made current.
    /// </summary>
    public string? StepsStream
    {
        get => _switchboard.StepsStream;
        set => _switchboard.StepsStream = value;
    }

    /// <summary>How many watchers read the steps stream. Only set between moves.</summary>
    public int StepWatchers
    {
        get => _switchboard.StepWatchers;
        set => _switchboard.StepWatchers = value;
    }

    public GameState State =>
        _switchboard.IsContradicted ? GameState.Contradicted
        : _cells.Cast<CellWorker>().All(cell => cell.IsFilled) ? GameState.Solved
        : GameState.InProgress;

    /// <summary>All 81 cells, row by row.</summary>
    public IEnumerable<Cell> Cells => _cells.Cast<CellWorker>().Select(cell => cell.Snapshot());

    public Cell Cell(int row, int column) => At(row, column).Snapshot();

    /// <summary>A watcher has read a step, which counts towards the cascade being over.</summary>
    public void StepRead() => _switchboard.MessageHandled();

    /// <summary>
    /// The player places <paramref name="digit"/> in a cell, sent on the cell's row stream. Completes once the whole
    /// cascade has, with how many deductions it made. The caller makes one move at a time.
    /// </summary>
    public async Task<(MoveOutcome Outcome, int Deductions)> MoveAsync(int row, int column, int digit)
    {
        ThrowIfOutside1To9(digit);
        var cell = At(row, column);

        if (_switchboard.IsContradicted)
        {
            return (new MoveOutcome.Rejected(
                "The game is in contradiction, so no more moves can be made. Replay to an earlier position to continue, or start a new game."), 0);
        }

        _switchboard.StartCascade();
        var correlationId = Guid.NewGuid().ToString();
        var reply = new TaskCompletionSource<MoveOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        _replies[correlationId] = reply;
        try
        {
            await _front!.SendAsync(
                Topology.UnitStream(_id, UnitKind.Row, row),
                new Command.PlaceMove(cell.Row, cell.Column, digit) { ReplyTo = new ReplyAddress(Topology.DirectReplyTo, correlationId) });

            // A worker that fails may never reply, and then the cascade fails instead. Otherwise the cell replied
            // before its delivery counted as handled, so the reply is on its way even if the cascade is already over.
            await _switchboard.CascadeOver;
            return (await reply.Task, _switchboard.Deductions);
        }
        finally
        {
            _replies.TryRemove(correlationId, out _);
        }
    }

    /// <summary>Stops every worker, and deletes the grid's streams.</summary>
    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll([
            .. _cells.Cast<CellWorker>().Select(cell => cell.DisposeAsync().AsTask()),
            .. _units.Select(unit => unit.DisposeAsync().AsTask()),
        ]);

        if (_front is not null)
        {
            foreach (var unit in _units)
            {
                await _front.Channel.DeleteStreamAsync(unit.Stream);
            }

            await _front.DisposeAsync();
        }
    }

    private async Task StartAsync()
    {
        _front = await Mailbox.OpenAsync(_connection, _switchboard);
        foreach (var unit in _units)
        {
            await _front.Channel.DeclareStreamAsync(unit.Stream);
        }

        var replies = new AsyncEventingBasicConsumer(_front.Channel);
        replies.ReceivedAsync += (_, delivery) =>
        {
            if (delivery.BasicProperties.CorrelationId is { } correlationId
                && _replies.TryGetValue(correlationId, out var reply))
            {
                reply.TrySetResult((MoveOutcome)Envelope.Open(delivery.BasicProperties, delivery.Body));
            }

            return Task.CompletedTask;
        };
        await _front.Channel.BasicConsumeAsync(Topology.DirectReplyTo, autoAck: true, replies);

        string Stream(UnitKind kind, int number) => Topology.UnitStream(_id, kind, number);
        await Task.WhenAll([
            .. _cells.Cast<CellWorker>().Select(cell => cell.StartAsync(
                _connection, Stream(UnitKind.Row, cell.Row), Stream(UnitKind.Column, cell.Column), Stream(UnitKind.Box, cell.Box))),
            .. _units.Select(unit => unit.StartAsync(_connection)),
        ]);
    }

    private CellWorker At(int row, int column)
    {
        ThrowIfOutside1To9(row);
        ThrowIfOutside1To9(column);
        return _cells[row - 1, column - 1];
    }

    private static void ThrowIfOutside1To9(int value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 1, name);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 9, name);
    }
}
