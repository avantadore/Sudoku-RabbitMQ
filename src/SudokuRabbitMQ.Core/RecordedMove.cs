namespace SudokuRabbitMQ.Core;

/// <summary>A move in a game's move history, with how many deductions its cascade made.</summary>
public sealed record RecordedMove(int Row, int Column, int Digit, int Deductions);
