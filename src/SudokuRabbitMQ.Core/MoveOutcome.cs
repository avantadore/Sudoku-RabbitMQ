namespace SudokuRabbitMQ.Core;

/// <summary>What became of a move: accepted, unchanged (the same digit again), or rejected with a reason.</summary>
public abstract record MoveOutcome
{
    private MoveOutcome()
    {
    }

    public sealed record Accepted : MoveOutcome;

    public sealed record Unchanged : MoveOutcome;

    public sealed record Rejected(string Reason) : MoveOutcome;
}
