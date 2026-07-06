using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zapret2Pilot.App.Input;
using Zapret2Pilot.Core.Results;

namespace Zapret2Pilot.App.ViewModelTests.Input;

public sealed class ExternalLinkLauncherTests
{
    [Fact]
    public async Task InvalidUriIsRejected()
    {
        FakeProcessLauncher fake = new();
        ExternalLinkLauncher launcher = new(fake);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Result<Unit> result = await launcher.LaunchAsync("not a url", cancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("Z2P.INPUT.LINK_INVALID_URI", result.Error.Code);
        Assert.Empty(fake.Invocations);
    }

    [Fact]
    public async Task NullUriIsRejected()
    {
        FakeProcessLauncher fake = new();
        ExternalLinkLauncher launcher = new(fake);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Result<Unit> result = await launcher.LaunchAsync(null!, cancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("Z2P.INPUT.LINK_INVALID_URI", result.Error.Code);
        Assert.Empty(fake.Invocations);
    }

    [Fact]
    public async Task HttpSchemeIsRejected()
    {
        FakeProcessLauncher fake = new();
        ExternalLinkLauncher launcher = new(fake);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Result<Unit> result = await launcher.LaunchAsync("http://github.com/evil", cancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("Z2P.INPUT.LINK_SCHEME_NOT_ALLOWED", result.Error.Code);
        Assert.Empty(fake.Invocations);
    }

    [Fact]
    public async Task FileSchemeIsRejected()
    {
        FakeProcessLauncher fake = new();
        ExternalLinkLauncher launcher = new(fake);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Result<Unit> result = await launcher.LaunchAsync("file:///c:/windows/system32/cmd.exe", cancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("Z2P.INPUT.LINK_SCHEME_NOT_ALLOWED", result.Error.Code);
        Assert.Empty(fake.Invocations);
    }

    [Fact]
    public async Task NonWhitelistedHostIsRejected()
    {
        FakeProcessLauncher fake = new();
        ExternalLinkLauncher launcher = new(fake);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Result<Unit> result = await launcher.LaunchAsync("https://attacker.example.com/payload", cancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("Z2P.INPUT.LINK_HOST_NOT_ALLOWED", result.Error.Code);
        Assert.Empty(fake.Invocations);
    }

    [Fact]
    public async Task WhitelistedGithubHostIsLaunchedAndReturnsSuccess()
    {
        FakeProcessLauncher fake = new();
        ExternalLinkLauncher launcher = new(fake);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Result<Unit> result = await launcher.LaunchAsync(
            "https://github.com/MrFr3di/zapret2pilot",
            cancellationToken);

        Assert.True(result.IsSuccess);
        ProcessStartInfo startInfo = Assert.Single(fake.Invocations);
        Assert.Equal("https://github.com/MrFr3di/zapret2pilot", startInfo.FileName);
        Assert.True(startInfo.UseShellExecute);
    }

    [Fact]
    public async Task WhitelistedRawGithubHostIsLaunchedAndReturnsSuccess()
    {
        FakeProcessLauncher fake = new();
        ExternalLinkLauncher launcher = new(fake);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Result<Unit> result = await launcher.LaunchAsync(
            "https://raw.githubusercontent.com/MrFr3di/zapret2pilot/main/README.md",
            cancellationToken);

        Assert.True(result.IsSuccess);
        ProcessStartInfo startInfo = Assert.Single(fake.Invocations);
        Assert.Equal(
            "https://raw.githubusercontent.com/MrFr3di/zapret2pilot/main/README.md",
            startInfo.FileName);
    }

    [Fact]
    public async Task WhitelistedDocsHostIsLaunchedAndReturnsSuccess()
    {
        FakeProcessLauncher fake = new();
        ExternalLinkLauncher launcher = new(fake);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Result<Unit> result = await launcher.LaunchAsync("https://zapret2pilot.github.io/help", cancellationToken);

        Assert.True(result.IsSuccess);
        ProcessStartInfo startInfo = Assert.Single(fake.Invocations);
        Assert.Equal("https://zapret2pilot.github.io/help", startInfo.FileName);
    }

    [Fact]
    public async Task NullProcessFromLauncherBecomesFailure()
    {
        FakeProcessLauncher fake = new() { NextResult = null };
        ExternalLinkLauncher launcher = new(fake);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Result<Unit> result = await launcher.LaunchAsync(
            "https://github.com/MrFr3di/zapret2pilot",
            cancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("Z2P.INPUT.LINK_LAUNCH_FAILED", result.Error.Code);
    }

    [Fact]
    public async Task ExceptionFromLauncherBecomesFailure()
    {
        FakeProcessLauncher fake = new() { NextException = new InvalidOperationException("shell gone") };
        ExternalLinkLauncher launcher = new(fake);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Result<Unit> result = await launcher.LaunchAsync(
            "https://github.com/MrFr3di/zapret2pilot",
            cancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("Z2P.INPUT.LINK_LAUNCH_FAILED", result.Error.Code);
        Assert.Contains("shell gone", result.Error.Message, StringComparison.Ordinal);
    }

    private sealed class FakeProcessLauncher : IProcessLauncher
    {
        private readonly List<ProcessStartInfo> invocations = new();

        public IReadOnlyList<ProcessStartInfo> Invocations => invocations;

        public Process? NextResult { get; set; } = new Process();

        public Exception? NextException { get; set; }

        public Process? Start(ProcessStartInfo startInfo)
        {
            ArgumentNullException.ThrowIfNull(startInfo);
            invocations.Add(startInfo);

            if (NextException is not null)
            {
                throw NextException;
            }

            return NextResult;
        }
    }
}
