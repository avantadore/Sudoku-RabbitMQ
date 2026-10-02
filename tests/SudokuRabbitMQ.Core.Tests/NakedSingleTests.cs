namespace SudokuRabbitMQ.Core.Tests;

public class NakedSingleTests
{
    [Fact]
    public async Task A_cell_left_with_one_candidate_is_filled_as_a_deduction_and_its_peers_eliminate_the_digit()
    {
        await using var game = await Broker.NewGameAsync();
        // Row 1 holds 1–7, so (1,8) and (1,9) are left with 8 and 9.
        for (var digit = 1; digit <= 7; digit++)
        {
            await game.MoveAsync(1, digit, digit);
        }

        await game.MoveAsync(1, 8, 8);

        var deduced = game.Cell(1, 9);
        Assert.Equal(9, deduced.Digit);
        Assert.Equal(PlacementSource.Deduction, deduced.Source);
        Assert.Equal([9], deduced.Candidates);
        Assert.DoesNotContain(9, game.Cell(5, 9).Candidates);
        Assert.DoesNotContain(9, game.Cell(2, 7).Candidates);
    }
}
