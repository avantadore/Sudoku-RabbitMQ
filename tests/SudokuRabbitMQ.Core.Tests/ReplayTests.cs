namespace SudokuRabbitMQ.Core.Tests;

// A replay promises the same grid, not the same steps (ADR 0003). When the moves reach a contradiction, it promises
// only a contradicted game.
public class ReplayTests
{
    [Fact]
    public async Task Replaying_to_the_last_move_gives_the_same_grid_as_the_live_game()
    {
        await using var game = await Broker.NewGameAsync();
        foreach (var (row, column, digit) in Puzzle.Givens.Take(20))
        {
            await game.MoveAsync(row, column, digit);
        }

        var live = GridOf(game);
        var state = game.State;
        var moves = game.Moves.ToList();

        await game.ReplayToAsync(game.Moves.Count);

        Assert.Equal(live, GridOf(game));
        Assert.Equal(state, game.State);
        Assert.Equal(moves, game.Moves);
        Assert.Equal(moves.Count, game.Position);
    }

    [Fact]
    public async Task Replaying_back_keeps_the_later_moves_and_replaying_forward_again_restores_them()
    {
        await using var game = await Broker.NewGameAsync();
        await game.MoveAsync(1, 1, 5);
        var afterFirst = GridOf(game);
        await game.MoveAsync(2, 4, 5);
        await game.MoveAsync(5, 5, 7);
        var afterThird = GridOf(game);
        var moves = game.Moves.ToList();

        await game.ReplayToAsync(1);

        Assert.Equal(1, game.Position);
        Assert.Equal(afterFirst, GridOf(game));
        Assert.Equal(moves, game.Moves);

        await game.ReplayToAsync(0);

        Assert.Equal(0, game.Position);
        Assert.All(game.Cells, cell => Assert.Null(cell.Digit));
        Assert.Equal(moves, game.Moves);

        await game.ReplayToAsync(3);

        Assert.Equal(3, game.Position);
        Assert.Equal(afterThird, GridOf(game));
    }

    [Fact]
    public async Task A_new_move_at_an_earlier_position_discards_the_later_moves()
    {
        await using var game = await ThreeMoves();
        await game.ReplayToAsync(1);

        var outcome = await game.MoveAsync(9, 9, 1);

        Assert.IsType<MoveOutcome.Accepted>(outcome);
        Assert.Equal([new RecordedMove(1, 1, 5, 0), new RecordedMove(9, 9, 1, 0)], game.Moves);
        Assert.Equal(2, game.Position);
        Assert.Null(game.Cell(2, 4).Digit);
    }

    [Fact]
    public async Task Placing_exactly_the_next_recorded_move_is_a_new_move_that_discards_the_ones_after_it()
    {
        await using var game = await ThreeMoves();
        await game.ReplayToAsync(1);

        await game.MoveAsync(2, 4, 5);

        Assert.Equal([new RecordedMove(1, 1, 5, 0), new RecordedMove(2, 4, 5, 0)], game.Moves);
        Assert.Equal(2, game.Position);
    }

    [Fact]
    public async Task An_unchanged_or_rejected_move_at_an_earlier_position_keeps_the_later_moves()
    {
        await using var game = await ThreeMoves();
        var moves = game.Moves.ToList();
        await game.ReplayToAsync(1);

        var unchanged = await game.MoveAsync(1, 1, 5);
        var rejected = await game.MoveAsync(1, 2, 5);

        Assert.IsType<MoveOutcome.Unchanged>(unchanged);
        Assert.IsType<MoveOutcome.Rejected>(rejected);
        Assert.Equal(moves, game.Moves);
        Assert.Equal(1, game.Position);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public async Task Replaying_outside_0_to_the_number_of_moves_throws(int position)
    {
        await using var game = await ThreeMoves();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => game.ReplayToAsync(position));
        Assert.Equal(3, game.Position);
    }

    [Fact]
    public async Task Two_fresh_games_given_the_same_moves_reach_the_same_grid()
    {
        await using var first = await Broker.NewGameAsync();
        await using var second = await Broker.NewGameAsync();

        foreach (var game in new[] { first, second })
        {
            foreach (var (row, column, digit) in Puzzle.Givens.Take(20))
            {
                await game.MoveAsync(row, column, digit);
            }
        }

        Assert.Equal(GridOf(first), GridOf(second));
        Assert.Equal(first.Moves, second.Moves);
    }

    [Fact]
    public async Task Replaying_to_a_move_into_contradiction_gives_a_contradicted_game()
    {
        await using var game = await Contradicted();

        await game.ReplayToAsync(game.Moves.Count);

        Assert.Equal(GameState.Contradicted, game.State);
    }

    [Fact]
    public async Task Replaying_to_before_the_move_into_contradiction_makes_the_game_playable_again()
    {
        await using var game = await Contradicted();
        await game.ReplayToAsync(game.Moves.Count);

        await game.ReplayToAsync(game.Moves.Count - 1);

        Assert.Equal(GameState.InProgress, game.State);
        Assert.IsType<MoveOutcome.Accepted>(await game.MoveAsync(1, 8, 9));
    }

    [Fact]
    public async Task A_move_in_a_contradicted_game_is_rejected_with_a_reason_that_points_to_replaying()
    {
        await using var game = await Contradicted();

        var rejected = Assert.IsType<MoveOutcome.Rejected>(await game.MoveAsync(5, 5, 5));

        Assert.Contains("replay to an earlier position", rejected.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<Game> ThreeMoves()
    {
        var game = await Broker.NewGameAsync();
        await game.MoveAsync(1, 1, 5);
        await game.MoveAsync(2, 4, 5);
        await game.MoveAsync(5, 5, 7);
        return game;
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

    private static List<string> GridOf(Game game) =>
        [.. game.Cells.Select(cell => $"{cell.Row},{cell.Column}: {cell.Digit} {cell.Source} [{string.Join("", cell.Candidates)}]")];
}
