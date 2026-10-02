using System.Text.Json.Serialization;

namespace SudokuRabbitMQ.Core;

/// <summary>
/// An instruction addressed to one cell. It travels on a unit stream the cell reads, and every other reader of that
/// stream skips it.
/// </summary>
internal abstract record Command : IAboutCell
{
    private Command(int row, int column)
    {
        Row = row;
        Column = column;
    }

    public int Row { get; }

    public int Column { get; }

    /// <summary>
    /// The player places <paramref name="Digit"/> in the cell, on the cell's row stream. The cell decides, because it
    /// owns its candidates, and answers to <see cref="ReplyTo"/>: the only thing that flows back towards the caller.
    /// </summary>
    public sealed record PlaceMove(int Row, int Column, int Digit) : Command(Row, Column)
    {
        /// <summary>Taken from the message's properties, not its body.</summary>
        [JsonIgnore]
        public ReplyAddress? ReplyTo { get; init; }
    }

    /// <summary>
    /// The rules force <paramref name="Digit"/> in the cell: a naked single, which the cell sends itself on its row
    /// stream, or a hidden single, which a unit's watcher sends on its own stream.
    /// </summary>
    public sealed record PlaceDeduction(int Row, int Column, int Digit) : Command(Row, Column);
}

/// <summary>Where and under which correlation id a cell answers a move.</summary>
internal sealed record ReplyAddress(string ReplyTo, string CorrelationId);
