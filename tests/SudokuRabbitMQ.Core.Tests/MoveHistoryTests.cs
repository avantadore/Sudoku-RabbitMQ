namespace SudokuRabbitMQ.Core.Tests;

public class MoveHistoryTests
{
    [Fact]
    public async Task A_new_game_has_no_moves_and_is_at_position_0()
    {
        await using var game = await Broker.NewGameAsync();

        Assert.Empty(game.Moves);
        Assert.Equal(0, game.Position);
    }

    [Fact]
    public async Task Accepted_moves_are_recorded_in_order_with_how_many_deductions_their_cascade_made()
    {
        await using var game = await Broker.NewGameAsync();
        for (var digit = 1; digit <= 7; digit++)
        {
            await game.MoveAsync(1, digit, digit);
        }

        // Leaves (1,9) with only 9, which the game deduces.
        await game.MoveAsync(1, 8, 8);

        RecordedMove[] expected =
        [
            new(1, 1, 1, 0), new(1, 2, 2, 0), new(1, 3, 3, 0), new(1, 4, 4, 0),
            new(1, 5, 5, 0), new(1, 6, 6, 0), new(1, 7, 7, 0), new(1, 8, 8, 1),
        ];
        Assert.Equal(expected, game.Moves);
        Assert.Equal(8, game.Position);
    }

    [Fact]
    public async Task Rejected_and_unchanged_moves_are_not_recorded()
    {
        await using var game = await Broker.NewGameAsync();
        await game.MoveAsync(1, 1, 5);

        await game.MoveAsync(1, 1, 5); // unchanged
        await game.MoveAsync(1, 1, 6); // already filled
        await game.MoveAsync(1, 2, 5); // not a candidate

        Assert.Equal([new RecordedMove(1, 1, 5, 0)], game.Moves);
        Assert.Equal(1, game.Position);
    }

    [Fact]
    public async Task A_move_into_contradiction_is_recorded_with_the_deductions_placed_before_its_cascade_stopped()
    {
        await using var game = await Broker.NewGameAsync();
        // Rows 1 and 9 hold 1–7 in columns 1–7, so (1,8), (1,9), (9,8) and (9,9) are left with 8 and 9.
        for (var column = 1; column <= 7; column++)
        {
            await game.MoveAsync(1, column, column);
            await game.MoveAsync(9, column, column % 7 + 1);
        }

        var watcher = await game.WatchStepsAsync();

        // 9 in column 9 leaves (1,9) and (9,9) with only 8, so 8 is deduced in one of them, which leaves the other
        // with no candidates. How much else is deduced before the contradiction stops the cascade can vary.
        await game.MoveAsync(5, 9, 9);

        var deductions = watcher.Drain().Count(step => step is Step.Placement { Source: PlacementSource.Deduction });
        Assert.Equal(GameState.Contradicted, game.State);
        Assert.InRange(deductions, 1, 81);
        Assert.Equal(new RecordedMove(5, 9, 9, deductions), game.Moves[^1]);
        Assert.Equal(15, game.Position);
    }
}
