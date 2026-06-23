using System.Collections.Generic;
using Xunit;
using Zapret2Pilot.Core.Primitives;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Engine.Zapret2.Profiles;

namespace Zapret2Pilot.Engine.Zapret2.Tests.Profiles;

/// <summary>
/// Focused unit tests for <see cref="ProfileDocumentValidator"/>.
/// The validator is intentionally DTO-level and must not access the
/// filesystem or perform any IO.
/// </summary>
public sealed class ProfileDocumentValidatorTests
{
    [Fact]
    public static void ValidDocumentPassesValidation()
    {
        ProfileDocumentValidator validator = new();
        ProfileDocument document = CreateValidDocument();

        Result result = validator.Validate(document);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public static void EmptyIdRejected()
    {
        ProfileDocumentValidator validator = new();
        ProfileDocument document = CreateValidDocument() with
        {
            Id = null!,
        };

        Result result = validator.Validate(document);

        Assert.True(result.IsFailure);
        ErrorInfo? error = result.IsFailure ? result.Error : null;
        Assert.NotNull(error);
        Assert.Equal("ProfileIdMissing", error!.Code);
        Assert.Equal(ErrorCategory.Validation, error.Category);
    }

    [Fact]
    public static void EmptyDisplayNameRejected()
    {
        ProfileDocumentValidator validator = new();
        ProfileDocument document = CreateValidDocument() with
        {
            DisplayName = string.Empty,
        };

        Result result = validator.Validate(document);

        Assert.True(result.IsFailure);
        ErrorInfo? error = result.IsFailure ? result.Error : null;
        Assert.NotNull(error);
        Assert.Equal("ProfileDisplayNameMissing", error!.Code);
        Assert.Equal(ErrorCategory.Validation, error.Category);
    }

    [Fact]
    public static void WhitespaceDisplayNameRejected()
    {
        ProfileDocumentValidator validator = new();
        ProfileDocument document = CreateValidDocument() with
        {
            DisplayName = "   ",
        };

        Result result = validator.Validate(document);

        Assert.True(result.IsFailure);
        ErrorInfo? error = result.IsFailure ? result.Error : null;
        Assert.NotNull(error);
        Assert.Equal("ProfileDisplayNameMissing", error!.Code);
        Assert.Equal(ErrorCategory.Validation, error.Category);
    }

    [Fact]
    public static void DuplicateStrategyReferencesRejected()
    {
        ProfileDocumentValidator validator = new();

        StrategyPackId packId = new("base-pack");
        StrategyReference duplicate = new(packId, "strategy-1");

        ProfileDocument document = CreateValidDocument() with
        {
            StrategyReferences = new List<StrategyReference>
            {
                duplicate,
                duplicate,
            },
        };

        Result result = validator.Validate(document);

        Assert.True(result.IsFailure);
        ErrorInfo? error = result.IsFailure ? result.Error : null;
        Assert.NotNull(error);
        Assert.Equal("DuplicateStrategyReference", error!.Code);
        Assert.Equal(ErrorCategory.Validation, error.Category);
    }

    [Fact]
    public static void InvalidHostlistPathRejected()
    {
        ProfileDocumentValidator validator = new();

        HostlistId hostlistId = new("default-list");
        HostlistReference unsafeReference = new(hostlistId, "../escape.txt");

        ProfileDocument document = CreateValidDocument() with
        {
            HostlistReferences = new List<HostlistReference>
            {
                unsafeReference,
            },
        };

        Result result = validator.Validate(document);

        Assert.True(result.IsFailure);
        ErrorInfo? error = result.IsFailure ? result.Error : null;
        Assert.NotNull(error);
        Assert.Equal("HostlistPathUnsafe", error!.Code);
        Assert.Equal(ErrorCategory.Security, error.Category);
    }

    [Fact]
    public static void AbsoluteHostlistPathRejected()
    {
        ProfileDocumentValidator validator = new();

        HostlistId hostlistId = new("default-list");
        HostlistReference absoluteReference = new(hostlistId, "C:\\hostlists\\default.txt");

        ProfileDocument document = CreateValidDocument() with
        {
            HostlistReferences = new List<HostlistReference>
            {
                absoluteReference,
            },
        };

        Result result = validator.Validate(document);

        Assert.True(result.IsFailure);
        ErrorInfo? error = result.IsFailure ? result.Error : null;
        Assert.NotNull(error);
        Assert.Equal("HostlistPathUnsafe", error!.Code);
        Assert.Equal(ErrorCategory.Security, error.Category);
    }

    [Fact]
    public static void NullStrategyReferencesRejected()
    {
        ProfileDocumentValidator validator = new();
        ProfileDocument document = CreateValidDocument() with
        {
            StrategyReferences = null!,
        };

        Result result = validator.Validate(document);

        Assert.True(result.IsFailure);
        ErrorInfo? error = result.IsFailure ? result.Error : null;
        Assert.NotNull(error);
        Assert.Equal("StrategyReferencesMissing", error!.Code);
        Assert.Equal(ErrorCategory.Validation, error.Category);
    }

    [Fact]
    public static void NullHostlistReferencesRejected()
    {
        ProfileDocumentValidator validator = new();
        ProfileDocument document = CreateValidDocument() with
        {
            HostlistReferences = null!,
        };

        Result result = validator.Validate(document);

        Assert.True(result.IsFailure);
        ErrorInfo? error = result.IsFailure ? result.Error : null;
        Assert.NotNull(error);
        Assert.Equal("HostlistReferencesMissing", error!.Code);
        Assert.Equal(ErrorCategory.Validation, error.Category);
    }

    [Fact]
    public static void InvalidStrategyReferenceRejected()
    {
        ProfileDocumentValidator validator = new();

        StrategyPackId packId = new("base-pack");
        StrategyReference blankNameReference = new(packId, string.Empty);

        ProfileDocument document = CreateValidDocument() with
        {
            StrategyReferences = new List<StrategyReference>
            {
                blankNameReference,
            },
        };

        Result result = validator.Validate(document);

        Assert.True(result.IsFailure);
        ErrorInfo? error = result.IsFailure ? result.Error : null;
        Assert.NotNull(error);
        Assert.Equal("StrategyReferenceInvalid", error!.Code);
        Assert.Equal(ErrorCategory.Validation, error.Category);
    }

    [Fact]
    public static void InvalidHostlistReferenceRejected()
    {
        ProfileDocumentValidator validator = new();

        HostlistId hostlistId = new("default-list");
        HostlistReference blankPathReference = new(hostlistId, string.Empty);

        ProfileDocument document = CreateValidDocument() with
        {
            HostlistReferences = new List<HostlistReference>
            {
                blankPathReference,
            },
        };

        Result result = validator.Validate(document);

        Assert.True(result.IsFailure);
        ErrorInfo? error = result.IsFailure ? result.Error : null;
        Assert.NotNull(error);
        Assert.Equal("HostlistReferenceInvalid", error!.Code);
        Assert.Equal(ErrorCategory.Validation, error.Category);
    }

    private static ProfileDocument CreateValidDocument()
    {
        return new ProfileDocument(
            Id: new ProfileId("test-profile"),
            DisplayName: "Test Profile",
            Description: "Default test profile used by validator tests.",
            StrategyReferences: new List<StrategyReference>(),
            HostlistReferences: new List<HostlistReference>());
    }
}
