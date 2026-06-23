using System.Collections.Generic;
using Xunit;
using Zapret2Pilot.Core.Primitives;
using Zapret2Pilot.Core.Profiles;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Engine.Zapret2.Profiles;

namespace Zapret2Pilot.Engine.Zapret2.Tests.Profiles;

/// <summary>
/// Focused unit tests for <see cref="ProfileDocumentMapper"/>.
/// The mapper is a pure resolver: it consumes a validated
/// <see cref="ProfileDocument"/> and a set of supplied
/// <see cref="StrategyPackDocument"/>s and returns a
/// <see cref="ProfileDefinition"/>. It does not access the filesystem,
/// does not perform path-traversal checks (those are the validator's
/// job) and does not produce a runtime plan.
/// </summary>
public sealed class ProfileDocumentMapperTests
{
    private static readonly string[] SampleStrategyParameters = { "--filter", "tcp" };

    [Fact]
    public static void ValidProfileMapsToDefinition()
    {
        ProfileDocumentMapper mapper = new();

        StrategyPackId packId = new("base-pack");
        HostlistId hostlistId = new("default-list");

        StrategyPackDocument pack = new(
            Id: packId,
            Strategies: new List<StrategyDocument>
            {
                new("strategy-1", new List<string> { "--filter", "tcp" }),
            });

        ProfileDocument document = new(
            Id: new ProfileId("test-profile"),
            DisplayName: "Test Profile",
            Description: "Default test profile.",
            StrategyReferences: new List<StrategyReference>
            {
                new(packId, "strategy-1"),
            },
            HostlistReferences: new List<HostlistReference>
            {
                new(hostlistId, "hostlists/default.txt"),
            });

        Result<ProfileDefinition> result = mapper.Map(document, new List<StrategyPackDocument> { pack });

        Assert.True(result.IsSuccess);
        ProfileDefinition definition = result.Value;
        Assert.Equal(new ProfileId("test-profile"), definition.Id);
        Assert.Equal("Test Profile", definition.DisplayName);
        Assert.Equal("Default test profile.", definition.Description);

        Assert.Single(definition.Strategies);
        StrategyAssignment strategyAssignment = definition.Strategies[0];
        Assert.Equal(packId, strategyAssignment.PackId);
        Assert.Equal("strategy-1", strategyAssignment.Strategy.Name);
        Assert.Equal(SampleStrategyParameters, strategyAssignment.Strategy.Parameters);

        Assert.Single(definition.Hostlists);
        HostlistAssignment hostlistAssignment = definition.Hostlists[0];
        Assert.Equal(hostlistId, hostlistAssignment.HostlistId);
        Assert.Equal("hostlists/default.txt", hostlistAssignment.RelativePath);
    }

    [Fact]
    public static void MissingStrategyPackReturnsStrategyPackMissing()
    {
        ProfileDocumentMapper mapper = new();

        StrategyPackId requestedPackId = new("requested-pack");
        StrategyPackId suppliedPackId = new("other-pack");

        StrategyPackDocument suppliedPack = new(
            Id: suppliedPackId,
            Strategies: new List<StrategyDocument>
            {
                new("strategy-1", new List<string>()),
            });

        ProfileDocument document = new(
            Id: new ProfileId("test-profile"),
            DisplayName: "Test Profile",
            Description: null,
            StrategyReferences: new List<StrategyReference>
            {
                new(requestedPackId, "strategy-1"),
            },
            HostlistReferences: new List<HostlistReference>());

        Result<ProfileDefinition> result = mapper.Map(document, new List<StrategyPackDocument> { suppliedPack });

        Assert.True(result.IsFailure);
        ErrorInfo? error = result.IsFailure ? result.Error : null;
        Assert.NotNull(error);
        Assert.Equal("StrategyPackMissing", error!.Code);
        Assert.Equal(ErrorCategory.StrategyPack, error.Category);
        Assert.Equal(ErrorSeverity.Error, error.Severity);
    }

    [Fact]
    public static void MissingStrategyInExistingPackReturnsStrategyMissing()
    {
        ProfileDocumentMapper mapper = new();

        StrategyPackId packId = new("base-pack");

        StrategyPackDocument pack = new(
            Id: packId,
            Strategies: new List<StrategyDocument>
            {
                new("strategy-1", new List<string>()),
            });

        ProfileDocument document = new(
            Id: new ProfileId("test-profile"),
            DisplayName: "Test Profile",
            Description: null,
            StrategyReferences: new List<StrategyReference>
            {
                new(packId, "missing-strategy"),
            },
            HostlistReferences: new List<HostlistReference>());

        Result<ProfileDefinition> result = mapper.Map(document, new List<StrategyPackDocument> { pack });

        Assert.True(result.IsFailure);
        ErrorInfo? error = result.IsFailure ? result.Error : null;
        Assert.NotNull(error);
        Assert.Equal("StrategyMissing", error!.Code);
        Assert.Equal(ErrorCategory.StrategyPack, error.Category);
        Assert.Equal(ErrorSeverity.Error, error.Severity);
    }

