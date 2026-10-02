namespace SudokuRabbitMQ.Core.Tests;

// Each grid is built so the hidden single exists in one unit only, and its cell still has other candidates,
// so the deduction can only come from that unit.
public class HiddenSingleTests
{
    [Fact]
    public async Task A_digit_with_one_possible_cell_in_a_row_is_placed_there_as_a_deduction()
    {
        await using var game = await Broker.NewGameAsync();
        await PlayRow1With2To7(game); // (1,7), (1,8) and (1,9) are left with 1, 8 and 9
        await game.MoveAsync(4, 7, 1); // 1 leaves (1,7)

        await game.MoveAsync(7, 8, 1); // 1 leaves (1,8): in row 1, only (1,9) can hold 1

        Assert.Equal((1, PlacementSource.Deduction), DigitAndSource(game.Cell(1, 9)));
        Assert.DoesNotContain(1, game.Cell(2, 9).Candidates);
    }

    [Fact]
    public async Task A_digit_with_one_possible_cell_in_a_column_is_placed_there_as_a_deduction()
    {
        await using var game = await Broker.NewGameAsync();
        for (var digit = 2; digit <= 7; digit++)
        {
            await game.MoveAsync(digit - 1, 1, digit); // (7,1), (8,1) and (9,1) are left with 1, 8 and 9
        }

        await game.MoveAsync(7, 4, 1); // 1 leaves (7,1)

        await game.MoveAsync(8, 7, 1); // 1 leaves (8,1): in column 1, only (9,1) can hold 1

        Assert.Equal((1, PlacementSource.Deduction), DigitAndSource(game.Cell(9, 1)));
        Assert.DoesNotContain(1, game.Cell(9, 2).Candidates);
    }

    [Fact]
    public async Task A_digit_with_one_possible_cell_in_a_box_is_placed_there_as_a_deduction()
    {
        await using var game = await Broker.NewGameAsync();
        // Box 1 holds 2–7, leaving (1,1), (2,2) and (3,3) with 1, 8 and 9.
        await game.MoveAsync(1, 2, 2);
        await game.MoveAsync(1, 3, 3);
        await game.MoveAsync(2, 1, 4);
        await game.MoveAsync(2, 3, 5);
        await game.MoveAsync(3, 1, 6);
        await game.MoveAsync(3, 2, 7);
        await game.MoveAsync(1, 5, 1); // 1 leaves (1,1)

        await game.MoveAsync(8, 2, 1); // 1 leaves (2,2): in box 1, only (3,3) can hold 1

        Assert.Equal((1, PlacementSource.Deduction), DigitAndSource(game.Cell(3, 3)));
        Assert.DoesNotContain(1, game.Cell(3, 9).Candidates);
    }

    [Fact]
    public async Task Filling_a_cell_with_another_digit_can_leave_a_hidden_single()
    {
        await using var game = await Broker.NewGameAsync();
        for (var digit = 2; digit <= 6; digit++)
        {
            await game.MoveAsync(1, digit - 1, digit);
        }

        await game.MoveAsync(4, 7, 1); // 1 leaves (1,7)
        await game.MoveAsync(7, 8, 1); // 1 leaves (1,8): in row 1, 1 can go in (1,6) or (1,9)

        await game.MoveAsync(1, 6, 7); // filling (1,6) with 7 leaves (1,9) as the only cell for 1 in row 1

        Assert.Equal((1, PlacementSource.Deduction), DigitAndSource(game.Cell(1, 9)));
    }

    [Fact]
    public async Task Hidden_and_naked_singles_chain_within_one_cascade()
    {
        await using var game = await Broker.NewGameAsync();
        await PlayRow1With2To7(game);
        await game.MoveAsync(4, 7, 1); // (1,7) is left with 8 and 9
        await game.MoveAsync(5, 8, 9); // (1,8) is left with 1 and 8
        var watcher = await game.WatchStepsAsync();

        // 1 leaves (1,8): it becomes a naked single (8), and (1,9) a hidden single for 1 in row 1.
        // Either of those placements then leaves (1,7) as the only place for 9.
        await game.MoveAsync(7, 8, 1);

        var steps = watcher.Drain();
        var forcing = new Step.Elimination(1, 8, 1);
        Step[] deductions =
        [
            new Step.Placement(1, 8, 8, PlacementSource.Deduction),
            new Step.Placement(1, 9, 1, PlacementSource.Deduction),
            new Step.Placement(1, 7, 9, PlacementSource.Deduction),
        ];
        Assert.Equal(new Step.Placement(7, 8, 1, PlacementSource.Move), steps[0]);
        StepAssert.SameSet(deductions, steps.OfType<Step.Placement>().Skip(1));
        Assert.All(deductions, deduction => StepAssert.Before(steps, forcing, deduction));
        Assert.True(
            steps.IndexOf(deductions[2]) > Math.Min(steps.IndexOf(deductions[0]), steps.IndexOf(deductions[1])),
            "(1,7) is forced only once (1,8) or (1,9) is filled.");
    }

    private static async Task PlayRow1With2To7(Game game)
    {
        for (var digit = 2; digit <= 7; digit++)
        {
            await game.MoveAsync(1, digit - 1, digit);
        }
    }

    private static (int?, PlacementSource?) DigitAndSource(Cell cell) => (cell.Digit, cell.Source);
}
