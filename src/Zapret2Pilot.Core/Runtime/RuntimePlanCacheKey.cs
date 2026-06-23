using Zapret2Pilot.Core.Internal;

namespace Zapret2Pilot.Core.Runtime;

/// <summary>
/// Content-addressed identifier for a compiled Zapret runtime plan.
///
/// Per <c>DEC-0011</c>, the key is a deterministic hash over every
/// input that affects the compiled output (profile document hash,
/// strategy pack hashes, hostlist fingerprints, runtime manifest hash,
/// compiler options, compiler version). Equality on the value string
/// is therefore sufficient to consider two plans byte-equivalent in
/// their compiled form.
///
/// This type lives in <c>Core.Runtime</c> (not in
/// <c>Engine.Zapret2.Compiler</c>) so that <see cref="CompiledZapretPlan"/>
/// can reference it without introducing a Core &rarr; Engine.Zapret2
/// dependency.
/// </summary>
public sealed record RuntimePlanCacheKey
{
    public RuntimePlanCacheKey(string value)
    {
        Value = Guard.NotNullOrWhiteSpace(value, nameof(value));
    }

    /// <summary>
    /// Lowercase hex SHA-256 digest of the canonicalized compilation
    /// inputs. Never null, empty or whitespace.
    /// </summary>
    public string Value { get; }

    public override string ToString()
    {
        return Value;
    }
}
