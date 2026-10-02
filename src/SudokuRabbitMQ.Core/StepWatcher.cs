using System.Collections.Concurrent;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace SudokuRabbitMQ.Core;

/// <summary>
/// One reader of a game's steps stream, from when it started watching. A move completes only once every watcher has
/// read every step of its cascade, so after awaiting a move, <see cref="TryRead"/> returns everything it did.
/// </summary>
public sealed class StepWatcher : IAsyncEnumerable<Step>, IAsyncDisposable
{
    private readonly ConcurrentQueue<Step> _steps = new();
    private readonly SemaphoreSlim _arrived = new(0); // released once per step and once on completion; may run ahead
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Func<StepWatcher, ValueTask> _stop;
    private IChannel? _channel;

    private StepWatcher(Func<StepWatcher, ValueTask> stop)
    {
        _stop = stop;
    }

    /// <summary>Completes when the watcher stops: when it is disposed, or its game is. Steps read until then remain.</summary>
    public Task Completion => _completion.Task;

    /// <summary>Starts reading the steps stream at its next step, calling <paramref name="read"/> after each.</summary>
    internal static async Task<StepWatcher> StartAsync(
        IConnection connection, string stream, Action read, Func<StepWatcher, ValueTask> stop)
    {
        var watcher = new StepWatcher(stop);
        var channel = await connection.CreateChannelAsync();
        watcher._channel = channel;
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: Topology.Prefetch, global: false);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            watcher._steps.Enqueue((Step)Envelope.Open(delivery.BasicProperties, delivery.Body));
            watcher._arrived.Release();
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false);
            read();
        };
        await channel.BasicConsumeAsync(stream, autoAck: false, consumerTag: "", noLocal: false, exclusive: false,
            arguments: Topology.StartAt("next"), consumer);
        return watcher;
    }

    /// <summary>A watcher of a game that has already been disposed: it has stopped before it started.</summary>
    internal static StepWatcher Stopped()
    {
        var watcher = new StepWatcher(_ => ValueTask.CompletedTask);
        watcher._completion.SetResult();
        return watcher;
    }

    /// <summary>Takes the next step read and not yet taken, if there is one.</summary>
    public bool TryRead(out Step step) => _steps.TryDequeue(out step!);

    public async IAsyncEnumerator<Step> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            if (_steps.TryDequeue(out var step))
            {
                yield return step;
            }
            else if (_completion.Task.IsCompleted)
            {
                yield break;
            }
            else
            {
                await _arrived.WaitAsync(cancellationToken);
            }
        }
    }

    /// <summary>Stops watching. The game stops counting on this watcher before its next move.</summary>
    public ValueTask DisposeAsync() => _stop(this);

    /// <summary>Closes the watcher's channel and ends its enumeration, once the game no longer counts on it.</summary>
    internal async ValueTask StopAsync()
    {
        if (_channel is { } channel)
        {
            _channel = null;
            await channel.CloseAsync();
            channel.Dispose();
        }

        _completion.TrySetResult();
        _arrived.Release();
    }
}
