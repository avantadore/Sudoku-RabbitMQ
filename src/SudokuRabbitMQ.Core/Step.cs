namespace SudokuRabbitMQ.Core;

/// <summary>One thing that happened to a grid. A move is followed by the steps of its cascade, in order.</summary>
public abstract record Step
{
    private Step()
    {
    }

    /// <summary>A digit was put in a cell, by the player (a move) or by the game (a deduction).</summary>
    public sealed record Placement(int Row, int Column, int Digit, PlacementSource Source) : Step;

    /// <summary>A candidate was removed from a cell because one of its peers holds that digit.</summary>
    public sealed record Elimination(int Row, int Column, int Digit) : Step;

    /// <summary>The grid can no longer lead to a solution. The game is read-only from here on.</summary>
    public abstract record Contradiction : Step
    {
        private Contradiction()
        {
        }

        /// <summary>A cell has no candidates left.</summary>
        public sealed record NoCandidateForCell(int Row, int Column) : Contradiction;

        /// <summary>
        /// A digit has no possible cell left in a unit. Boxes are numbered 1–9 row by row, starting top left.
        /// </summary>
        public sealed record NoCellForDigit(UnitKind UnitKind, int UnitNumber, int Digit) : Contradiction;
    }
}
