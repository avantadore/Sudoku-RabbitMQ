using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace SudokuRabbitMQ.Core;

/// <summary>
/// How a message looks on a stream (ADR 0004): its kind in the <c>type</c> property, the cell it is about in the
/// <c>row</c> and <c>column</c> headers, and the message itself as JSON.
/// </summary>
internal static class Envelope
{
    private static readonly Dictionary<string, Type> Kinds = new Type[]
    {
        typeof(Command.PlaceMove),
        typeof(Command.PlaceDeduction),
        typeof(Event.Filled),
        typeof(Event.CandidateLost),
        typeof(Step.Placement),
        typeof(Step.Elimination),
        typeof(Step.Contradiction.NoCandidateForCell),
        typeof(Step.Contradiction.NoCellForDigit),
        typeof(MoveOutcome.Accepted),
        typeof(MoveOutcome.Unchanged),
        typeof(MoveOutcome.Rejected),
    }.ToDictionary(kind => kind.Name);

    public static (BasicProperties Properties, byte[] Body) Seal(object message)
    {
        var properties = new BasicProperties { Type = message.GetType().Name };
        if (message is IAboutCell cell)
        {
            properties.Headers = new Dictionary<string, object?> { ["row"] = cell.Row, ["column"] = cell.Column };
        }

        if (message is Command.PlaceMove { ReplyTo: { } reply })
        {
            properties.ReplyTo = reply.ReplyTo;
            properties.CorrelationId = reply.CorrelationId;
        }

        return (properties, JsonSerializer.SerializeToUtf8Bytes(message, message.GetType()));
    }

    public static object Open(IReadOnlyBasicProperties properties, ReadOnlyMemory<byte> body)
    {
        if (properties.Type is not { } kind || !Kinds.TryGetValue(kind, out var type))
        {
            throw new InvalidOperationException($"A message of unknown kind '{properties.Type}': {Encoding.UTF8.GetString(body.Span)}");
        }

        var message = JsonSerializer.Deserialize(body.Span, type)!;
        return message is Command.PlaceMove move && properties is { ReplyTo: { } replyTo, CorrelationId: { } correlationId }
            ? move with { ReplyTo = new ReplyAddress(replyTo, correlationId) }
            : message;
    }
}
