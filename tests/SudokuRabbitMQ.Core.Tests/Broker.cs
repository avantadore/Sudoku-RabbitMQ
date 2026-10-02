using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace SudokuRabbitMQ.Core.Tests;

/// <summary>
/// One RabbitMQ container for the whole test assembly, started on first use and removed by Testcontainers when the
/// run ends, unless <c>SUDOKU_TEST_RABBITMQ</c> names a broker to use instead. Tests run in parallel on it safely,
/// because every game and grid has streams of its own.
/// </summary>
internal static class Broker
{
    private static readonly Lazy<Task<string>> ConnectionString = new(async () =>
    {
        if (Environment.GetEnvironmentVariable("SUDOKU_TEST_RABBITMQ") is { Length: > 0 } given)
        {
            return given;
        }

        var container = new RabbitMqBuilder("rabbitmq:4.1").Build();
        await container.StartAsync();
        return container.GetConnectionString();
    });

    public static async Task<IConnectionFactory> ConnectionsAsync() =>
        new ConnectionFactory { Uri = new Uri(await ConnectionStringAsync()) };

    public static Task<string> ConnectionStringAsync() => ConnectionString.Value;

    public static async Task<Game> NewGameAsync() => await Game.NewAsync(await ConnectionsAsync());
}
