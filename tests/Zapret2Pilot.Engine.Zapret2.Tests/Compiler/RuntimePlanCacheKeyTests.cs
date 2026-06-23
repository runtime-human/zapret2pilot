using System.Collections.Generic;
using Xunit;
using Zapret2Pilot.Core.Primitives;
using Zapret2Pilot.Core.Profiles;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Core.Runtime;
using Zapret2Pilot.Engine.Zapret2.Compiler;

namespace Zapret2Pilot.Engine.Zapret2.Tests.Compiler;

/// <summary>
/// Focused unit tests for the content-addressed
/// <see cref="RuntimePlanCacheKey"/> and the cache-key sensitivity of
/// <see cref="ZapretPlanCompiler"/>.
///
/// Per <c>DEC-0011</c> the cache key must change whenever any
/// compilation input changes (profile document hash, strategy pack
/// hashes, hostlist fingerprints, runtime manifest hash, compiler
/// options, compiler version), and must remain stable when no input
/// changes. The tests below pin both guarantees.
/// </summary>
public sealed class RuntimePlanCacheKeyTests
{
    [Fact]
    public static void SameInputsProduceEqualKeys()
    {
        ZapretPlanCompiler compiler = new();
        CompilationInputs inputs = BuildValidInputs();

        Result<CompiledZapretPlan> first = compiler.Compile(inputs);
        Result<CompiledZapretPlan> second = compiler.Compile(inputs);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.CacheKey, second.Value.CacheKey);
    }

    [Fact]
    public static void ChangingProfileDocumentHashChangesTheKey()
    {
        ZapretPlanCompiler compiler = new();

        CompilationInputs baseline = BuildValidInputs();
        CompilationInputs mutated = WithProfileDocumentHash(baseline, "doc-hash-2");

        Assert.NotEqual(
            compiler.Compile(baseline).Value.CacheKey,
            compiler.Compile(mutated).Value.CacheKey);
    }

    [Fact]
    public static void ChangingStrategyPackHashChangesTheKey()
    {
        ZapretPlanCompiler compiler = new();

        CompilationInputs baseline = BuildValidInputs();
        CompilationInputs mutated = WithStrategyPackHash(baseline, "base-pack", "pack-hash-2");

        Assert.NotEqual(
            compiler.Compile(baseline).Value.CacheKey,
            compiler.Compile(mutated).Value.CacheKey);
    }

    [Fact]
    public static void ChangingHostlistFingerprintChangesTheKey()
    {
        ZapretPlanCompiler compiler = new();

        CompilationInputs baseline = BuildValidInputs();
        CompilationInputs mutated = WithHostlistFingerprint(baseline, "default-list", "hostlist-fp-2");

        Assert.NotEqual(
            compiler.Compile(baseline).Value.CacheKey,
            compiler.Compile(mutated).Value.CacheKey);
    }

    [Fact]
    public static void ChangingRuntimeManifestHashChangesTheKey()
    {
        ZapretPlanCompiler compiler = new();

        CompilationInputs baseline = BuildValidInputs();
        CompilationInputs mutated = WithRuntimeManifestHash(baseline, "manifest-hash-2");

        Assert.NotEqual(
            compiler.Compile(baseline).Value.CacheKey,
            compiler.Compile(mutated).Value.CacheKey);
    }

    [Fact]
    public static void ChangingCompilerOptionsChangesTheKey()
    {
        ZapretPlanCompiler compiler = new();

        CompilationInputs baseline = BuildValidInputs();
        CompilationInputs mutated = WithCompilerOptions(
            baseline,
            new ZapretCompilerOptions(
                "0.0.15",
                new Dictionary<string, string> { ["emit-quote-hostlists"] = "on" }));

        Assert.NotEqual(
            compiler.Compile(baseline).Value.CacheKey,
            compiler.Compile(mutated).Value.CacheKey);
    }

    [Fact]
    public static void ChangingCompilerVersionChangesTheKey()
    {
        ZapretPlanCompiler compiler = new();

        CompilationInputs baseline = BuildValidInputs();
        CompilationInputs mutated = WithCompilerOptions(
            baseline,
            new ZapretCompilerOptions("0.0.16"));

        Assert.NotEqual(
            compiler.Compile(baseline).Value.CacheKey,
            compiler.Compile(mutated).Value.CacheKey);
    }

    [Fact]
    public static void RecordEqualityIsStructuralAndHashCodeMatches()
    {
        RuntimePlanCacheKey left = new("aabbcc");
        RuntimePlanCacheKey right = new("aabbcc");
        RuntimePlanCacheKey different = new("ddeeff");

        Assert.Equal(left, right);
        Assert.True(left.Equals(right));
        Assert.True(left == right);
        Assert.False(left == different);
        Assert.False(left.Equals(different));
        Assert.False(left == different);

        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public static void ConstructorRejectsNullOrWhitespace()
    {
        Assert.Throws<System.ArgumentNullException>(() => new RuntimePlanCacheKey(null!));
        Assert.Throws<System.ArgumentException>(() => new RuntimePlanCacheKey(string.Empty));
        Assert.Throws<System.ArgumentException>(() => new RuntimePlanCacheKey("   "));
    }

    private static CompilationInputs BuildValidInputs()
    {
        StrategyPackId packId = new("base-pack");
        HostlistId hostlistId = new("default-list");

        StrategyDefinition strategy = new(
            "strategy-1",
            new List<string> { "--filter", "tcp" });

        StrategyAssignment assignment = new(packId, strategy);
        HostlistAssignment hostlistAssignment = new(hostlistId, "hostlists/default.txt");

        ProfileDefinition definition = new(
            id: new ProfileId("test-profile"),
            displayName: "Test Profile",
            description: null,
            strategies: new List<StrategyAssignment> { assignment },
            hostlists: new List<HostlistAssignment> { hostlistAssignment });

        return new CompilationInputs(
            definition: definition,
            profileDocumentHash: "doc-hash-1",
            strategyPackHashes: new Dictionary<StrategyPackId, string> { [packId] = "pack-hash-1" },
            hostlistFingerprints: new Dictionary<HostlistId, string> { [hostlistId] = "hostlist-fp-1" },
            runtimeManifestHash: "manifest-hash-1",
            options: new ZapretCompilerOptions("0.0.15"));
    }

    private static CompilationInputs WithProfileDocumentHash(CompilationInputs baseline, string hash)
    {
        StrategyPackId packId = new("base-pack");
        HostlistId hostlistId = new("default-list");

        return new CompilationInputs(
            definition: baseline.Definition,
            profileDocumentHash: hash,
            strategyPackHashes: new Dictionary<StrategyPackId, string>(baseline.StrategyPackHashes),
            hostlistFingerprints: new Dictionary<HostlistId, string>(baseline.HostlistFingerprints),
            runtimeManifestHash: baseline.RuntimeManifestHash,
            options: baseline.Options);
    }

    private static CompilationInputs WithStrategyPackHash(CompilationInputs baseline, string packIdValue, string hash)
    {
        StrategyPackId packId = new(packIdValue);
        HostlistId hostlistId = new("default-list");

        Dictionary<StrategyPackId, string> hashes = new(baseline.StrategyPackHashes);
        hashes[packId] = hash;

        return new CompilationInputs(
            definition: baseline.Definition,
            profileDocumentHash: baseline.ProfileDocumentHash,
            strategyPackHashes: hashes,
            hostlistFingerprints: new Dictionary<HostlistId, string>(baseline.HostlistFingerprints),
            runtimeManifestHash: baseline.RuntimeManifestHash,
            options: baseline.Options);
    }

    private static CompilationInputs WithHostlistFingerprint(CompilationInputs baseline, string hostlistIdValue, string fingerprint)
    {
        HostlistId hostlistId = new(hostlistIdValue);

        Dictionary<HostlistId, string> fingerprints = new(baseline.HostlistFingerprints);
        fingerprints[hostlistId] = fingerprint;

        return new CompilationInputs(
            definition: baseline.Definition,
            profileDocumentHash: baseline.ProfileDocumentHash,
            strategyPackHashes: new Dictionary<StrategyPackId, string>(baseline.StrategyPackHashes),
            hostlistFingerprints: fingerprints,
            runtimeManifestHash: baseline.RuntimeManifestHash,
            options: baseline.Options);
    }

    private static CompilationInputs WithRuntimeManifestHash(CompilationInputs baseline, string hash)
    {
        return new CompilationInputs(
            definition: baseline.Definition,
            profileDocumentHash: baseline.ProfileDocumentHash,
            strategyPackHashes: new Dictionary<StrategyPackId, string>(baseline.StrategyPackHashes),
            hostlistFingerprints: new Dictionary<HostlistId, string>(baseline.HostlistFingerprints),
            runtimeManifestHash: hash,
            options: baseline.Options);
    }

    private static CompilationInputs WithCompilerOptions(CompilationInputs baseline, ZapretCompilerOptions options)
    {
        return new CompilationInputs(
            definition: baseline.Definition,
            profileDocumentHash: baseline.ProfileDocumentHash,
            strategyPackHashes: new Dictionary<StrategyPackId, string>(baseline.StrategyPackHashes),
            hostlistFingerprints: new Dictionary<HostlistId, string>(baseline.HostlistFingerprints),
            runtimeManifestHash: baseline.RuntimeManifestHash,
            options: options);
    }
}
