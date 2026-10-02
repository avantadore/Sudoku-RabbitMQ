using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace SudokuRabbitMQ.Web;

/// <summary>Talks to the Sudoku API, resolved through Aspire service discovery.</summary>
public sealed class SudokuApi(HttpClient http)
{
    // Enums travel as their names. Anything else, such as a renamed member, fails to deserialise instead of passing silently.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    public async Task<Grid> NewGameAsync(CancellationToken cancellationToken = default)
    {
        var response = await http.PostAsync("/games", content: null, cancellationToken);
        return await ReadGridAsync(response, cancellationToken);
    }

    /// <summary>Places a digit. A rejected move returns the API's reason instead of a grid.</summary>
    public async Task<MoveResult> MoveAsync(Guid id, int row, int column, int digit, CancellationToken cancellationToken = default)
    {
        var response = await http.PutAsJsonAsync($"/games/{id}/cells/{row}/{column}", new { digit }, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken);
            return new MoveResult.Rejected(problem?.Detail ?? "The move was rejected.");
        }

        return new MoveResult.Accepted(await ReadGridAsync(response, cancellationToken));
    }

    /// <summary>Replays the game to <paramref name="position"/>, from 0 (the empty grid) to the number of moves.</summary>
    public async Task<Grid> SetPositionAsync(Guid id, int position, CancellationToken cancellationToken = default)
    {
        var response = await http.PutAsJsonAsync($"/games/{id}/position", new { position }, cancellationToken);
        return await ReadGridAsync(response, cancellationToken);
    }

    private static async Task<Grid> ReadGridAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Grid>(Json, cancellationToken)
            ?? throw new HttpRequestException("The API returned no grid.");
    }
}

/// <summary>A game's grid, with its position and its whole move history, including any moves after the position.</summary>
public sealed record Grid(Guid Id, GameState State, IReadOnlyList<GridCell> Cells, int Position, IReadOnlyList<GridMove> Moves);

public sealed record GridCell(int Row, int Column, int? Digit, PlacementSource? Source, IReadOnlyList<int> Candidates);

/// <summary>A move in the game's move history, with how many deductions its cascade made.</summary>
public sealed record GridMove(int Row, int Column, int Digit, int Deductions);

/// <summary>Mirrors the Core enum of the same name, which the Web project cannot reference.</summary>
public enum GameState
{
    InProgress,
    Solved,
    Contradicted,
}

/// <summary>Mirrors the Core enum of the same name, which the Web project cannot reference.</summary>
public enum PlacementSource
{
    Move,
    Deduction,
}

public abstract record MoveResult
{
    private MoveResult()
    {
    }

    public sealed record Accepted(Grid Grid) : MoveResult;

    public sealed record Rejected(string Reason) : MoveResult;
}
