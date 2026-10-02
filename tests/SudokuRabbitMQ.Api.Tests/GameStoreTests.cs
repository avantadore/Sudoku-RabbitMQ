using SudokuRabbitMQ.Core;
using SudokuRabbitMQ.Core.Tests;

namespace SudokuRabbitMQ.Api.Tests;

public class GameStoreTests : IAsyncLifetime
{
    private readonly ManualTime _time = new();
    private GameStore _store = null!;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() =>
        _store = new GameStore(await Broker.ConnectionsAsync(), _time, TimeSpan.FromMinutes(5));

    public ValueTask DisposeAsync() => _store.DisposeAsync();

    [Fact]
    public async Task Using_an_unknown_game_reports_it_as_not_found()
    {
        var (found, _) = await _store.TryUseAsync(Guid.NewGuid(), game => Task.FromResult(game.State));

        Assert.False(found);
    }

    [Fact]
    public async Task A_new_game_can_be_used_by_its_id_and_keeps_its_moves_between_uses()
    {
        var id = await _store.NewAsync((id, _) => id);
        await _store.TryUseAsync(id, game => game.MoveAsync(4, 7, 3));

        var (found, digit) = await _store.TryUseAsync(id, game => Task.FromResult(game.Cell(4, 7).Digit));

        Assert.True(found);
        Assert.Equal(3, digit);
    }

    [Fact]
    public async Task A_second_use_of_a_game_waits_until_the_first_has_finished()
    {
        var id = await _store.NewAsync((id, _) => id);
        var firstStarted = new TaskCompletionSource();
        var finishFirst = new TaskCompletionSource();

        var first = _store.TryUseAsync(id, async _ =>
        {
            firstStarted.SetResult();
            await finishFirst.Task;
            return true;
        });
        await firstStarted.Task.WaitAsync(Cancellation);
        var second = Task.Run(() => _store.TryUseAsync(id, _ => Task.FromResult(true)), Cancellation);

        await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(200), Cancellation));
        var secondFinishedEarly = second.IsCompleted;
        finishFirst.SetResult();
        await Task.WhenAll(first, second);

        Assert.False(secondFinishedEarly);
    }

    [Fact]
    public async Task A_game_used_within_the_idle_time_keeps_its_grid()
    {
        var id = await _store.NewAsync((id, _) => id);
        _time.Advance(TimeSpan.FromMinutes(4));

        await _store.SuspendIdleAsync();

        Assert.False((await _store.TryUseAsync(id, game => Task.FromResult(game.IsSuspended))).Result);
    }

    [Fact]
    public async Task An_idle_game_is_suspended_and_its_next_move_rebuilds_the_grid_where_it_left_off()
    {
        var id = await _store.NewAsync((id, _) => id);
        await _store.TryUseAsync(id, game => game.MoveAsync(1, 1, 5));
        _time.Advance(TimeSpan.FromMinutes(5));

        await _store.SuspendIdleAsync();

        var (_, suspended) = await _store.TryUseAsync(id, game => Task.FromResult((game.IsSuspended, game.Cell(1, 1).Digit)));
        Assert.Equal((true, 5), suspended);

        var (_, moved) = await _store.TryUseAsync(id, async game =>
            (await game.MoveAsync(1, 2, 5), game.IsSuspended, game.Moves.Count));
        Assert.IsType<MoveOutcome.Rejected>(moved.Item1); // 5 is no longer a candidate, so the replay rebuilt (1,1)
        Assert.False(moved.IsSuspended);
        Assert.Equal(1, moved.Count);
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
