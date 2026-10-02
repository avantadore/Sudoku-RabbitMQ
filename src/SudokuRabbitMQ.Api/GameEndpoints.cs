using Microsoft.AspNetCore.Http.HttpResults;
using SudokuRabbitMQ.Core;

namespace SudokuRabbitMQ.Api;

public static class GameEndpoints
{
    public static IEndpointRouteBuilder MapGameEndpoints(this IEndpointRouteBuilder app)
    {
        var games = app.MapGroup("/games").WithTags("Games");

        games.MapPost("/", CreateGame);
        games.MapGet("/{id:guid}", GetGame);
        games.MapPut("/{id:guid}/cells/{row:int}/{column:int}", MakeMove);
        games.MapPut("/{id:guid}/position", SetPosition);
        games.MapGet("/{id:guid}/cells/{row:int}/{column:int}/candidates", GetCandidates);

        return app;
    }

    private static async Task<Created<GridResponse>> CreateGame(GameStore store)
    {
        var grid = await store.NewAsync(GridResponse.From);
        return TypedResults.Created($"/games/{grid.Id}", grid);
    }

    private static async Task<Results<Ok<GridResponse>, NotFound>> GetGame(Guid id, GameStore store) =>
        await store.TryUseAsync(id, game => Task.FromResult(GridResponse.From(id, game))) is (true, var grid)
            ? TypedResults.Ok(grid)
            : TypedResults.NotFound();

    private static async Task<Results<Ok<GridResponse>, ProblemHttpResult, ValidationProblem, NotFound>> MakeMove(
        Guid id, int row, int column, MoveRequest move, GameStore store)
    {
        var errors = ValidateOneToNine(("Row", row), ("Column", column), ("Digit", move.Digit));
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var (found, played) = await store.TryUseAsync(id, async game =>
            (Outcome: await game.MoveAsync(row, column, move.Digit), Grid: GridResponse.From(id, game)));
        if (!found)
        {
            return TypedResults.NotFound();
        }

        return played.Outcome switch
        {
            MoveOutcome.Rejected rejected => TypedResults.Problem(
                title: "Move rejected", detail: rejected.Reason, statusCode: StatusCodes.Status409Conflict),
            _ => TypedResults.Ok(played.Grid),
        };
    }

    /// <summary>Replays the game to a position from 0 (the empty grid) to the number of moves, keeping every move.</summary>
    private static async Task<Results<Ok<GridResponse>, ValidationProblem, NotFound>> SetPosition(
        Guid id, PositionRequest request, GameStore store)
    {
        // Checked while holding the game, so the range is the one the replay sees.
        var (found, grid) = await store.TryUseAsync(id, async game =>
        {
            if (request.Position < 0 || request.Position > game.Moves.Count)
            {
                return (GridResponse?)null;
            }

            await game.ReplayToAsync(request.Position);
            return GridResponse.From(id, game);
        });

        if (!found)
        {
            return TypedResults.NotFound();
        }

        return grid is not null
            ? TypedResults.Ok(grid)
            : TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Position"] = ["Position must be between 0 and the number of moves."],
            });
    }

    private static async Task<Results<Ok<IReadOnlyList<int>>, ValidationProblem, NotFound>> GetCandidates(
        Guid id, int row, int column, GameStore store)
    {
        var errors = ValidateOneToNine(("Row", row), ("Column", column));
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return await store.TryUseAsync(id, game => Task.FromResult(game.Cell(row, column).Candidates)) is (true, var candidates)
            ? TypedResults.Ok(candidates)
            : TypedResults.NotFound();
    }

    /// <summary>Rows, columns and digits are all 1–9.</summary>
    private static Dictionary<string, string[]> ValidateOneToNine(params (string Name, int Value)[] values) =>
        values
            .Where(value => value.Value is < 1 or > 9)
            .ToDictionary(value => value.Name, value => new[] { $"{value.Name} must be between 1 and 9." });
}

public sealed record MoveRequest(int Digit);

public sealed record PositionRequest(int Position);
