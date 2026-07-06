using System;
using Xunit;
using Zapret2Pilot.App.Input;

namespace Zapret2Pilot.App.ViewModelTests.Input;

public sealed class CliAllowlistTests
{
    [Fact]
    public void EmptyArgsAreAllowedAndDefault()
    {
        CliParseResult result = CliAllowlist.Parse([]);

        Assert.True(result.IsAllowed);
        Assert.Equal(CliLaunchMode.Default, result.Mode);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void LongHelpIsAllowedAsHelpMode()
    {
        CliParseResult result = CliAllowlist.Parse(["--help"]);

        Assert.True(result.IsAllowed);
        Assert.Equal(CliLaunchMode.Help, result.Mode);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public void ShortHelpIsAllowedAsHelpMode()
    {
        CliParseResult result = CliAllowlist.Parse(["-h"]);

        Assert.True(result.IsAllowed);
        Assert.Equal(CliLaunchMode.Help, result.Mode);
    }

    [Fact]
    public void VersionIsAllowedAsVersionMode()
    {
        CliParseResult result = CliAllowlist.Parse(["--version"]);

        Assert.True(result.IsAllowed);
        Assert.Equal(CliLaunchMode.Version, result.Mode);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public void UnknownArgumentIsRejected()
    {
        CliParseResult result = CliAllowlist.Parse(["--evil"]);

        Assert.False(result.IsAllowed);
        Assert.Equal(CliLaunchMode.Default, result.Mode);
        Assert.Equal("Z2P.INPUT.CLI_UNKNOWN_ARGUMENT", result.ErrorCode);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void EmptyArgumentIsRejected()
    {
        CliParseResult result = CliAllowlist.Parse([""]);

        Assert.False(result.IsAllowed);
        Assert.Equal("Z2P.INPUT.CLI_UNKNOWN_ARGUMENT", result.ErrorCode);
    }

    [Fact]
    public void MultipleArgumentsAreRejected()
    {
        CliParseResult result = CliAllowlist.Parse(["--help", "--version"]);

        Assert.False(result.IsAllowed);
        Assert.Equal(CliLaunchMode.Default, result.Mode);
        Assert.Equal("Z2P.INPUT.CLI_TOO_MANY_ARGUMENTS", result.ErrorCode);
    }

    [Fact]
    public void ValueStyleArgumentIsRejected()
    {
        CliParseResult result = CliAllowlist.Parse(["--TrustedTufRoot=attacker"]);

        Assert.False(result.IsAllowed);
        Assert.Equal("Z2P.INPUT.CLI_UNKNOWN_ARGUMENT", result.ErrorCode);
    }

    [Fact]
    public void NullArgsArrayIsRejected()
    {
        Assert.Throws<ArgumentNullException>(
            static () => CliAllowlist.Parse(null!));
    }
}
