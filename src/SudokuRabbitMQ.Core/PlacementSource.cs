namespace SudokuRabbitMQ.Core;

/// <summary>Who made a placement: the player (a move) or the game itself (a deduction).</summary>
public enum PlacementSource
{
    Move,
    Deduction,
}
