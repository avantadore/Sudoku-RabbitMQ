namespace SudokuRabbitMQ.Core.Tests;

// Within a cascade, steps promise only that cause comes before effect (ADR 0003): a move's placement comes first,
// each elimination comes after the placement that caused it, and each deduction comes after the step that forced
// it. Everything else is compared as a set.
public class StepTests
{
    [Fact]
    public async Task A_move_emits_the_move_first_then_an_elimination_for_each_peer()
    {
        await using var game = await Broker.NewGameAsync();
        var watcher = await game.WatchStepsAsync();

        await game.MoveAsync(1, 1, 5);

        var steps = watcher.Drain();
        Assert.Equal(new Step.Placement(1, 1, 5, PlacementSource.Move), steps[0]);
        Step[] eliminations =
        [
            new Step.Elimination(1, 2, 5), new Step.Elimination(1, 3, 5), new Step.Elimination(1, 4, 5),
            new Step.Elimination(1, 5, 5), new Step.Elimination(1, 6, 5), new Step.Elimination(1, 7, 5),
            new Step.Elimination(1, 8, 5), new Step.Elimination(1, 9, 5),
            new Step.Elimination(2, 1, 5), new Step.Elimination(2, 2, 5), new Step.Elimination(2, 3, 5),
            new Step.Elimination(3, 1, 5), new Step.Elimination(3, 2, 5), new Step.Elimination(3, 3, 5),
            new Step.Elimination(4, 1, 5), new Step.Elimination(5, 1, 5), new Step.Elimination(6, 1, 5),
            new Step.Elimination(7, 1, 5), new Step.Elimination(8, 1, 5), new Step.Elimination(9, 1, 5),
        ];
        StepAssert.SameSet(eliminations, steps.Skip(1));
    }

    [Fact]
    public async Task A_move_emits_eliminations_only_for_peers_that_still_had_the_digit()
    {
        await using var game = await Broker.NewGameAsync();
        await game.MoveAsync(1, 1, 5);
        var watcher = await game.WatchStepsAsync();

        await game.MoveAsync(2, 4, 5);

        // (2,1), (2,2) and (2,3) already lost 5 to (1,1), the box of (2,4) has lost row 1, and (1,4) lost it too.
        var steps = watcher.Drain();
        Assert.Equal(new Step.Placement(2, 4, 5, PlacementSource.Move), steps[0]);
        Step[] eliminations =
        [
            new Step.Elimination(2, 5, 5), new Step.Elimination(2, 6, 5), new Step.Elimination(2, 7, 5),
            new Step.Elimination(2, 8, 5), new Step.Elimination(2, 9, 5),
            new Step.Elimination(3, 4, 5), new Step.Elimination(3, 5, 5), new Step.Elimination(3, 6, 5),
            new Step.Elimination(4, 4, 5), new Step.Elimination(5, 4, 5), new Step.Elimination(6, 4, 5),
            new Step.Elimination(7, 4, 5), new Step.Elimination(8, 4, 5), new Step.Elimination(9, 4, 5),
        ];
        StepAssert.SameSet(eliminations, steps.Skip(1));
    }

    [Fact]
    public async Task A_cascade_emits_each_deduction_after_the_elimination_that_forced_it_and_before_its_own_eliminations()
    {
        await using var game = await Broker.NewGameAsync();
        for (var digit = 1; digit <= 7; digit++)
        {
            await game.MoveAsync(1, digit, digit);
        }

        var watcher = await game.WatchStepsAsync();

        await game.MoveAsync(1, 8, 8);

        var steps = watcher.Drain();
        var move = new Step.Placement(1, 8, 8, PlacementSource.Move);
        var forcing = new Step.Elimination(1, 9, 8); // leaves (1,9) with only 9
        var deduction = new Step.Placement(1, 9, 9, PlacementSource.Deduction);
        Step[] eliminationsOf8 =
        [
            forcing,
            new Step.Elimination(2, 7, 8), new Step.Elimination(2, 8, 8), new Step.Elimination(2, 9, 8),
            new Step.Elimination(3, 7, 8), new Step.Elimination(3, 8, 8), new Step.Elimination(3, 9, 8),
            new Step.Elimination(4, 8, 8), new Step.Elimination(5, 8, 8), new Step.Elimination(6, 8, 8),
            new Step.Elimination(7, 8, 8), new Step.Elimination(8, 8, 8), new Step.Elimination(9, 8, 8),
        ];
        Step[] eliminationsOf9 =
        [
            new Step.Elimination(2, 7, 9), new Step.Elimination(2, 8, 9), new Step.Elimination(2, 9, 9),
            new Step.Elimination(3, 7, 9), new Step.Elimination(3, 8, 9), new Step.Elimination(3, 9, 9),
            new Step.Elimination(4, 9, 9), new Step.Elimination(5, 9, 9), new Step.Elimination(6, 9, 9),
            new Step.Elimination(7, 9, 9), new Step.Elimination(8, 9, 9), new Step.Elimination(9, 9, 9),
        ];
        Assert.Equal(move, steps[0]);
        StepAssert.SameSet([move, .. eliminationsOf8, deduction, .. eliminationsOf9], steps);
        StepAssert.Before(steps, forcing, deduction);
        Assert.All(eliminationsOf9, elimination => StepAssert.Before(steps, deduction, elimination));
    }

    [Fact]
    public async Task Deductions_forced_by_the_same_elimination_each_come_after_it()
    {
        await using var game = await Broker.NewGameAsync();
        for (var digit = 1; digit <= 6; digit++)
        {
            await game.MoveAsync(1, digit, digit);
        }

        await game.MoveAsync(7, 8, 9); // (1,8) is left with 7 and 8
        await game.MoveAsync(4, 9, 9); // (1,9) is left with 7 and 8, so 9 is a hidden single at (1,7)
        Assert.Equal(PlacementSource.Deduction, game.Cell(1, 7).Source);
        var watcher = await game.WatchStepsAsync();

        // Eliminating 7 from (1,9) forces two deductions: (1,9) is a naked single for 8, and (1,8) is the
        // only cell left for 7 in row 1.
        await game.MoveAsync(9, 9, 7);

        var steps = watcher.Drain();
        var forcing = new Step.Elimination(1, 9, 7);
        Step[] deductions =
        [
            new Step.Placement(1, 9, 8, PlacementSource.Deduction),
            new Step.Placement(1, 8, 7, PlacementSource.Deduction),
        ];
        Assert.Equal(new Step.Placement(9, 9, 7, PlacementSource.Move), steps[0]);
        StepAssert.SameSet(deductions, steps.OfType<Step.Placement>().Skip(1));
        Assert.All(deductions, deduction => StepAssert.Before(steps, forcing, deduction));
    }

    [Fact]
    public async Task Rejected_and_unchanged_moves_emit_nothing()
    {
        await using var game = await Broker.NewGameAsync();
        await game.MoveAsync(1, 1, 5);
        var watcher = await game.WatchStepsAsync();

        await game.MoveAsync(1, 1, 5);
        await game.MoveAsync(1, 1, 6);
        await game.MoveAsync(1, 2, 5);

        Assert.Empty(watcher.Drain());
    }
}
