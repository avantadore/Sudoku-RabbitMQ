namespace SudokuRabbitMQ.Core.Tests;

public class NewGameTests
{
    private static readonly int[] AllDigits = [1, 2, 3, 4, 5, 6, 7, 8, 9];

    [Fact]
    public async Task A_new_game_is_in_progress()
    {
        await using var game = await Broker.NewGameAsync();

        Assert.Equal(GameState.InProgress, game.State);
    }

    [Fact]
    public async Task Every_cell_of_a_new_game_is_empty_with_all_nine_candidates()
    {
        await using var game = await Broker.NewGameAsync();

        for (var row = 1; row <= 9; row++)
        {
            for (var column = 1; column <= 9; column++)
            {
                var cell = game.Cell(row, column);

                Assert.Equal(row, cell.Row);
                Assert.Equal(column, cell.Column);
                Assert.Null(cell.Digit);
                Assert.Null(cell.Source);
                Assert.Equal(AllDigits, cell.Candidates);
            }
        }
    }

    [Fact]
    public async Task A_game_lists_its_81_cells_row_by_row()
    {
        await using var game = await Broker.NewGameAsync();

        var coordinates = game.Cells.Select(cell => (cell.Row, cell.Column));

        var expected = Enumerable.Range(1, 9)
            .SelectMany(row => Enumerable.Range(1, 9).Select(column => (row, column)));
        Assert.Equal(expected, coordinates);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(10, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 10)]
    public async Task A_cell_outside_rows_and_columns_1_to_9_does_not_exist(int row, int column)
    {
        await using var game = await Broker.NewGameAsync();

        Assert.Throws<ArgumentOutOfRangeException>(() => game.Cell(row, column));
    }
}
