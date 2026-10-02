namespace SudokuRabbitMQ.Core.Tests;

public class ConcurrencyTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_move_made_while_another_moves_cascade_is_running_waits_its_turn_and_then_applies()
    {
        await using var game = await Broker.NewGameAsync();
        foreach (var (row, column, digit) in Puzzle.Givens)
        {
            await game.MoveAsync(row, column, digit);
        }

        // Go back to just before the move with the longest cascade, so it is still running when the next move comes.
        var longest = game.Moves.MaxBy(move => move.Deductions)!;
        await game.ReplayToAsync(game.Moves.ToList().IndexOf(longest));

        var first = game.MoveAsync(longest.Row, longest.Column, longest.Digit);
        Assert.SkipWhen(first.IsCompleted, "The first move's cascade finished before the second move could be made.");
        var second = game.MoveAsync(longest.Row, longest.Column, longest.Digit);

        Assert.IsType<MoveOutcome.Accepted>(await first);
        Assert.IsType<MoveOutcome.Unchanged>(await second); // so it saw the first move's placement
        Assert.Equal(longest, game.Moves[^1]);
    }

    [Fact]
    public async Task Moves_made_at_the_same_time_are_applied_one_after_the_other()
    {
        await using var game = await Broker.NewGameAsync();

        // 1–7 in row 1, which leaves (1,8) and (1,9) with 8 and 9 whatever order the moves are applied in.
        var outcomes = await Task.WhenAll(
            Enumerable.Range(1, 7).Select(column => Task.Run(() => game.MoveAsync(1, column, column), Cancellation)));

        Assert.All(outcomes, outcome => Assert.IsType<MoveOutcome.Accepted>(outcome));
        Assert.Equal(7, game.Moves.Count);
        Assert.All(game.Moves, move => Assert.Equal(0, move.Deductions));
        Assert.Equal(Enumerable.Range(1, 7), Enumerable.Range(1, 7).Select(column => (int)game.Cell(1, column).Digit!));
        Assert.Equal([8, 9], game.Cell(1, 8).Candidates);
        Assert.Equal([8, 9], game.Cell(1, 9).Candidates);
    }

    [Fact]
    public async Task Disposing_a_game_after_moves_and_replays_completes()
    {
        var game = await Broker.NewGameAsync();
        foreach (var (row, column, digit) in Puzzle.Givens.Take(20))
        {
            await game.MoveAsync(row, column, digit);
        }

        await game.ReplayToAsync(5);
        await game.ReplayToAsync(20);

        await game.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
    }

    [Fact]
    public async Task A_disposed_game_accepts_no_more_moves()
    {
        var game = await Broker.NewGameAsync();
        await game.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => game.MoveAsync(1, 1, 1));
    }
}
