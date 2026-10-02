namespace SudokuRabbitMQ.Core;

/// <summary>A snapshot of one cell of the grid. Rows and columns are 1–9.</summary>
public sealed record Cell(int Row, int Column, int? Digit, PlacementSource? Source, IReadOnlyList<int> Candidates);
