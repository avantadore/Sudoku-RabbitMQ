namespace SudokuRabbitMQ.Web.Tests;

public class GridDisplayTests
{
    // The classes are the ones Home.razor.css and Board.razor.css style.
    [Theory]
    [InlineData(GameState.InProgress, "in-progress", "In progress")]
    [InlineData(GameState.Solved, "solved", "Solved!")]
    [InlineData(GameState.Contradicted, "contradicted", "Contradicted: this grid can no longer be solved. Replay to an earlier move to continue.")]
    public void A_game_state_is_shown_with_its_class_and_label(GameState state, string cssClass, string label)
    {
        Assert.Equal(cssClass, state.CssClass());
        Assert.Equal(label, state.Label());
    }

    [Theory]
    [InlineData(PlacementSource.Move, "move", "Move")]
    [InlineData(PlacementSource.Deduction, "deduction", "Deduction")]
    public void A_placement_source_is_shown_with_its_class_and_label(PlacementSource source, string cssClass, string label)
    {
        Assert.Equal(cssClass, source.CssClass());
        Assert.Equal(label, source.Label());
    }

    [Fact]
    public void Every_game_state_and_placement_source_has_a_class_and_label()
    {
        Assert.All(Enum.GetValues<GameState>(), state => Assert.All([state.CssClass(), state.Label()], Assert.NotEmpty));
        Assert.All(Enum.GetValues<PlacementSource>(), source => Assert.All([source.CssClass(), source.Label()], Assert.NotEmpty));
    }
}
