namespace SudokuRabbitMQ.Core;

/// <summary>
/// What the workers of one grid share. It counts the deliveries in flight: as many more as a stream has readers on
/// every publish, one fewer once a reader has handled or skipped one, so when the count reaches zero the move's
/// cascade is over. It tallies the cascade's deductions, holds the grid's contradiction flag, and knows where steps go.
/// </summary>
internal sealed class Switchboard
{
    private readonly Lock _contradiction = new(); // so no deduction is placed once a contradiction is decided
    private int _inFlight;
    private int _deductions;
    private bool _contradicted;
    private TaskCompletionSource _cascadeOver = NewCascadeOver();
    private volatile string? _stepsStream;
    private volatile int _stepWatchers;

    /// <summary>The game's steps stream. While it is null, as during a replay, no steps are published.</summary>
    public string? StepsStream
    {
        get => _stepsStream;
        set => _stepsStream = value;
    }

    /// <summary>How many watchers read the steps stream, so how many deliveries each step makes.</summary>
    public int StepWatchers
    {
        get => _stepWatchers;
        set => _stepWatchers = value;
    }

    public bool IsContradicted
    {
        get
        {
            lock (_contradiction)
            {
                return _contradicted;
            }
        }
    }

    /// <summary>
    /// Starts counting a new move's cascade. The grid must be quiet, which it is between moves, because a game makes
    /// one move at a time.
    /// </summary>
    public void StartCascade()
    {
        _deductions = 0;
        _cascadeOver = NewCascadeOver();
    }

    /// <summary>Completes once no delivery is left in flight, or fails if a worker failed.</summary>
    public Task CascadeOver => _cascadeOver.Task;

    public int Deductions => Volatile.Read(ref _deductions);

    public void MessageSent(int deliveries) => Interlocked.Add(ref _inFlight, deliveries);

    public void MessageHandled()
    {
        if (Interlocked.Decrement(ref _inFlight) == 0)
        {
            _cascadeOver.TrySetResult();
        }
    }

    /// <summary>A worker failed to handle a message: a bug, which the waiting move reports.</summary>
    public void Fail(Exception exception) => _cascadeOver.TrySetException(exception);

    /// <summary>
    /// Places a deduction by running <paramref name="fill"/>, unless the grid is in contradiction. Checked and placed
    /// as one, so no deduction slips in once a contradiction has been decided. True if it was placed.
    /// </summary>
    public bool Deduce(Action fill)
    {
        lock (_contradiction)
        {
            if (_contradicted)
            {
                return false;
            }

            fill();
            _deductions++;
            return true;
        }
    }

    /// <summary>
    /// The first contradiction to arrive puts the grid in contradiction, and true tells its finder to publish it as a
    /// step, once: never as a failure (ADR 0001). Any later one is the same dead end seen from elsewhere.
    /// </summary>
    public bool Contradict()
    {
        lock (_contradiction)
        {
            if (_contradicted)
            {
                return false;
            }

            _contradicted = true;
            return true;
        }
    }

    private static TaskCompletionSource NewCascadeOver() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
