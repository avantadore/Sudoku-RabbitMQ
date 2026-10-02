using RabbitMQ.Client;

namespace SudokuRabbitMQ.Core;

/// <summary>
/// The streams a game talks on (ADR 0004). Each grid has 27 unit streams, read by the unit's nine cells and its
/// watcher; each game has one steps stream, read by whoever watches the game. A stream is read in full by every
/// consumer, so a unit's stream is its topic.
/// </summary>
internal static class Topology
{
    /// <summary>Every message on a unit stream is read by the unit's nine cells and its watcher.</summary>
    public const int ReadersPerUnitStream = 10;

    /// <summary>How many deliveries a consumer may have unacknowledged. Streams require a limit.</summary>
    public const ushort Prefetch = 100;

    /// <summary>Where a move's reply goes: RabbitMQ's direct reply-to, which needs no queue of its own.</summary>
    public const string DirectReplyTo = "amq.rabbitmq.reply-to";

    /// <summary><c>sudoku.grid.&lt;grid&gt;.row.3</c>, <c>….column.5</c> or <c>….box.2</c>.</summary>
    public static string UnitStream(Guid grid, UnitKind kind, int number) =>
        $"sudoku.grid.{grid}.{kind.ToString().ToLowerInvariant()}.{number}";

    /// <summary><c>sudoku.game.&lt;game&gt;.steps</c>. It belongs to the game, so watchers keep it through a replay.</summary>
    public static string StepsStream(Guid game) => $"sudoku.game.{game}.steps";

    /// <summary>The box a cell belongs to, 1–9 row by row from the top left.</summary>
    public static int BoxOf(int row, int column) => (row - 1) / 3 * 3 + (column - 1) / 3 + 1;

    /// <summary>Where a new consumer starts reading a stream: <c>first</c> or <c>next</c>.</summary>
    public static Dictionary<string, object?> StartAt(string offset) => new() { ["x-stream-offset"] = offset };

    public static Task DeclareStreamAsync(this IChannel channel, string name) =>
        channel.QueueDeclareAsync(name, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "stream" });

    public static Task DeleteStreamAsync(this IChannel channel, string name) => channel.QueueDeleteAsync(name);
}
