using SudokuRabbitMQ.Core;

namespace SudokuRabbitMQ.Api;

/// <summary>A game's grid, with its position and its whole move history, including any moves after the position.</summary>
public sealed record GridResponse(
    Guid Id, GameState State, IReadOnlyList<CellResponse> Cells, int Position, IReadOnlyList<MoveResponse> Moves)
{
    public static GridResponse From(Guid id, Game game) =>
        new(id, game.State, [.. game.Cells.Select(CellResponse.From)], game.Position, [.. game.Moves.Select(MoveResponse.From)]);
}

public sealed record CellResponse(int Row, int Column, int? Digit, PlacementSource? Source, IReadOnlyList<int> Candidates)
{
    public static CellResponse From(Cell cell) =>
        new(cell.Row, cell.Column, cell.Digit, cell.Source, cell.Candidates);
}

public sealed record MoveResponse(int Row, int Column, int Digit, int Deductions)
{
    public static MoveResponse From(RecordedMove move) =>
        new(move.Row, move.Column, move.Digit, move.Deductions);
}
