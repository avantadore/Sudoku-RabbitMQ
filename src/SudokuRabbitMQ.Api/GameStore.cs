using System.Collections.Concurrent;
using RabbitMQ.Client;
using SudokuRabbitMQ.Core;

namespace SudokuRabbitMQ.Api;

/// <summary>
/// Holds games in memory for the lifetime of the process. A game makes one move at a time by itself, but a request
/// also reads the grid it leaves behind, so the store hands a game out only while holding that game's semaphore:
/// one request at a time, held across its awaits. A grid costs RabbitMQ streams and channels, so a game left idle
/// has its grid suspended, to be rebuilt by replay on its next move (ADR 0004).
/// </summary>
public sealed class GameStore(IConnectionFactory connections, TimeProvider time, TimeSpan idleAfter) : IAsyncDisposable
{
    /// <summary>How long a game may go unused before its grid is suspended, unless told otherwise.</summary>
    public static readonly TimeSpan DefaultIdleAfter = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<Guid, Entry> _games = new();

    /// <summary>Starts a new game, running <paramref name="use"/> on it before any other request can reach it.</summary>
    public async Task<T> NewAsync<T>(Func<Guid, Game, T> use)
    {
        var game = await Game.NewAsync(connections);
        var result = use(game.Id, game);
        _games[game.Id] = new Entry(game, time.GetUtcNow());
        return result;
    }

    /// <summary>
    /// Runs <paramref name="use"/> on the game while holding its semaphore. Found is false, and Result the default,
    /// if there is no such game.
    /// </summary>
    public async Task<(bool Found, T Result)> TryUseAsync<T>(Guid id, Func<Game, Task<T>> use)
    {
        if (_games.GetValueOrDefault(id) is not { } entry)
        {
            return (false, default!);
        }

        await entry.Turn.WaitAsync();
        try
        {
            return (true, await use(entry.Game));
        }
        finally
        {
            entry.LastUsed = time.GetUtcNow();
            entry.Turn.Release();
        }
    }

    /// <summary>
    /// Suspends the grid of every game not used for <c>idleAfter</c>, skipping any game a request is using right now.
    /// </summary>
    public async Task SuspendIdleAsync()
    {
        var now = time.GetUtcNow();
        foreach (var entry in _games.Values)
        {
            if (now - entry.LastUsed < idleAfter || entry.Game.IsSuspended || !await entry.Turn.WaitAsync(TimeSpan.Zero))
            {
                continue;
            }

            try
            {
                await entry.Game.SuspendAsync();
            }
            finally
            {
                entry.Turn.Release();
            }
        }
    }

    /// <summary>Disposes every game, which deletes their streams.</summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var entry in _games.Values)
        {
            await entry.Game.DisposeAsync();
        }

        _games.Clear();
    }

    private sealed record Entry(Game Game, DateTimeOffset Created)
    {
        public SemaphoreSlim Turn { get; } = new(1, 1);

        public DateTimeOffset LastUsed { get; set; } = Created;
    }
}

/// <summary>Suspends idle games' grids every minute.</summary>
public sealed class IdleGameSuspender(GameStore store, TimeProvider time) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await store.SuspendIdleAsync();
        }
    }
}
