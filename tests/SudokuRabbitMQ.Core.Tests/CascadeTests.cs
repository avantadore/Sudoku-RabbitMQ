namespace SudokuRabbitMQ.Core.Tests;

public class CascadeTests
{
    [Fact]
    public async Task Deductions_force_further_deductions_until_the_givens_of_a_puzzle_solve_it()
    {
        await using var game = await Broker.NewGameAsync();

        foreach (var (row, column, digit) in Puzzle.Givens)
        {
            // A given may already have been deduced from earlier givens, which leaves the move unchanged.
            Assert.IsNotType<MoveOutcome.Rejected>(await game.MoveAsync(row, column, digit));
        }

        foreach (var (row, column, digit) in Puzzle.Solution)
        {
            var cell = game.Cell(row, column);
            Assert.Equal(digit, cell.Digit);
            if (!Puzzle.IsGiven(row, column))
            {
                Assert.Equal(PlacementSource.Deduction, cell.Source);
            }
        }
    }
}
