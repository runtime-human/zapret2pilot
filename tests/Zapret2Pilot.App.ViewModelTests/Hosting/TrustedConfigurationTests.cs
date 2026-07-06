using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;
using Zapret2Pilot.App.Hosting;

namespace Zapret2Pilot.App.ViewModelTests.Hosting;

public sealed class TrustedConfigurationTests
{
    [Fact]
    public static void EnvironmentVariableCannotOverrideTrustedTufRoot()
    {
        const string envKey = "Z2P__TrustedTufRoot";
        const string envValue = "attacker-controlled-root";
        Environment.SetEnvironmentVariable(envKey, envValue);

        try
        {
            using IHost host = Z2PHostBuilder.Build([]);
            Z2PApplicationOptions options = host.Services
                .GetRequiredService<IOptions<Z2PApplicationOptions>>().Value;

            Assert.Equal(Z2PConfigurationDefaults.TrustedTufRoot, options.TrustedTufRoot);
            Assert.NotEqual(envValue, options.TrustedTufRoot);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envKey, null);
        }
    }
}