    [Fact]
    public static void NullHostlistRelativePathReturnsHostlistReferenceInvalid()
    {
        ProfileDocumentMapper mapper = new();

        HostlistId hostlistId = new("default-list");

        HostlistReference nullPathReference = new(hostlistId, null!);

        ProfileDocument document = new(
            Id: new ProfileId("test-profile"),
            DisplayName: "Test Profile",
            Description: null,
            StrategyReferences: new List<StrategyReference>(),
            HostlistReferences: new List<HostlistReference>
            {
                nullPathReference,
            });

        Result<ProfileDefinition> result = mapper.Map(document, new List<StrategyPackDocument>());

        Assert.True(result.IsFailure);
        ErrorInfo? error = result.IsFailure ? result.Error : null;
        Assert.NotNull(error);
        Assert.Equal("HostlistReferenceInvalid", error!.Code);
        Assert.Equal(ErrorCategory.Hostlist, error.Category);
        Assert.Equal(ErrorSeverity.Error, error.Severity);
    }

    [Fact]
    public static void BlankHostlistRelativePathReturnsHostlistReferenceInvalid()
    {
        ProfileDocumentMapper mapper = new();

        HostlistId hostlistId = new("default-list");

        HostlistReference blankPathReference = new(hostlistId, "   ");

        ProfileDocument document = new(
            Id: new ProfileId("test-profile"),
            DisplayName: "Test Profile",
            Description: null,
            StrategyReferences: new List<StrategyReference>(),
            HostlistReferences: new List<HostlistReference>
            {
                blankPathReference,
            });

        Result<ProfileDefinition> result = mapper.Map(document, new List<StrategyPackDocument>());

        Assert.True(result.IsFailure);
        ErrorInfo? error = result.IsFailure ? result.Error : null;
        Assert.NotNull(error);
        Assert.Equal("HostlistReferenceInvalid", error!.Code);
        Assert.Equal(ErrorCategory.Hostlist, error.Category);
    }

    [Fact]
    public static void NullHostlistIdReturnsHostlistReferenceInvalid()
    {
        ProfileDocumentMapper mapper = new();

        HostlistReference nullIdReference = new(null!, "hostlists/default.txt");

        ProfileDocument document = new(
            Id: new ProfileId("test-profile"),
            DisplayName: "Test Profile",
            Description: null,
            StrategyReferences: new List<StrategyReference>(),
            HostlistReferences: new List<HostlistReference>
            {
                nullIdReference,
            });

        Result<ProfileDefinition> result = mapper.Map(document, new List<StrategyPackDocument>());

        Assert.True(result.IsFailure);
        ErrorInfo? error = result.IsFailure ? result.Error : null;
        Assert.NotNull(error);
        Assert.Equal("HostlistReferenceInvalid", error!.Code);
        Assert.Equal(ErrorCategory.Hostlist, error.Category);
    }

    [Fact]
    public static void EmptyStrategyAndHostlistListsMapSuccessfully()
    {
        ProfileDocumentMapper mapper = new();

        ProfileDocument document = new(
            Id: new ProfileId("empty-profile"),
            DisplayName: "Empty Profile",
            Description: null,
            StrategyReferences: new List<StrategyReference>(),
            HostlistReferences: new List<HostlistReference>());

        Result<ProfileDefinition> result = mapper.Map(document, new List<StrategyPackDocument>());

        Assert.True(result.IsSuccess);
        ProfileDefinition definition = result.Value;
        Assert.Equal(new ProfileId("empty-profile"), definition.Id);
        Assert.Equal("Empty Profile", definition.DisplayName);
        Assert.Empty(definition.Strategies);
        Assert.Empty(definition.Hostlists);
    }

    [Fact]
    public static void NullProfileReturnsProfileDocumentMissing()
    {
        ProfileDocumentMapper mapper = new();

        Result<ProfileDefinition> result = mapper.Map(null!, new List<StrategyPackDocument>());

        Assert.True(result.IsFailure);
        ErrorInfo? error = result.IsFailure ? result.Error : null;
        Assert.NotNull(error);
        Assert.Equal("ProfileDocumentMissing", error!.Code);
        Assert.Equal(ErrorCategory.Profile, error.Category);
        Assert.Equal(ErrorSeverity.Error, error.Severity);
    }
}
