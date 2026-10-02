namespace SudokuRabbitMQ.Web.Tests;

/// <summary>
/// The API sends Core's enums by name, and the Web reads them into its own copies.
/// Renaming, adding or removing a member on one side only must fail here, not in the browser.
/// </summary>
public class EnumContractTests
{
    [Fact]
    public void The_web_knows_every_game_state_the_api_can_send() =>
        Assert.Equal(Enum.GetNames<Core.GameState>(), Enum.GetNames<GameState>());

    [Fact]
    public void The_web_knows_every_placement_source_the_api_can_send() =>
        Assert.Equal(Enum.GetNames<Core.PlacementSource>(), Enum.GetNames<PlacementSource>());
}
