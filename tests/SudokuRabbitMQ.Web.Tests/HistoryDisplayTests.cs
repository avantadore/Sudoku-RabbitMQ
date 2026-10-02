namespace SudokuRabbitMQ.Web.Tests;

public class HistoryDisplayTests
{
    [Fact]
    public void A_new_game_has_only_the_empty_grid_row_and_it_is_current()
    {
        var row = Assert.Single(HistoryDisplay.Rows(position: 0, moves: []));

        Assert.Equal(new HistoryRow(0, "Empty grid", null, null, HistoryRowState.Current), row);
    }

    [Fact]
    public void Each_move_is_numbered_with_its_cell_and_digit_and_a_suffix_only_when_it_made_deductions()
    {
        GridMove[] moves = [new(1, 1, 5, 0), new(2, 3, 8, 1), new(4, 7, 2, 5)];

        var rows = HistoryDisplay.Rows(position: 3, moves);

        HistoryRow[] expected =
        [
            new(0, "Empty grid", null, null, HistoryRowState.Applied),
            new(1, "1. r1c1 = 5", null, null, HistoryRowState.Applied),
            new(2, "2. r2c3 = 8", "(+1)", "1 deduction", HistoryRowState.Applied),
            new(3, "3. r4c7 = 2", "(+5)", "5 deductions", HistoryRowState.Current),
        ];
        Assert.Equal(expected, rows);
    }

    [Fact]
    public void Moves_after_the_position_are_kept_as_rows_after_the_current_one()
    {
        GridMove[] moves = [new(1, 1, 5, 0), new(2, 3, 8, 1), new(4, 7, 2, 5)];

        var states = HistoryDisplay.Rows(position: 1, moves).Select(row => row.State);

        Assert.Equal([HistoryRowState.Applied, HistoryRowState.Current, HistoryRowState.After, HistoryRowState.After], states);
    }

    // The classes are the ones MoveHistory.razor.css styles.
    [Theory]
    [InlineData(HistoryRowState.Applied, "applied")]
    [InlineData(HistoryRowState.Current, "current")]
    [InlineData(HistoryRowState.After, "after")]
    public void A_history_row_state_is_shown_with_its_class(HistoryRowState state, string cssClass) =>
        Assert.Equal(cssClass, state.CssClass());

    [Theory]
    [InlineData(0, 0, false, false, false)]
    [InlineData(0, 2, false, false, true)]
    [InlineData(1, 2, false, true, true)]
    [InlineData(2, 2, false, true, false)]
    [InlineData(1, 2, true, false, false)]
    public void Replaying_back_and_forward_is_possible_between_the_empty_grid_and_the_last_move_while_not_busy(
        int position, int moves, bool busy, bool canReplayBack, bool canReplayForward)
    {
        Assert.Equal(canReplayBack, HistoryDisplay.CanReplayBack(position, busy));
        Assert.Equal(canReplayForward, HistoryDisplay.CanReplayForward(position, moves, busy));
    }
}
