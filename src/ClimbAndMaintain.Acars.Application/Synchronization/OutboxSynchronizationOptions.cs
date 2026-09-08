namespace ClimbAndMaintain.Acars.Application.Synchronization;

public sealed record OutboxSynchronizationOptions
{
    public int BatchSize { get; init; } = 25;

    public int MaximumItemsPerRun { get; init; } = 500;

    public TimeSpan InitialRetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan MaximumRetryDelay { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan GetRetryDelay(int completedAttemptCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(completedAttemptCount);
        if (completedAttemptCount == 0)
        {
            return TimeSpan.Zero;
        }

        int exponent = Math.Min(completedAttemptCount - 1, 30);
        double multiplier = Math.Pow(2, exponent);
        double milliseconds = Math.Min(
            InitialRetryDelay.TotalMilliseconds * multiplier,
            MaximumRetryDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(BatchSize);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumItemsPerRun, BatchSize);
        if (InitialRetryDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(InitialRetryDelay),
                InitialRetryDelay,
                "The initial retry delay must be positive.");
        }

        if (MaximumRetryDelay < InitialRetryDelay)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumRetryDelay),
                MaximumRetryDelay,
                "The maximum retry delay cannot be shorter than the initial retry delay.");
        }
    }
}
