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
    private double availableTokens;
    private DateTimeOffset lastRefillUtc;
    private bool initialized;

    public BrokerRequestRateGate(int requestsPerSecond, int burstCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestsPerSecond);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(burstCapacity);
        ArgumentOutOfRangeException.ThrowIfLessThan(burstCapacity, requestsPerSecond);

        this.requestsPerSecond = requestsPerSecond;
        this.burstCapacity = burstCapacity;
        availableTokens = burstCapacity;
    }

    public bool TryAcquire(DateTimeOffset nowUtc)
    {
        lock (sync)
        {
            if (!initialized)
            {
                initialized = true;
                lastRefillUtc = nowUtc;
            }
            else
            {
                if (nowUtc < lastRefillUtc)
                {
                    return false;
                }

                double elapsedSeconds = (nowUtc - lastRefillUtc).TotalSeconds;
                if (elapsedSeconds > 0)
                {
                    availableTokens = Math.Min(
                        burstCapacity,
                        availableTokens + (elapsedSeconds * requestsPerSecond));
                    lastRefillUtc = nowUtc;
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
