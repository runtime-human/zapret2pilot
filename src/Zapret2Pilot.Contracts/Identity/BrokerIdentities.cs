using System.Security.Cryptography;

namespace Zapret2Pilot.Contracts.Identity;

public readonly record struct AppSessionId(Guid Value)
{
    public static AppSessionId New() => new(Guid.NewGuid());
}

public readonly record struct BrokerSessionId(Guid Value)
{
    public static BrokerSessionId New() => new(Guid.NewGuid());
}

public readonly record struct BrokerOperationId(Guid Value)
{
    public static BrokerOperationId New() => new(Guid.NewGuid());
}

public readonly record struct RequestSequence(long Value);

public readonly record struct RuntimeGeneration(long Value);

public readonly record struct PreparedBundleId(Guid Value)
{
    public static PreparedBundleId New() => new(Guid.NewGuid());
}

public readonly record struct PreparedPlanId(Guid Value)
{
    public static PreparedPlanId New() => new(Guid.NewGuid());
}

public readonly record struct Sha256Digest(string Value)
{
    public const string Prefix = "sha256:";

    public static Sha256Digest Compute(ReadOnlySpan<byte> content)
    {
        byte[] hash = SHA256.HashData(content);
        return new Sha256Digest(Prefix + Convert.ToHexString(hash).ToLowerInvariant());
    }

    public bool IsCanonical
        => Value is not null
            && Value.StartsWith(Prefix, StringComparison.Ordinal)
            && IsLowerHex(Value.AsSpan(Prefix.Length));

    private static bool IsLowerHex(ReadOnlySpan<char> value)
    {
        if (value.Length != 64)
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f')))
            {
                return false;
            }
        }

        return true;
    }
}

public readonly record struct RuntimeBundleId(Sha256Digest Digest);

public readonly record struct LogonSessionId(uint LowPart, int HighPart);
