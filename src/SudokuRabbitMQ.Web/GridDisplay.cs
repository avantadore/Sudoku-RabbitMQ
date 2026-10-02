namespace SudokuRabbitMQ.Web;

/// <summary>How a game state and a placement source look on the page: the one place that maps them to CSS classes and text.</summary>
public static class GridDisplay
{
    public static string CssClass(this GameState state) => state switch
    {
        GameState.InProgress => "in-progress",
        GameState.Solved => "solved",
        GameState.Contradicted => "contradicted",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    public static string Label(this GameState state) => state switch
    {
        GameState.InProgress => "In progress",
        GameState.Solved => "Solved!",
        GameState.Contradicted => "Contradicted: this grid can no longer be solved. Replay to an earlier move to continue.",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    public static string CssClass(this PlacementSource source) => source switch
    {
        PlacementSource.Move => "move",
        PlacementSource.Deduction => "deduction",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };

    public static string Label(this PlacementSource source) => source switch
    {
        PlacementSource.Move => "Move",
        PlacementSource.Deduction => "Deduction",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };
}
