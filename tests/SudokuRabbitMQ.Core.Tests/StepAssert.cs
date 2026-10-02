namespace SudokuRabbitMQ.Core.Tests;

/// <summary>
/// Reading and checking watched steps. Within a cascade, steps promise only that cause comes before effect
/// (ADR 0003), so everything else is compared as a set.
/// </summary>
internal static class StepAssert
{
    /// <summary>
    /// The steps the watcher has read and not yet been taken. A move completes only once every watcher has read all
    /// of its steps, so after awaiting a move this is everything it did.
    /// </summary>
    public static List<Step> Drain(this StepWatcher watcher)
    {
        var steps = new List<Step>();
        while (watcher.TryRead(out var step))
        {
            steps.Add(step);
        }

        return steps;
    }

    public static void SameSet(IEnumerable<Step> expected, IEnumerable<Step> actual) =>
        Assert.Equal(InSomeFixedOrder(expected), InSomeFixedOrder(actual));

    public static void Before(IList<Step> steps, Step cause, Step effect)
    {
        var causeAt = steps.IndexOf(cause);
        var effectAt = steps.IndexOf(effect);
        Assert.True(causeAt >= 0, $"{cause} was not emitted.");
        Assert.True(effectAt >= 0, $"{effect} was not emitted.");
        Assert.True(causeAt < effectAt, $"{cause} should come before {effect}.");
    }

    private static List<string> InSomeFixedOrder(IEnumerable<Step> steps) =>
        [.. steps.Select(step => step.ToString()).Order(StringComparer.Ordinal)];
}
