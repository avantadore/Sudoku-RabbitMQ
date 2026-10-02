using System.Net;
using System.Text;
using System.Text.Json;

namespace SudokuRabbitMQ.Web.Tests;

public class SudokuApiTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_grids_state_and_sources_are_read_into_enums()
    {
        var api = ApiReturning(GridJson(state: "Solved", source: "\"Deduction\""));

        var grid = await api.NewGameAsync(Cancellation);

        Assert.Equal(GameState.Solved, grid.State);
        Assert.Equal(PlacementSource.Deduction, grid.Cells[0].Source);
        Assert.Null(grid.Cells[1].Source);
    }

    [Fact]
    public async Task A_grids_position_and_moves_are_read_including_moves_after_the_position()
    {
        var api = ApiReturning(GridJson(state: "InProgress", source: "\"Move\""));

        var grid = await api.NewGameAsync(Cancellation);

        Assert.Equal(1, grid.Position);
        Assert.Equal([new GridMove(1, 1, 5, 0), new GridMove(4, 7, 2, 3)], grid.Moves);
    }

    [Fact]
    public async Task Setting_the_position_puts_it_to_the_game_and_reads_the_replayed_grid()
    {
        var handler = new StubHandler(GridJson(state: "InProgress", source: "\"Move\""));
        var api = new SudokuApi(new HttpClient(handler) { BaseAddress = new Uri("http://api") });
        var id = Guid.NewGuid();

        var grid = await api.SetPositionAsync(id, 1, Cancellation);

        Assert.Equal(HttpMethod.Put, handler.Request?.Method);
        Assert.Equal($"/games/{id}/position", handler.Request?.RequestUri?.AbsolutePath);
        Assert.Equal("""{"position":1}""", handler.Body);
        Assert.Equal(1, grid.Position);
        Assert.Equal(2, grid.Moves.Count);
    }

    [Theory]
    [InlineData("Won", "\"Move\"")]
    [InlineData("1", "\"Move\"")]
    [InlineData("Solved", "\"Guess\"")]
    [InlineData("Solved", "0")]
    public async Task A_state_or_source_the_web_does_not_know_fails_loudly(string state, string source)
    {
        var api = ApiReturning(GridJson(state, source));

        await Assert.ThrowsAsync<JsonException>(() => api.NewGameAsync(Cancellation));
    }

    /// <summary>
    /// A grid of two cells: one filled from <paramref name="source"/> (raw JSON), one empty. It is at position 1 of
    /// two moves, so the second move is kept but not applied.
    /// </summary>
    private static string GridJson(string state, string source) =>
        $$"""
        {
          "id": "{{Guid.NewGuid()}}",
          "state": "{{state}}",
          "cells": [
            { "row": 1, "column": 1, "digit": 5, "source": {{source}}, "candidates": [5] },
            { "row": 1, "column": 2, "digit": null, "source": null, "candidates": [1, 2] }
          ],
          "position": 1,
          "moves": [
            { "row": 1, "column": 1, "digit": 5, "deductions": 0 },
            { "row": 4, "column": 7, "digit": 2, "deductions": 3 }
          ]
        }
        """;

    private static SudokuApi ApiReturning(string json) =>
        new(new HttpClient(new StubHandler(json)) { BaseAddress = new Uri("http://api") });

    /// <summary>Answers every request with <paramref name="json"/>, and remembers the last request and its body.</summary>
    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }
}
