using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Zapret2Pilot.Runtime.Locking;

namespace Zapret2Pilot.Runtime.Detection;

/// <summary>
/// Production implementation of <see cref="IRuntimeOwnershipDetector"/>.
/// Verifies that the process described by the runtime lock metadata still
/// corresponds to the expected Z2P runtime by checking PID, process name,
/// executable path, command-line hash, plan hash and process start time.
/// </summary>
public sealed class RuntimeOwnershipDetector : IRuntimeOwnershipDetector
{
    private const int StartTimeToleranceSeconds = 5;

    private readonly IProcessSystemAccessor processAccessor;

    public RuntimeOwnershipDetector(IProcessSystemAccessor processAccessor)
    {
        ArgumentNullException.ThrowIfNull(processAccessor);

        this.processAccessor = processAccessor;
    }

    public RuntimeOwnershipVerificationResult Verify(
        RuntimeLockMetadata metadata,
        string expectedPlanHash)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPlanHash);

        if (!processAccessor.TryGetProcessById(
                metadata.Process.ProcessId,
                out IProcessSnapshot? snapshot) || snapshot is null)
        {
            return RuntimeOwnershipVerificationResult.NoProcess();
        }

        if (!string.Equals(
                snapshot.ProcessName,
                metadata.Process.ProcessName,
                StringComparison.OrdinalIgnoreCase))
        {
            return RuntimeOwnershipVerificationResult.ProcessNameMismatch();
        }

        if (!string.Equals(
                snapshot.ExecutablePath,
                metadata.Process.ExecutablePath,
                StringComparison.OrdinalIgnoreCase))
        {
            return RuntimeOwnershipVerificationResult.ExecutablePathMismatch();
        }

        if (snapshot.CommandLine is null)
        {
            return RuntimeOwnershipVerificationResult.CommandLineUnverifiable();
        }

        string actualCommandLineHash = ComputeCommandLineHash(snapshot.CommandLine);
        if (!string.Equals(
                actualCommandLineHash,
                metadata.Process.CommandLineHash,
                StringComparison.Ordinal))
        {
            return RuntimeOwnershipVerificationResult.CommandLineHashMismatch();
        }

        if (!string.Equals(
                metadata.Process.PlanHash,
                expectedPlanHash,
                StringComparison.Ordinal))
        {
            return RuntimeOwnershipVerificationResult.PlanHashMismatch();
        }

        TimeSpan delta = snapshot.StartedAtUtc - metadata.Process.ProcessStartedAtUtc;
        if (Math.Abs(delta.TotalSeconds) > StartTimeToleranceSeconds)
        {
            return RuntimeOwnershipVerificationResult.ProcessStartTimeMismatch();
        }

        return RuntimeOwnershipVerificationResult.OwnedByExpectedRuntime();
    }

    private static string ComputeCommandLineHash(string commandLine)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(commandLine);
        byte[] hashBytes = SHA256.HashData(bytes);

        StringBuilder builder = new(hashBytes.Length * 2);
        foreach (byte b in hashBytes)
        {
            builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
