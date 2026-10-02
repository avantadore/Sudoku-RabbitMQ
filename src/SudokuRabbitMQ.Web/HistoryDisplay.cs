namespace SudokuRabbitMQ.Web;

/// <summary>How the move history looks on the page: one row per position, starting with the empty grid.</summary>
public static class HistoryDisplay
{
    public static IReadOnlyList<HistoryRow> Rows(int position, IReadOnlyList<GridMove> moves) =>
    [
        new HistoryRow(0, "Empty grid", null, null, StateOf(0, position)),
        .. moves.Select((move, index) => Row(index + 1, move, position)),
    ];

    /// <summary>◀ replays to the previous position, as far back as the empty grid.</summary>
    public static bool CanReplayBack(int position, bool busy) => !busy && position > 0;

    /// <summary>▶ replays to the next position, as far forward as the last kept move.</summary>
    public static bool CanReplayForward(int position, int moves, bool busy) => !busy && position < moves;

    public static string CssClass(this HistoryRowState state) => state switch
    {
        HistoryRowState.Applied => "applied",
        HistoryRowState.Current => "current",
        HistoryRowState.After => "after",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    private static HistoryRow Row(int number, GridMove move, int position) =>
        new(
            number,
            $"{number}. r{move.Row}c{move.Column} = {move.Digit}",
            move.Deductions > 0 ? $"(+{move.Deductions})" : null,
            move.Deductions switch
            {
                0 => null,
                1 => "1 deduction",
                var deductions => $"{deductions} deductions",
            },
            StateOf(number, position));

    private static HistoryRowState StateOf(int rowPosition, int position) =>
        rowPosition < position ? HistoryRowState.Applied
        : rowPosition == position ? HistoryRowState.Current
        : HistoryRowState.After;
}

/// <summary>
/// One row of the history panel. <paramref name="Position"/> is the position the row stands for: 0 for the empty
/// grid, then the number of each move. <paramref name="Deductions"/> and its tooltip are null for a move that made none.
/// </summary>
public sealed record HistoryRow(int Position, string Text, string? Deductions, string? DeductionsTooltip, HistoryRowState State);

public enum HistoryRowState
{
    /// <summary>The row's move applies to the grid.</summary>
    Applied,

    /// <summary>The row is the game's position.</summary>
    Current,

    /// <summary>The row's move is kept but does not apply, until a new move discards it.</summary>
    After,
}
