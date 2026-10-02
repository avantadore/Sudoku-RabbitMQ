using RabbitMQ.Client;

namespace SudokuRabbitMQ.Core;

/// <summary>
/// A session in which one 9×9 grid is filled in. Every game starts with all 81 cells empty. A game has its own
/// connection to RabbitMQ and its own steps stream; its grid has its own unit streams (ADR 0004).
/// </summary>
public sealed class Game : IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly IChannel _channel; // declares and deletes the steps stream
    private readonly List<RecordedMove> _moves = [];
    private readonly List<StepWatcher> _watchers = [];
    private readonly SemaphoreSlim _turn = new(1, 1); // one move, replay, watcher change or suspension at a time
    private bool _disposed;
    private volatile Grid? _grid;
    private volatile Suspended? _suspended;

    private Game(Guid id, IConnection connection, IChannel channel, Grid grid)
    {
        Id = id;
        _connection = connection;
        _channel = channel;
        _grid = grid;
        grid.StepsStream = StepsStream;
    }

    /// <summary>Starts a new game on its own connection from <paramref name="connections"/>.</summary>
    public static async Task<Game> NewAsync(IConnectionFactory connections, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        var connection = await connections.CreateConnectionAsync($"sudoku game {id}", cancellationToken);
        try
        {
            var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
            await channel.DeclareStreamAsync(Topology.StepsStream(id));
            return new Game(id, connection, channel, await Grid.StartAsync(connection));
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public Guid Id { get; }

    public GameState State => _grid?.State ?? _suspended!.State;

    /// <summary>The moves this game has accepted, in order.</summary>
    public IReadOnlyList<RecordedMove> Moves => _moves.AsReadOnly();

    /// <summary>How many moves from the start of <see cref="Moves"/> apply to the grid. 0 is the empty grid.</summary>
    public int Position { get; private set; }

    /// <summary>All 81 cells, row by row.</summary>
    public IEnumerable<Cell> Cells => _grid?.Cells ?? _suspended!.Cells;

    public Cell Cell(int row, int column) =>
        _grid?.Cell(row, column) ?? _suspended!.Cells.Single(cell => cell.Row == row && cell.Column == column);

    /// <summary>Whether the grid has been put away to free its streams, to be rebuilt by replay on the next move.</summary>
    public bool IsSuspended => _grid is null;

    private string StepsStream => Topology.StepsStream(Id);

    /// <summary>
    /// A fresh reader of everything that happens to the grid from now on: each move, followed by the steps of its
    /// cascade. Every watcher reads every step. Within a cascade, only cause comes before effect: the move's
    /// placement first, each elimination after the placement that caused it, each deduction after the step that
    /// forced it (ADR 0003). A contradiction is a step too, so a watcher ends only when it or the game is disposed. It
    /// is not necessarily the last step of its move: eliminations still follow, but no further deductions do. A replay
    /// publishes nothing: watchers keep watching and read the steps of the moves after it.
    /// </summary>
    public async Task<StepWatcher> WatchStepsAsync()
    {
        await _turn.WaitAsync();
        try
        {
            if (_disposed)
            {
                return StepWatcher.Stopped();
            }

            var watcher = await StepWatcher.StartAsync(_connection, StepsStream, () => _grid?.StepRead(), StopWatchingAsync);
            _watchers.Add(watcher);
            CountWatchers();
            return watcher;
        }
        finally
        {
            _turn.Release();
        }
    }

    /// <summary>
    /// The player places <paramref name="digit"/> in a cell, and the move completes once its whole cascade has. A
    /// move made while another is running waits its turn. Only an accepted move enters the move history.
    /// </summary>
    public async Task<MoveOutcome> MoveAsync(int row, int column, int digit)
    {
        await _turn.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_grid is null)
            {
                MakeCurrent(await BuildAsync(Position));
            }

            var (outcome, deductions) = await _grid!.MoveAsync(row, column, digit);
            if (outcome is MoveOutcome.Accepted)
            {
                _moves.RemoveRange(Position, _moves.Count - Position);
                _moves.Add(new RecordedMove(row, column, digit, deductions));
                Position = _moves.Count;
            }

            return outcome;
        }
        finally
        {
            _turn.Release();
        }
    }

    /// <summary>
    /// Rebuilds the grid from the first <paramref name="position"/> moves, which becomes the game's position. Watchers
    /// see none of it.
    /// </summary>
    public async Task ReplayToAsync(int position)
    {
        await _turn.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(position);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(position, _moves.Count);

            var replaced = _grid;
            MakeCurrent(await BuildAsync(position));
            Position = position;
            if (replaced is not null)
            {
                await replaced.DisposeAsync();
            }
        }
        finally
        {
            _turn.Release();
        }
    }

    /// <summary>
    /// Puts the grid away, deleting its unit streams, while keeping the game: its state and cells can still be read,
    /// and its next move first rebuilds the grid by replaying to the position. A grid in contradiction may come back
    /// as a different one (ADR 0003).
    /// </summary>
    public async Task SuspendAsync()
    {
        await _turn.WaitAsync();
        try
        {
            if (_disposed || _grid is not { } grid)
            {
                return;
            }

            _suspended = new Suspended(grid.State, [.. grid.Cells]);
            _grid = null;
            await grid.DisposeAsync();
        }
        finally
        {
            _turn.Release();
        }
    }

    /// <summary>
    /// Waits for a running move, stops the grid's workers and every watcher, and deletes the game's streams. Steps
    /// already read can still be taken.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _turn.WaitAsync();
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var watcher in _watchers)
            {
                await watcher.StopAsync();
            }

            _watchers.Clear();
            if (_grid is { } grid)
            {
                await grid.DisposeAsync();
            }

            await _channel.DeleteStreamAsync(StepsStream);
            await _connection.CloseAsync();
            await _connection.DisposeAsync();
        }
        finally
        {
            _turn.Release();
        }
    }

    /// <summary>
    /// A grid on which the first <paramref name="position"/> moves have been replayed, publishing no steps. If one is
    /// ever not accepted, the grid would no longer match the history, so it fails before the grid is used.
    /// </summary>
    private async Task<Grid> BuildAsync(int position)
    {
        var grid = await Grid.StartAsync(_connection);
        try
        {
            foreach (var move in _moves.Take(position))
            {
                // Every move up to the position was accepted before, and the grid it met is rebuilt exactly as long
                // as no contradiction is reached, which only the last of them can reach (ADR 0003).
                if ((await grid.MoveAsync(move.Row, move.Column, move.Digit)).Outcome is not MoveOutcome.Accepted)
                {
                    throw new InvalidOperationException($"Replaying {move} was not accepted, so the grid would not match the move history.");
                }
            }

            return grid;
        }
        catch
        {
            await grid.DisposeAsync();
            throw;
        }
    }

    private void MakeCurrent(Grid grid)
    {
        grid.StepsStream = StepsStream;
        grid.StepWatchers = _watchers.Count;
        _grid = grid;
        _suspended = null;
    }

    private void CountWatchers()
    {
        if (_grid is { } grid)
        {
            grid.StepWatchers = _watchers.Count;
        }
    }

    private async ValueTask StopWatchingAsync(StepWatcher watcher)
    {
        await _turn.WaitAsync();
        try
        {
            if (_watchers.Remove(watcher))
            {
                CountWatchers();
            }

            await watcher.StopAsync();
        }
        finally
        {
            _turn.Release();
        }
    }

    private sealed record Suspended(GameState State, IReadOnlyList<Cell> Cells);
}
