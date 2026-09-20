using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Zapret2Pilot.Broker.Tests.Runtime;

public sealed class BrokerHardKillContainmentTests
{
    private const string CrashHostExecutable =
        "Zapret2Pilot.Testing.BrokerCrashHost.exe";

    [Fact]
    public static async Task HardKilledBroker_LeavesNoOrphanFakeRuntimeProcess()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string helperPath = ResolveCrashHostPath();

        Assert.True(
            File.Exists(helperPath),
            $"Broker crash host not found at '{helperPath}'.");

        string root = Path.Combine(
            Path.GetTempPath(),
            "z2p-broker-hard-kill",
            Guid.NewGuid().ToString("N"));
        string readyFile = Path.Combine(
            root,
            "runtime-pid.txt");

        Directory.CreateDirectory(root);

        ProcessStartInfo startInfo = new(helperPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        startInfo.ArgumentList.Add(root);
        startInfo.ArgumentList.Add(readyFile);

        using Process broker = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Failed to start Broker crash-test host.");

        int runtimeProcessId = 0;

        try
        {
            string ready = await WaitForReadyFileAsync(
                broker,
                readyFile,
                TestContext.Current.CancellationToken);

            Assert.DoesNotStartWith(
                "ERROR:",
                ready,
                StringComparison.Ordinal);

            Assert.True(
                int.TryParse(
                    ready,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out runtimeProcessId)
                && runtimeProcessId > 0,
                $"Crash host returned invalid FakeRuntime PID: '{ready}'.");

            Assert.True(
                IsProcessAlive(runtimeProcessId),
                "FakeRuntime must be alive before the Broker is killed.");

            // This is deliberately NOT Kill(entireProcessTree: true).
            // Only the Broker process is terminated. FakeRuntime must die
            // because the Broker-owned kill-on-close Job Object handle is
            // closed by OS process teardown.
            broker.Kill(entireProcessTree: false);
            await broker.WaitForExitAsync(
                TestContext.Current.CancellationToken);

            await WaitForProcessGoneAsync(
                runtimeProcessId,
                TestContext.Current.CancellationToken);

            Assert.False(
                IsProcessAlive(runtimeProcessId),
                "FakeRuntime survived hard Broker process termination.");
        }
        finally
        {
            if (!broker.HasExited)
            {
                // Cleanup fallback is allowed to kill the tree only after the
                // proof path has failed/aborted; it is not part of the success
                // assertion above.
                broker.Kill(entireProcessTree: true);
                await broker.WaitForExitAsync(
                    CancellationToken.None);
            }

            if (runtimeProcessId > 0
                && IsProcessAlive(runtimeProcessId))
            {
                try
                {
                    using Process runtime =
                        Process.GetProcessById(runtimeProcessId);
                    runtime.Kill(entireProcessTree: true);
                }
                catch (ArgumentException)
                {
                    // Process already exited.
                }
                catch (InvalidOperationException)
                {
                    // Process already exited.
                }
            }

            TryDelete(root);
        }
    }

    private static string ResolveCrashHostPath()
    {
        string repositoryRoot = FindRepositoryRoot();

        DirectoryInfo targetDirectory =
            new(AppContext.BaseDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));

        string configuration =
            targetDirectory.Parent?.Name
            ?? throw new InvalidOperationException(
                "Could not determine test build configuration.");

        return Path.Combine(
            repositoryRoot,
            "tests",
            "Zapret2Pilot.Testing.BrokerCrashHost",
            "bin",
            configuration,
            "net10.0-windows10.0.26100.0",
            CrashHostExecutable);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory =
            new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "Zapret2Pilot.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate repository root from Broker test output.");
    }

    private static async Task<string> WaitForReadyFileAsync(
        Process broker,
        string readyFile,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline =
            DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(readyFile))
            {
                string content = await File.ReadAllTextAsync(
                    readyFile,
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(content))
                {
                    return content.Trim();
                }
            }

            if (broker.HasExited)
            {
                string stderr =
                    await broker.StandardError.ReadToEndAsync(
                        cancellationToken);
                string stdout =
                    await broker.StandardOutput.ReadToEndAsync(
                        cancellationToken);

                throw new InvalidOperationException(
                    $"Broker crash host exited before readiness. ExitCode={broker.ExitCode}; stderr='{stderr}'; stdout='{stdout}'.");
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(50),
                cancellationToken);
        }

        throw new TimeoutException(
            "Broker crash host did not publish FakeRuntime PID within 20 seconds.");
    }

    private static async Task WaitForProcessGoneAsync(
        int processId,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline =
            DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsProcessAlive(processId))
            {
                return;
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(50),
                cancellationToken);
        }
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using Process process =
                Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(
                    directory,
                    recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort test cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort test cleanup.
        }
    }
}
