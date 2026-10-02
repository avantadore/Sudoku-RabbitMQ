namespace SudokuRabbitMQ.Core;

/// <summary>
/// A fact a cell announces on its row, column and box streams. Every reader of those streams hears it: the cell's
/// peers and the units' watchers. An event is not a step: an observer never sees one.
/// </summary>
internal abstract record Event : IAboutCell
{
    private Event(int row, int column)
    {
        Row = row;
        Column = column;
    }

    public int Row { get; }

    public int Column { get; }

    /// <summary>The cell was filled with <paramref name="Digit"/>. Its peers eliminate the digit when they hear it.</summary>
    public sealed record Filled(int Row, int Column, int Digit, PlacementSource Source) : Event(Row, Column);

    /// <summary>The cell lost <paramref name="Digit"/> as a candidate, by elimination or because it was filled.</summary>
    public sealed record CandidateLost(int Row, int Column, int Digit) : Event(Row, Column);
}

/// <summary>A message about one cell, which carries it in its <c>row</c> and <c>column</c> headers.</summary>
internal interface IAboutCell
{
    int Row { get; }

    int Column { get; }
}
