using System.Collections.Generic;
using Xunit;
using Zapret2Pilot.Core.Primitives;
using Zapret2Pilot.Core.Profiles;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Core.Runtime;
using Zapret2Pilot.Engine.Zapret2.Compiler;

namespace Zapret2Pilot.Engine.Zapret2.Tests.Compiler;

/// <summary>
/// Focused unit tests for <see cref="ZapretPlanCompiler"/>.
///
/// The compiler is a pure, deterministic component: it does not access
/// the filesystem and does not launch any process. The tests therefore
/// exercise the compiler against in-memory <see cref="CompilationInputs"/>
/// and assert on the structural shape of the produced
/// <see cref="CompiledZapretPlan"/>.
/// </summary>
public sealed class ZapretPlanCompilerTests
{
    [Fact]
    public static void ValidInputsProducePopulatedPlan()
    {
        ZapretPlanCompiler compiler = new();

        CompilationInputs inputs = BuildValidInputs();

        Result<CompiledZapretPlan> result = compiler.Compile(inputs);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : string.Empty);
        CompiledZapretPlan plan = result.Value;

        Assert.Equal(new ProfileId("test-profile"), plan.ProfileId);
        Assert.NotNull(plan.Id);
        Assert.NotNull(plan.CacheKey);
        Assert.NotEmpty(plan.CacheKey!.Value);
        Assert.Equal(new ProfileId("test-profile").Value, plan.ProfileId!.Value);
        Assert.Equal(ZapretPlanCompiler.CommandLinePlaceholder, plan.CommandLine);

        Assert.NotNull(plan.Arguments);
        Assert.NotEmpty(plan.Arguments!);
        Assert.Contains("--filter", plan.Arguments!);
        Assert.Contains("tcp", plan.Arguments!);
        Assert.Contains("--hostlists=hostlists/hostlists/default.txt", plan.Arguments!);

        Assert.NotNull(plan.ArgsContent);
        Assert.NotEmpty(plan.ArgsContent);
        Assert.Contains("--filter", plan.ArgsContent);
        Assert.Contains("tcp", plan.ArgsContent);
        Assert.Contains("--hostlists=hostlists/hostlists/default.txt", plan.ArgsContent);

        Assert.Single(plan.Hostlists);
        Assert.Equal("hostlists/default.txt", plan.Hostlists[0].RelativePath);
    }

    [Fact]
    public static void NullInputsAreRejected()
    {
        ZapretPlanCompiler compiler = new();

        Result<CompiledZapretPlan> result = compiler.Compile(null!);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Runtime, result.Error.Category);
        Assert.Equal(ErrorSeverity.Error, result.Error.Severity);
        Assert.NotNull(result.Error.Code);
    }

    [Fact]
    public static void PlanIdIsDerivedFromCacheKey()
    {
        ZapretPlanCompiler compiler = new();

        CompilationInputs inputs = BuildValidInputs();
        Result<CompiledZapretPlan> result = compiler.Compile(inputs);

        Assert.True(result.IsSuccess);
        Assert.Equal(result.Value.CacheKey!.Value, result.Value.Id!.Value);
    }

    [Fact]
    public static void TokensContainingShellMetacharactersAreQuoted()
    {
        ZapretPlanCompiler compiler = new();

        StrategyPackId packId = new("base-pack");
        HostlistId hostlistId = new("default-list");

        StrategyDefinition strategy = new(
            "strategy-with-space",
            new List<string> { "--filter", "value with space", "key=\"quoted\"" });

        StrategyAssignment assignment = new(packId, strategy);
        HostlistAssignment hostlistAssignment = new(hostlistId, "hostlists/default.txt");

        ProfileDefinition definition = new(
            id: new ProfileId("test-profile"),
            displayName: "Test Profile",
            description: null,
            strategies: new List<StrategyAssignment> { assignment },
            hostlists: new List<HostlistAssignment> { hostlistAssignment });

        CompilationInputs inputs = new(
            definition: definition,
            profileDocumentHash: "doc-hash-1",
            strategyPackHashes: new Dictionary<StrategyPackId, string> { [packId] = "pack-hash-1" },
            hostlistFingerprints: new Dictionary<HostlistId, string> { [hostlistId] = "hostlist-fp-1" },
            runtimeManifestHash: "manifest-hash-1",
            options: new ZapretCompilerOptions("0.0.15"));

        Result<CompiledZapretPlan> result = compiler.Compile(inputs);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : string.Empty);
        IReadOnlyList<string> arguments = result.Value.Arguments!;

        // "--filter" is safe and must NOT be quoted.
        Assert.Contains("--filter", arguments);
        // "value with space" contains a space and MUST be quoted.
        Assert.Contains("\"value with space\"", arguments);
        // "key=\"quoted\"" contains a double-quote and MUST be quoted with
        // the embedded quote escaped as \".
        Assert.Contains("\"key=\\\"quoted\\\"\"", arguments);
    }

    [Fact]
    public static void EmptyProfileProducesEmptyArguments()
    {
        ZapretPlanCompiler compiler = new();

        ProfileDefinition definition = new(
            id: new ProfileId("empty-profile"),
            displayName: "Empty Profile",
            description: null,
            strategies: new List<StrategyAssignment>(),
            hostlists: new List<HostlistAssignment>());

        CompilationInputs inputs = new(
            definition: definition,
            profileDocumentHash: "doc-hash-1",
            strategyPackHashes: new Dictionary<StrategyPackId, string>(),
            hostlistFingerprints: new Dictionary<HostlistId, string>(),
            runtimeManifestHash: "manifest-hash-1",
            options: new ZapretCompilerOptions("0.0.15"));

        Result<CompiledZapretPlan> result = compiler.Compile(inputs);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Arguments);
        Assert.Empty(result.Value.Arguments!);
        Assert.Equal(string.Empty, result.Value.ArgsContent);
        Assert.Empty(result.Value.Hostlists);
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
            description: "Default test profile.",
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
}
