using RabbitMQ.Client;

namespace SudokuRabbitMQ.Core;

/// <summary>
/// A row, column or box's watcher: a worker reading the unit's stream alongside its nine cells. It never relays:
/// the cells hear each other directly. It tracks which of the unit's cells can still hold each digit: when only one
/// empty cell can, it deduces the digit there (a hidden single) on the unit's stream; when none can, the grid is in
/// contradiction. It skips the commands it hears.
/// </summary>
internal sealed class UnitWorker : IAsyncDisposable
{
    private readonly UnitKind _kind;
    private readonly int _number;
    private readonly IReadOnlyList<(int Row, int Column)> _cells;
    private readonly Switchboard _switchboard;
    private Mailbox? _mailbox;

    /// <summary>For each digit 1–9, the indexes of the cells that can still hold it. A filled cell holds its digit.</summary>
    private readonly HashSet<int>[] _holders;

    /// <summary>Which of the cells, by index, have been filled.</summary>
    private readonly bool[] _filled = new bool[9];

    public UnitWorker(Guid grid, UnitKind kind, int number, IReadOnlyList<(int Row, int Column)> cells, Switchboard switchboard)
    {
        _kind = kind;
        _number = number;
        _cells = cells;
        _switchboard = switchboard;
        _holders = [.. Enumerable.Range(0, 9).Select(_ => Enumerable.Range(0, 9).ToHashSet())];
        Stream = Topology.UnitStream(grid, kind, number);
    }

    /// <summary>The unit's stream: its topic, which its cells and this watcher read.</summary>
    public string Stream { get; }

    public async Task StartAsync(IConnection connection)
    {
        _mailbox = await Mailbox.OpenAsync(connection, _switchboard);
        await _mailbox.ReadAsync(Stream, HandleAsync);
    }

    public ValueTask DisposeAsync() => _mailbox?.DisposeAsync() ?? ValueTask.CompletedTask;

    private Task HandleAsync(object message)
    {
        switch (message)
        {
            case Event.Filled filled:
                _filled[IndexOf(filled)] = true;
                return Task.CompletedTask;
            case Event.CandidateLost lost:
                return OnCandidateLostAsync(IndexOf(lost), lost.Digit);
            default:
                return Task.CompletedTask;
        }
    }

    private async Task OnCandidateLostAsync(int index, int digit)
    {
        var holders = _holders[digit - 1];
        if (!holders.Remove(index))
        {
            return;
        }

        switch (holders.Count)
        {
            case 0:
                if (_switchboard.Contradict())
                {
                    await _mailbox!.PublishStepAsync(new Step.Contradiction.NoCellForDigit(_kind, _number, digit));
                }

                break;
            case 1 when holders.Single() is var only && !_filled[only]:
                await _mailbox!.SendAsync(Stream, new Command.PlaceDeduction(_cells[only].Row, _cells[only].Column, digit));
                break;
        }
    }

    private int IndexOf(IAboutCell message)
    {
        for (var index = 0; index < 9; index++)
        {
            if (_cells[index] == (message.Row, message.Column))
            {
                return index;
            }
        }

        throw new ArgumentException($"Cell ({message.Row}, {message.Column}) is not in {Stream}.");
    }
}
