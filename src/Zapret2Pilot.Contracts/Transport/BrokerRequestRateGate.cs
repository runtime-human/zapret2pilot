namespace Zapret2Pilot.Contracts.Transport;

/// <summary>
/// Dependency-light token-bucket admission primitive for the broker ingress pump.
/// It bounds request bursts without owning transport or runtime lifecycle state.
/// </summary>
public sealed class BrokerRequestRateGate
{
    private readonly object sync = new();
    private readonly int requestsPerSecond;
    private readonly int burstCapacity;
    private readonly TimeProvider timeProvider;
    private double availableTokens;
    private long lastRefillTimestamp;
    private bool initialized;

    public BrokerRequestRateGate(int requestsPerSecond, int burstCapacity)
        : this(requestsPerSecond, burstCapacity, TimeProvider.System)
    {
    }

    public BrokerRequestRateGate(
        int requestsPerSecond,
        int burstCapacity,
        TimeProvider timeProvider)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestsPerSecond);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(burstCapacity);
        ArgumentOutOfRangeException.ThrowIfLessThan(burstCapacity, requestsPerSecond);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.requestsPerSecond = requestsPerSecond;
        this.burstCapacity = burstCapacity;
        this.timeProvider = timeProvider;
        availableTokens = burstCapacity;
    }

    public bool TryAcquire()
    {
        lock (sync)
        {
            long now = timeProvider.GetTimestamp();
            if (!initialized)
            {
                initialized = true;
                lastRefillTimestamp = now;
            }
            else
            {
                TimeSpan elapsed = timeProvider.GetElapsedTime(lastRefillTimestamp, now);
                if (elapsed > TimeSpan.Zero)
                {
                    availableTokens = Math.Min(
                        burstCapacity,
                        availableTokens + (elapsed.TotalSeconds * requestsPerSecond));
                    lastRefillTimestamp = now;
                }
            }

            if (availableTokens < 1)
            {
                return false;
            }

            availableTokens -= 1;
            return true;
        }
    }
}
