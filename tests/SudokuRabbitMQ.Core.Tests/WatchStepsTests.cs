namespace SudokuRabbitMQ.Core.Tests;

public class WatchStepsTests
{
    [Fact]
    public async Task Several_watchers_each_receive_every_step_of_a_move()
    {
        await using var game = await Broker.NewGameAsync();
        var first = await game.WatchStepsAsync();
        var second = await game.WatchStepsAsync();

        await game.MoveAsync(1, 1, 5);

        var firstSteps = first.Drain();
        Assert.Equal(21, firstSteps.Count); // the placement and the 20 peers' eliminations
        StepAssert.SameSet(firstSteps, second.Drain());
    }

    [Fact]
    public async Task A_watcher_created_after_a_move_receives_only_later_steps()
    {
        await using var game = await Broker.NewGameAsync();
        await game.MoveAsync(1, 1, 5);

        var steps = await game.WatchStepsAsync();
        await game.MoveAsync(9, 9, 1);

        Assert.Equal(new Step.Placement(9, 9, 1, PlacementSource.Move), steps.Drain()[0]);
    }

    [Fact]
    public async Task A_watcher_from_before_a_replay_receives_none_of_the_replayed_steps_but_those_of_the_next_move()
    {
        await using var game = await Broker.NewGameAsync();
        await game.MoveAsync(1, 1, 5);
        await game.MoveAsync(2, 4, 5);
        var steps = await game.WatchStepsAsync();

        await game.ReplayToAsync(0);
        await game.ReplayToAsync(2);

        Assert.Empty(steps.Drain());

        await game.MoveAsync(9, 9, 1);

        var next = steps.Drain();
        Assert.Equal(new Step.Placement(9, 9, 1, PlacementSource.Move), next[0]);
        Assert.DoesNotContain(next, step => step is Step.Placement { Digit: not 1 });
    }

    [Fact]
    public async Task Disposing_a_game_completes_its_watchers_readers()
    {
        var game = await Broker.NewGameAsync();
        var steps = await game.WatchStepsAsync();
        await game.MoveAsync(1, 1, 5);

        await game.DisposeAsync();

        // Steps written before disposal can still be read, then the reader ends.
        Assert.NotEmpty(steps.Drain());
        await steps.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }
}
