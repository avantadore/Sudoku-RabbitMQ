using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SudokuRabbitMQ.Core.Tests;

namespace SudokuRabbitMQ.Api.Tests;

public class GameEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Creating_a_game_returns_201_with_its_location_and_grid()
    {
        var response = await _client.PostAsync("/games", content: null, Cancellation);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var grid = await ReadJson(response);
        var id = grid.GetProperty("id").GetString();
        Assert.Equal($"/games/{id}", response.Headers.Location?.OriginalString);
        Assert.Equal("InProgress", grid.GetProperty("state").GetString());
        AssertGridShape(grid);
        Assert.Equal(0, grid.GetProperty("position").GetInt32());
        Assert.Empty(grid.GetProperty("moves").EnumerateArray());
    }

    [Fact]
    public async Task A_created_game_can_be_read_back_by_its_id()
    {
        var id = await CreateGame();

        var response = await _client.GetAsync($"/games/{id}", Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var grid = await ReadJson(response);
        Assert.Equal(id, grid.GetProperty("id").GetString());
        AssertGridShape(grid);
    }

    [Fact]
    public async Task Reading_an_unknown_game_returns_404()
    {
        var response = await _client.GetAsync($"/games/{Guid.NewGuid()}", Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reading_a_cells_candidates_in_a_new_game_returns_1_to_9()
    {
        var id = await CreateGame();

        var response = await _client.GetAsync($"/games/{id}/cells/4/7/candidates", Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9], DigitsOf(await ReadJson(response)));
    }

    [Fact]
    public async Task Reading_a_filled_cells_candidates_returns_its_digit()
    {
        var id = await CreateGame();
        await Move(id, 4, 7, 3);

        var response = await _client.GetAsync($"/games/{id}/cells/4/7/candidates", Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([3], DigitsOf(await ReadJson(response)));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(10, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 10)]
    public async Task Reading_candidates_outside_rows_and_columns_1_to_9_returns_400(int row, int column)
    {
        var id = await CreateGame();

        var response = await _client.GetAsync($"/games/{id}/cells/{row}/{column}/candidates", Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertProblemDetails(response);
    }

    [Fact]
    public async Task Reading_candidates_of_an_unknown_game_returns_404()
    {
        var response = await _client.GetAsync($"/games/{Guid.NewGuid()}/cells/1/1/candidates", Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_move_returns_200_with_the_updated_grid()
    {
        var id = await CreateGame();

        var response = await Move(id, 2, 3, 8);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var grid = await ReadJson(response);
        Assert.Equal(id, grid.GetProperty("id").GetString());
        AssertGridShape(grid);
        var cell = CellOf(grid, 2, 3);
        Assert.Equal(8, cell.GetProperty("digit").GetInt32());
        Assert.Equal("Move", cell.GetProperty("source").GetString());
    }

    [Fact]
    public async Task A_deduced_cell_reports_its_source_as_deduction()
    {
        var id = await CreateGame();
        for (var digit = 1; digit <= 7; digit++)
        {
            await Move(id, 1, digit, digit);
        }

        // Leaves (1,9) with a single candidate, which the game places itself.
        var response = await Move(id, 1, 8, 8);

        var grid = await ReadJson(response);
        Assert.Equal("Move", CellOf(grid, 1, 8).GetProperty("source").GetString());
        Assert.Equal("Deduction", CellOf(grid, 1, 9).GetProperty("source").GetString());
    }

    [Fact]
    public async Task Every_grid_carries_the_position_and_the_moves_with_their_deductions()
    {
        var id = await CreateGame();
        for (var digit = 1; digit <= 7; digit++)
        {
            await Move(id, 1, digit, digit);
        }

        var moved = await ReadJson(await Move(id, 1, 8, 8)); // deduces 9 at (1,9)
        var read = await ReadJson(await _client.GetAsync($"/games/{id}", Cancellation));

        foreach (var grid in new[] { moved, read })
        {
            Assert.Equal(8, grid.GetProperty("position").GetInt32());
            var moves = grid.GetProperty("moves").EnumerateArray().ToList();
            Assert.Equal(8, moves.Count);
            Assert.Equal(
                """{"row":1,"column":8,"digit":8,"deductions":1}""",
                moves[^1].GetRawText());
            Assert.Equal(0, moves[0].GetProperty("deductions").GetInt32());
        }
    }

    [Fact]
    public async Task Setting_the_position_replays_the_grid_back_and_forward_and_keeps_every_move()
    {
        var id = await CreateGame();
        await Move(id, 1, 1, 5);
        await Move(id, 2, 4, 5);

        var back = await SetPosition(id, 1);
        var backGrid = await ReadJson(back);
        var forward = await ReadJson(await SetPosition(id, 2));

        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
        Assert.Equal(1, backGrid.GetProperty("position").GetInt32());
        Assert.Equal(2, backGrid.GetProperty("moves").GetArrayLength());
        Assert.Equal(5, CellOf(backGrid, 1, 1).GetProperty("digit").GetInt32());
        Assert.Equal(JsonValueKind.Null, CellOf(backGrid, 2, 4).GetProperty("digit").ValueKind);
        AssertGridShape(backGrid);
        Assert.Equal(2, forward.GetProperty("position").GetInt32());
        Assert.Equal(5, CellOf(forward, 2, 4).GetProperty("digit").GetInt32());
    }

    [Fact]
    public async Task Setting_the_same_position_twice_gives_the_same_grid()
    {
        var id = await CreateGame();
        await Move(id, 1, 1, 5);
        await Move(id, 2, 4, 5);

        var first = await ReadJson(await SetPosition(id, 1));
        var second = await ReadJson(await SetPosition(id, 1));

        Assert.Equal(first.GetRawText(), second.GetRawText());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public async Task Setting_a_position_outside_0_to_the_number_of_moves_returns_400_keyed_position(int position)
    {
        var id = await CreateGame();
        await Move(id, 1, 1, 5);
        await Move(id, 2, 4, 5);

        var response = await SetPosition(id, position);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await AssertProblemDetails(response);
        Assert.Equal(["Position"], problem.GetProperty("errors").EnumerateObject().Select(error => error.Name));
    }

    [Fact]
    public async Task Setting_the_position_of_an_unknown_game_returns_404()
    {
        var response = await SetPosition(Guid.NewGuid().ToString(), 0);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_move_at_an_earlier_position_discards_the_later_moves_but_a_rejected_one_does_not()
    {
        var id = await CreateGame();
        await Move(id, 1, 1, 5);
        await Move(id, 2, 4, 5);
        await Move(id, 5, 5, 7);
        await SetPosition(id, 1);

        var rejected = await Move(id, 1, 2, 5); // 5 is no longer a candidate in row 1
        var kept = await ReadJson(await _client.GetAsync($"/games/{id}", Cancellation));
        var accepted = await ReadJson(await Move(id, 9, 9, 1));

        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal(1, kept.GetProperty("position").GetInt32());
        Assert.Equal(3, kept.GetProperty("moves").GetArrayLength());
        Assert.Equal(2, accepted.GetProperty("position").GetInt32());
        Assert.Equal(
            """[{"row":1,"column":1,"digit":5,"deductions":0},{"row":9,"column":9,"digit":1,"deductions":0}]""",
            accepted.GetProperty("moves").GetRawText());
    }

    [Fact]
    public async Task A_move_into_contradiction_returns_200_with_the_state_and_later_moves_return_409()
    {
        var id = await CreateGame();
        for (var digit = 1; digit <= 7; digit++)
        {
            await Move(id, 1, digit, digit);
        }

        // Legal, but leaves 9 with no place in row 1.
        var contradicting = await Move(id, 2, 7, 9);
        var later = await Move(id, 5, 5, 5);

        Assert.Equal(HttpStatusCode.OK, contradicting.StatusCode);
        Assert.Equal("Contradicted", (await ReadJson(contradicting)).GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.Conflict, later.StatusCode);
        var problem = await AssertProblemDetails(later);
        Assert.Equal(await ContradictedReason(), problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Setting_the_position_to_before_the_move_into_contradiction_lets_the_game_accept_moves_again()
    {
        var id = await CreateGame();
        for (var digit = 1; digit <= 7; digit++)
        {
            await Move(id, 1, digit, digit);
        }

        await Move(id, 2, 7, 9); // leaves 9 with no place in row 1

        var replayed = await ReadJson(await SetPosition(id, 7));
        var moved = await Move(id, 1, 8, 9);

        Assert.Equal("InProgress", replayed.GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal(9, CellOf(await ReadJson(moved), 1, 8).GetProperty("digit").GetInt32());
    }

    /// <summary>The reason Core itself gives for rejecting a move in a contradicted game.</summary>
    private static async Task<string> ContradictedReason()
    {
        await using var game = await Broker.NewGameAsync();
        for (var digit = 1; digit <= 7; digit++)
        {
            await game.MoveAsync(1, digit, digit);
        }

        await game.MoveAsync(2, 7, 9);
        return Assert.IsType<Core.MoveOutcome.Rejected>(await game.MoveAsync(5, 5, 5)).Reason;
    }

    [Fact]
    public async Task A_game_reports_in_progress_while_cells_are_empty_and_solved_once_all_81_are_filled()
    {
        var id = await CreateGame();

        var responses = new List<HttpResponseMessage>();
        foreach (var (row, column, digit) in Puzzle.Givens)
        {
            responses.Add(await Move(id, row, column, digit));
        }

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal("InProgress", (await ReadJson(responses[0])).GetProperty("state").GetString());
        Assert.Equal("Solved", (await ReadJson(responses[^1])).GetProperty("state").GetString());
    }

    [Fact]
    public async Task Repeating_a_move_returns_200_with_the_unchanged_grid()
    {
        var id = await CreateGame();
        var first = await ReadJson(await Move(id, 2, 3, 8));

        var response = await Move(id, 2, 3, 8);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(first.GetRawText(), (await ReadJson(response)).GetRawText());
    }

    [Fact]
    public async Task A_rejected_move_returns_409_with_the_reason()
    {
        var id = await CreateGame();
        await Move(id, 2, 3, 8);

        var response = await Move(id, 2, 3, 9);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await AssertProblemDetails(response);
        Assert.Contains("already holds 8", problem.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(10, 1, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 10, 1)]
    [InlineData(1, 1, 0)]
    [InlineData(1, 1, 10)]
    public async Task A_move_outside_1_to_9_returns_400(int row, int column, int digit)
    {
        var id = await CreateGame();

        var response = await Move(id, row, column, digit);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertProblemDetails(response);
    }

    [Fact]
    public async Task A_move_in_an_unknown_game_returns_404()
    {
        var response = await Move(Guid.NewGuid().ToString(), 1, 1, 1);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<string> CreateGame()
    {
        var response = await _client.PostAsync("/games", content: null, Cancellation);
        response.EnsureSuccessStatusCode();
        return (await ReadJson(response)).GetProperty("id").GetString()!;
    }

    private Task<HttpResponseMessage> Move(string id, int row, int column, int digit) =>
        _client.PutAsJsonAsync($"/games/{id}/cells/{row}/{column}", new { digit }, Cancellation);

    private Task<HttpResponseMessage> SetPosition(string id, int position) =>
        _client.PutAsJsonAsync($"/games/{id}/position", new { position }, Cancellation);

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync(Cancellation);
        return JsonDocument.Parse(json).RootElement;
    }

    private static async Task<JsonElement> AssertProblemDetails(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await ReadJson(response);
        Assert.Equal((int)response.StatusCode, problem.GetProperty("status").GetInt32());
        return problem;
    }

    private static IEnumerable<int> DigitsOf(JsonElement array) => array.EnumerateArray().Select(digit => digit.GetInt32());

    private static JsonElement CellOf(JsonElement grid, int row, int column) =>
        grid.GetProperty("cells").EnumerateArray()
            .Single(cell => cell.GetProperty("row").GetInt32() == row && cell.GetProperty("column").GetInt32() == column);

    /// <summary>The grid representation: a flat list of 81 cells, each carrying its own coordinates.</summary>
    private static void AssertGridShape(JsonElement grid)
    {
        Assert.Equal(JsonValueKind.String, grid.GetProperty("state").ValueKind);

        var cells = grid.GetProperty("cells").EnumerateArray().ToList();
        Assert.Equal(81, cells.Count);
        Assert.Equal(81, cells.Select(cell => (cell.GetProperty("row").GetInt32(), cell.GetProperty("column").GetInt32())).Distinct().Count());
        Assert.All(cells, cell =>
        {
            Assert.InRange(cell.GetProperty("row").GetInt32(), 1, 9);
            Assert.InRange(cell.GetProperty("column").GetInt32(), 1, 9);
            Assert.Contains(cell.GetProperty("digit").ValueKind, new[] { JsonValueKind.Null, JsonValueKind.Number });
            Assert.Contains(cell.GetProperty("source").ValueKind, new[] { JsonValueKind.Null, JsonValueKind.String });
            Assert.All(cell.GetProperty("candidates").EnumerateArray(), digit => Assert.Equal(JsonValueKind.Number, digit.ValueKind));
        });
    }
}
