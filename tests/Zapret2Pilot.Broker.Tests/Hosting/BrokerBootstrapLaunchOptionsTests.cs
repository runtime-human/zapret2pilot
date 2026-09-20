using Xunit;
using Zapret2Pilot.Broker.Hosting;

namespace Zapret2Pilot.Broker.Tests.Hosting;

public sealed class BrokerBootstrapLaunchOptionsTests
{
    [Fact]
    public static void ClosedCliAcceptsOnlyPipeNameAndPositiveAppPid()
    {
        bool accepted = BrokerBootstrapLaunchOptions.TryParse(
            [
                "--bootstrap-pipe",
                "z2p-bootstrap-abc123",
                "--app-pid",
                "4242",
            ],
            out BrokerBootstrapLaunchOptions? options,
            out string? error);

        Assert.True(accepted);
        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal(
            "z2p-bootstrap-abc123",
            options!.BootstrapPipeName);
        Assert.Equal(4242, options.AppProcessId);
    }

    [Theory]
    [InlineData("--bootstrap-secret", "deadbeef")]
    [InlineData("--runtime-path", "C:\\runtime.exe")]
    [InlineData("--app-pid", "0")]
    public static void ClosedCliRejectsSecretPathAndInvalidPid(
        string key,
        string value)
    {
        string[] args = key == "--app-pid"
            ? [
                "--bootstrap-pipe",
                "z2p-bootstrap-abc123",
                key,
                value,
            ]
            : [
                "--bootstrap-pipe",
                "z2p-bootstrap-abc123",
                key,
                value,
            ];

        bool accepted = BrokerBootstrapLaunchOptions.TryParse(
            args,
            out _,
            out _);

        Assert.False(accepted);
    }
}
