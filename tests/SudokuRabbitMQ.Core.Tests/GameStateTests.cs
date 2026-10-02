namespace SudokuRabbitMQ.Core.Tests;

public class GameStateTests
{
    [Fact]
    public async Task A_cell_left_with_no_candidates_puts_the_game_in_contradiction_after_applying_the_move()
    {
        await using var game = await Broker.NewGameAsync();
        // Rows 1 and 9 hold 1–7 in columns 1–7, so (1,8), (1,9), (9,8) and (9,9) are left with 8 and 9.
        for (var column = 1; column <= 7; column++)
        {
            await game.MoveAsync(1, column, column);
            await game.MoveAsync(9, column, column % 7 + 1);
        }

        var watcher = await game.WatchStepsAsync();

        // 9 in column 9 leaves (1,9) and (9,9) both with only 8. Whichever is deduced 8 first leaves the other
        // with no candidates at all.
        var outcome = await game.MoveAsync(5, 9, 9);

        Assert.IsType<MoveOutcome.Accepted>(outcome);
        Assert.Equal(9, game.Cell(5, 9).Digit);
        Assert.Equal(GameState.Contradicted, game.State);
        var contradiction = Assert.IsType<Step.Contradiction.NoCandidateForCell>(
            Assert.Single(watcher.Drain().OfType<Step.Contradiction>()));
        Assert.Contains((contradiction.Row, contradiction.Column), new[] { (1, 9), (9, 9) });
        Assert.False(watcher.Completion.IsCompleted);
    }

    [Fact]
    public async Task A_digit_left_with_no_cell_in_a_unit_puts_the_game_in_contradiction_after_applying_the_move()
    {
        await using var game = await Broker.NewGameAsync();
        // Row 1 holds 1–6, so 9 can only go in (1,7), (1,8) or (1,9), all in the top-right box.
        for (var digit = 1; digit <= 6; digit++)
        {
            await game.MoveAsync(1, digit, digit);
        }

        var watcher = await game.WatchStepsAsync();

        // A legal move, but now 9 has no place in row 1. (1,7), (1,8) and (1,9) keep 7 and 8, so nothing is deduced.
        var outcome = await game.MoveAsync(2, 7, 9);

        Assert.IsType<MoveOutcome.Accepted>(outcome);
        Assert.Equal(9, game.Cell(2, 7).Digit);
        Assert.Equal(GameState.Contradicted, game.State);
        AssertNoCellForDigitUnlessACellRanOut(
            new Step.Contradiction.NoCellForDigit(UnitKind.Row, 1, 9),
            Assert.Single(watcher.Drain().OfType<Step.Contradiction>()),
            ranOutIn: (row, column) => row <= 3 && column >= 7);
        Assert.False(watcher.Completion.IsCompleted);
    }

    [Fact]
    public async Task A_digit_left_with_no_cell_in_a_box_names_the_box_by_its_number_row_by_row()
    {
        await using var game = await Broker.NewGameAsync();
        // Box 4 (rows 4–6, columns 1–3) holds 1–6 in columns 2 and 3, so (4,1), (5,1) and (6,1) are left with
        // 7, 8 and 9.
        for (var row = 4; row <= 6; row++)
        {
            await game.MoveAsync(row, 2, row - 3);
            await game.MoveAsync(row, 3, row);
        }

        var watcher = await game.WatchStepsAsync();

        // 8 in column 1, above box 4, leaves box 4 no cell for 8. (4,1), (5,1) and (6,1) keep 7 and 9, so nothing is
        // deduced, and columns 2 and 3 still have room for 8 below box 4.
        await game.MoveAsync(2, 1, 8);

        Assert.Equal(GameState.Contradicted, game.State);
        AssertNoCellForDigitUnlessACellRanOut(
            new Step.Contradiction.NoCellForDigit(UnitKind.Box, 4, 8),
            Assert.Single(watcher.Drain().OfType<Step.Contradiction>()),
            ranOutIn: (_, column) => column == 1);
    }

    [Fact]
    public async Task Every_move_in_a_contradicted_game_is_rejected_and_the_grid_stays_readable()
    {
        await using var game = await Contradicted();
        var watcher = await game.WatchStepsAsync();

        var elsewhere = await game.MoveAsync(5, 5, 5);
        var repeated = await game.MoveAsync(2, 7, 9);

        Assert.Contains("contradiction", Assert.IsType<MoveOutcome.Rejected>(elsewhere).Reason);
        Assert.IsType<MoveOutcome.Rejected>(repeated);
        Assert.Empty(watcher.Drain());
        Assert.Null(game.Cell(5, 5).Digit);
        Assert.Equal(81, game.Cells.Count());
    }

    [Fact]
    public async Task A_game_is_in_progress_while_cells_are_empty_and_solved_once_all_81_are_filled()
    {
        await using var game = await Broker.NewGameAsync();
        var givens = Puzzle.Givens.ToList();

        await game.MoveAsync(givens[0].Row, givens[0].Column, givens[0].Digit);
        Assert.Equal(GameState.InProgress, game.State);

        foreach (var (row, column, digit) in givens.Skip(1))
        {
            await game.MoveAsync(row, column, digit);
        }

        Assert.Equal(GameState.Solved, game.State);
        Assert.All(game.Cells, cell => Assert.NotNull(cell.Digit));
    }

    /// <summary>
    /// A unit left with one cell for a digit deduces it there. Usually that cell has already lost the digit and
    /// refuses, and the unit then finds no cell for it. But the elimination can still be on its way, and then the
    /// deduction is placed and a cell runs out of candidates instead: which contradiction wins can vary (ADR 0003).
    /// </summary>
    private static void AssertNoCellForDigitUnlessACellRanOut(
        Step.Contradiction.NoCellForDigit expected, Step.Contradiction actual, Func<int, int, bool> ranOutIn)
    {
        if (actual is Step.Contradiction.NoCandidateForCell ranOut)
        {
            Assert.True(ranOutIn(ranOut.Row, ranOut.Column), $"{ranOut} is not where a raced deduction could leave a cell.");
        }
        else
        {
            Assert.Equal(expected, actual);
        }
    }

    /// <summary>Row 1 holds 1–7, then 9 in (2,7) leaves 9 with no place in row 1.</summary>
    private static async Task<Game> Contradicted()
    {
        var game = await Broker.NewGameAsync();
        for (var digit = 1; digit <= 7; digit++)
        {
            await game.MoveAsync(1, digit, digit);
        }

        await game.MoveAsync(2, 7, 9);
        return game;
    }
}
