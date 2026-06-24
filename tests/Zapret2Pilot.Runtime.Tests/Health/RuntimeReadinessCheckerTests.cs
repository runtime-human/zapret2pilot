using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zapret2Pilot.Core.Results;
using Zapret2Pilot.Runtime.Health;

namespace Zapret2Pilot.Runtime.Tests.Health;

/// <summary>
/// Focused xUnit tests for <see cref="RuntimeReadinessChecker"/>.
///
/// <para>
/// The success and failure cases need a real, observable child process
/// because the checker reads <see cref="Process.HasExited"/> from a live
/// <see cref="System.Diagnostics.Process"/> handle. The tests launch the
/// bundled <c>Zapret2Pilot.Testing.FakeRuntime</c> executable, which
/// prints <c>READY</c> on stdout and blocks until terminated. The
/// launched child is killed and waited on inside each test's
/// <c>finally</c> block so no process leaks across the suite.
/// </para>
/// </summary>
public sealed class RuntimeReadinessCheckerTests
{
    private static readonly TimeSpan ShortReadinessTimeout = TimeSpan.FromMilliseconds(100);

    [Fact]
    public static async Task SuccessWhenProcessIsAliveAfterTimeout()
    {
        using LaunchedFakeRuntime launched = LaunchedFakeRuntime.Start();

        try
        {
            Result<Unit> result = await RuntimeReadinessChecker.CheckAsync(
                launched.Process,
                ShortReadinessTimeout,
                CancellationToken.None);

            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : string.Empty);
        }
        finally
        {
            launched.Terminate();
        }
    }

    [Fact]
    public static async Task FailureWhenProcessHasAlreadyExited()
    {
        using LaunchedFakeRuntime launched = LaunchedFakeRuntime.Start();
        launched.Terminate();
        launched.Process.WaitForExit(5000);

        Result<Unit> result = await RuntimeReadinessChecker.CheckAsync(
            launched.Process,
            ShortReadinessTimeout,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("RUNTIME_NOT_READY", result.Error.Code);
        Assert.Equal(ErrorCategory.Runtime, result.Error.Category);
    }

    [Fact]
    public static async Task NullProcessThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            RuntimeReadinessChecker.CheckAsync(
                process: null!,
                readinessTimeout: ShortReadinessTimeout,
                cancellationToken: CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public static async Task NonPositiveTimeoutThrowsArgumentOutOfRangeException(int totalMilliseconds)
    {
        using LaunchedFakeRuntime launched = LaunchedFakeRuntime.Start();
        try
        {
            TimeSpan timeout = TimeSpan.FromMilliseconds(totalMilliseconds);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                RuntimeReadinessChecker.CheckAsync(
                    process: launched.Process,
                    readinessTimeout: timeout,
                    cancellationToken: CancellationToken.None));
        }
        finally
        {
            launched.Terminate();
        }
    }

    [Fact]
    public static async Task CancellationTokenTriggersOperationCanceledException()
    {
        using LaunchedFakeRuntime launched = LaunchedFakeRuntime.Start();
        try
        {
            using CancellationTokenSource cts = new();

            // Schedule cancellation before the readiness window elapses.
            cts.CancelAfter(ShortReadinessTimeout / 2);

            // Task.Delay throws TaskCanceledException (a subclass of
            // OperationCanceledException) when the token is cancelled.
            // ThrowsAnyAsync accepts the derived type.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                RuntimeReadinessChecker.CheckAsync(
                    process: launched.Process,
                    readinessTimeout: TimeSpan.FromSeconds(5),
                    cancellationToken: cts.Token));
        }
        finally
        {
            launched.Terminate();
        }
    }

    /// <summary>
    /// Helper that owns a launched FakeRuntime process and guarantees
    /// cleanup via <see cref="IDisposable"/>.
    /// </summary>
    private sealed class LaunchedFakeRuntime : IDisposable
    {
        private bool terminated;

        private LaunchedFakeRuntime(Process process, string executablePath)
        {
            Process = process;
            ExecutablePath = executablePath;
        }

        public Process Process { get; }

        public string ExecutablePath { get; }

        public static LaunchedFakeRuntime Start()
        {
            string baseDir = AppContext.BaseDirectory;
            string executablePath = Path.Combine(baseDir, "Zapret2Pilot.Testing.FakeRuntime.exe");

            if (!File.Exists(executablePath))
            {
                throw new FileNotFoundException(
                    $"Fake runtime not found at '{executablePath}'. " +
                    "Ensure tests/Zapret2Pilot.Testing.FakeRuntime is referenced by the test project.",
                    executablePath);
            }

            ProcessStartInfo startInfo = new()
            {
                FileName = executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    $"Failed to start fake runtime at '{executablePath}'.");

            return new LaunchedFakeRuntime(process, executablePath);
        }

        public void Terminate()
        {
            if (terminated)
            {
                return;
            }

            terminated = true;

            try
            {
                if (!Process.HasExited)
                {
                    Process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // Already disposed/exited.
            }
            catch (Win32Exception)
            {
                // Race during termination.
            }

            try
            {
                Process.WaitForExit(5000);
            }
            catch (InvalidOperationException)
            {
                // Already disposed.
            }
            catch (Win32Exception)
            {
                // Race.
            }
        }

        public void Dispose()
        {
            Terminate();

            try
            {
                Process.Dispose();
            }
            catch
            {
                // Best effort.
            }
        }
    }
}
