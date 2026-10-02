namespace SudokuRabbitMQ.Core.Tests;

/// <summary>
/// The example puzzle and its solution from https://en.wikipedia.org/wiki/Sudoku. It needs only naked and hidden
/// singles, but most of its cells are forced only by earlier deductions.
/// </summary>
internal static class Puzzle
{
    private static readonly string[] GivenRows =
    [
        "53..7....",
        "6..195...",
        ".98....6.",
        "8...6...3",
        "4..8.3..1",
        "7...2...6",
        ".6....28.",
        "...419..5",
        "....8..79",
    ];

    private static readonly string[] SolutionRows =
    [
        "534678912",
        "672195348",
        "198342567",
        "859761423",
        "426853791",
        "713924856",
        "961537284",
        "287419635",
        "345286179",
    ];

    /// <summary>The givens, row by row.</summary>
    public static IEnumerable<(int Row, int Column, int Digit)> Givens => Cells(GivenRows);

    public static IEnumerable<(int Row, int Column, int Digit)> Solution => Cells(SolutionRows);

    public static bool IsGiven(int row, int column) => GivenRows[row - 1][column - 1] != '.';

    private static IEnumerable<(int Row, int Column, int Digit)> Cells(string[] rows) =>
        from row in Enumerable.Range(1, 9)
        from column in Enumerable.Range(1, 9)
        let character = rows[row - 1][column - 1]
        where character != '.'
        select (row, column, character - '0');
}
