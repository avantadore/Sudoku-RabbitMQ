using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace SudokuRabbitMQ.Core;

/// <summary>
/// A worker's own AMQP channel: the streams it reads, and what it publishes. RabbitMQ hands a channel's deliveries
/// to its consumers one at a time, so a worker handles one message at a time however many streams it reads, while
/// workers run in parallel with each other (ADR 0004). Every delivery on a unit stream counts as in flight until its
/// reader has handled it, or skipped it.
/// </summary>
internal sealed class Mailbox : IAsyncDisposable
{
    private readonly IChannel _channel;
    private readonly Switchboard _switchboard;

    private Mailbox(IChannel channel, Switchboard switchboard)
    {
        _channel = channel;
        _switchboard = switchboard;
    }

    public static async Task<Mailbox> OpenAsync(IConnection connection, Switchboard switchboard)
    {
        var channel = await connection.CreateChannelAsync();
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: Topology.Prefetch, global: false);
        return new Mailbox(channel, switchboard);
    }

    public IChannel Channel => _channel;

    /// <summary>Reads a unit stream from its start, handing each message to <paramref name="handle"/>.</summary>
    public async Task ReadAsync(string stream, Func<object, Task> handle)
    {
        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            try
            {
                await handle(Envelope.Open(delivery.BasicProperties, delivery.Body));
                await _channel.BasicAckAsync(delivery.DeliveryTag, multiple: false);
            }
            catch (Exception exception)
            {
                _switchboard.Fail(exception);
            }
            finally
            {
                _switchboard.MessageHandled();
            }
        };

        await _channel.BasicConsumeAsync(stream, autoAck: false, consumerTag: "", noLocal: false, exclusive: false,
            arguments: Topology.StartAt("first"), consumer);
    }

    /// <summary>Publishes a command or an event to a unit stream, where every one of its readers will read it.</summary>
    public Task SendAsync(string stream, object message)
    {
        _switchboard.MessageSent(Topology.ReadersPerUnitStream);
        return PublishAsync(stream, message);
    }

    /// <summary>
    /// Publishes a step to the game's steps stream, where each watcher will read it. A grid being rebuilt by a replay
    /// has no steps stream, and publishes nothing.
    /// </summary>
    public Task PublishStepAsync(Step step)
    {
        if (_switchboard.StepsStream is not { } stream)
        {
            return Task.CompletedTask;
        }

        _switchboard.MessageSent(_switchboard.StepWatchers);
        return PublishAsync(stream, step);
    }

    /// <summary>Answers a move. A reply is not counted as in flight: the move waits for it by itself.</summary>
    public Task ReplyAsync(ReplyAddress address, MoveOutcome outcome)
    {
        var (properties, body) = Envelope.Seal(outcome);
        properties.CorrelationId = address.CorrelationId;
        return _channel.BasicPublishAsync("", address.ReplyTo, mandatory: false, properties, body).AsTask();
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.CloseAsync();
        _channel.Dispose();
    }

    private Task PublishAsync(string stream, object message)
    {
        var (properties, body) = Envelope.Seal(message);
        return _channel.BasicPublishAsync("", stream, mandatory: false, properties, body).AsTask();
    }
}
