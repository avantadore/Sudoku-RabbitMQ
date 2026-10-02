using RabbitMQ.Client;

namespace SudokuRabbitMQ.Core;

/// <summary>
/// One cell, as a worker reading its row, column and box streams. It owns its candidates, so it decides whether a
/// move is accepted, eliminates by itself when it hears that a peer was filled, detects its own naked single, and
/// announces what happened to it on all three streams. It skips whatever else it hears: commands for other cells, and
/// candidates its peers lost.
/// </summary>
internal sealed class CellWorker : IAsyncDisposable
{
    private readonly SortedSet<int> _candidates = [1, 2, 3, 4, 5, 6, 7, 8, 9];
    private readonly Lock _state = new(); // the worker writes while the game may take a snapshot
    private readonly Switchboard _switchboard;
    private Mailbox? _mailbox;
    private string[] _units = [];

    public CellWorker(int row, int column, Switchboard switchboard)
    {
        Row = row;
        Column = column;
        _switchboard = switchboard;
    }

    public int Row { get; }

    public int Column { get; }

    /// <summary>The box this cell belongs to, 1–9 row by row from the top left.</summary>
    public int Box => Topology.BoxOf(Row, Column);

    public int? Digit { get; private set; }

    public PlacementSource? Source { get; private set; }

    /// <summary>Starts reading the cell's row, column and box streams, in that order.</summary>
    public async Task StartAsync(IConnection connection, string row, string column, string box)
    {
        _units = [row, column, box];
        _mailbox = await Mailbox.OpenAsync(connection, _switchboard);
        foreach (var unit in _units)
        {
            await _mailbox.ReadAsync(unit, HandleAsync);
        }
    }

    public bool IsFilled
    {
        get
        {
            lock (_state)
            {
                return Digit is not null;
            }
        }
    }

    public Cell Snapshot()
    {
        lock (_state)
        {
            return new Cell(Row, Column, Digit, Source, [.. _candidates]);
        }
    }

    public ValueTask DisposeAsync() => _mailbox?.DisposeAsync() ?? ValueTask.CompletedTask;

    private Mailbox Mailbox => _mailbox ?? throw new InvalidOperationException($"Cell ({Row}, {Column}) has not started.");

    private Task HandleAsync(object message) => message switch
    {
        Command.PlaceMove move when IsThis(move) => PlaceMoveAsync(move),
        Command.PlaceDeduction deduction when IsThis(deduction) => PlaceDeductionAsync(deduction.Digit),
        Event.Filled filled when !IsThis(filled) => EliminateAsync(filled.Digit),
        _ => Task.CompletedTask,
    };

    private bool IsThis(IAboutCell message) => message.Row == Row && message.Column == Column;

    private async Task PlaceMoveAsync(Command.PlaceMove move)
    {
        var reply = move.ReplyTo ?? throw new InvalidOperationException($"{move} has no reply address.");
        if (Digit == move.Digit)
        {
            await Mailbox.ReplyAsync(reply, new MoveOutcome.Unchanged());
        }
        else if (Digit is { } placed)
        {
            await Mailbox.ReplyAsync(reply, new MoveOutcome.Rejected($"Cell ({Row}, {Column}) already holds {placed}, and placements are final."));
        }
        else if (!_candidates.Contains(move.Digit))
        {
            await Mailbox.ReplyAsync(reply, new MoveOutcome.Rejected($"{move.Digit} is not a candidate for cell ({Row}, {Column})."));
        }
        else
        {
            await Mailbox.ReplyAsync(reply, new MoveOutcome.Accepted());
            await AnnounceFilledAsync(move.Digit, PlacementSource.Move, Fill(move.Digit, PlacementSource.Move));
        }
    }

    // A unit's view of its cells can lag, and so can this cell's: by the time a deduction arrives, the cascade may
    // have filled the cell, removed the digit or reached a contradiction, after which nothing more is deduced.
    private async Task PlaceDeductionAsync(int digit)
    {
        if (Digit is not null || !_candidates.Contains(digit))
        {
            return;
        }

        List<int> lost = [];
        if (_switchboard.Deduce(() => lost = Fill(digit, PlacementSource.Deduction)))
        {
            await AnnounceFilledAsync(digit, PlacementSource.Deduction, lost);
        }
    }

    /// <summary>Puts the digit in the cell, and returns the candidates the cell lost by it.</summary>
    private List<int> Fill(int digit, PlacementSource source)
    {
        lock (_state)
        {
            List<int> lost = [.. _candidates.Where(candidate => candidate != digit)];
            Digit = digit;
            Source = source;
            _candidates.IntersectWith([digit]);
            return lost;
        }
    }

    private async Task AnnounceFilledAsync(int digit, PlacementSource source, List<int> lost)
    {
        await Mailbox.PublishStepAsync(new Step.Placement(Row, Column, digit, source));

        // Filled first, so a unit already knows the cell holds its digit when it hears which candidates it lost.
        await AnnounceAsync(new Event.Filled(Row, Column, digit, source));
        foreach (var candidate in lost)
        {
            await AnnounceAsync(new Event.CandidateLost(Row, Column, candidate));
        }
    }

    private async Task EliminateAsync(int digit)
    {
        if (Digit == digit)
        {
            // A peer was filled with this cell's own digit. Only a deduction that raced the elimination ruling it
            // out can do that, so the digit is now twice in a unit and this cell has no candidate left.
            await ContradictAsync();
            return;
        }

        int remaining;
        lock (_state)
        {
            if (!_candidates.Remove(digit))
            {
                return; // already gone, e.g. eliminated by a peer that shares two units with this cell
            }

            remaining = _candidates.Count;
        }

        await Mailbox.PublishStepAsync(new Step.Elimination(Row, Column, digit));

        if (Digit is null && remaining == 1)
        {
            // A naked single, sent to itself so that whatever it has already heard is handled first.
            await Mailbox.SendAsync(_units[0], new Command.PlaceDeduction(Row, Column, _candidates.Min));
        }
        else if (Digit is null && remaining == 0)
        {
            await ContradictAsync();
        }

        await AnnounceAsync(new Event.CandidateLost(Row, Column, digit));
    }

    private async Task ContradictAsync()
    {
        if (_switchboard.Contradict())
        {
            await Mailbox.PublishStepAsync(new Step.Contradiction.NoCandidateForCell(Row, Column));
        }
    }

    private async Task AnnounceAsync(Event @event)
    {
        foreach (var unit in _units)
        {
            await Mailbox.SendAsync(unit, @event);
        }
    }
}
