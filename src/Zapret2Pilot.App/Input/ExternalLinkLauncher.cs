using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Zapret2Pilot.Core.Results;

namespace Zapret2Pilot.App.Input;

/// <summary>
/// Test-seam for <see cref="System.Diagnostics.Process.Start(ProcessStartInfo)"/>.
/// In production this is implemented by <see cref="ProcessLauncher"/>; in tests
/// the launcher can be replaced by a fake that records the
/// <see cref="ProcessStartInfo"/> that would have been used.
/// </summary>
public interface IProcessLauncher
{
    Process? Start(ProcessStartInfo startInfo);
}

/// <summary>
/// Production <see cref="IProcessLauncher"/> backed by
/// <see cref="System.Diagnostics.Process.Start(ProcessStartInfo)"/>.
/// </summary>
public sealed class ProcessLauncher : IProcessLauncher
{
    public Process? Start(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        return Process.Start(startInfo);
    }
}

/// <summary>
/// Trusted boundary for opening an external link from the UI. Only
/// whitelisted schemes and hosts are allowed to reach the shell, and
/// even then the launch is funnelled through a testable
/// <see cref="IProcessLauncher"/> so no caller can invoke
/// <c>ShellExecute</c> on arbitrary user input.
/// </summary>
public interface IExternalLinkLauncher
{
    Task<Result<Unit>> LaunchAsync(string url, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IExternalLinkLauncher"/>. The set of
/// permitted schemes and hosts is intentionally tiny and is owned
/// exclusively by this type — callers cannot extend it.
/// </summary>
public sealed class ExternalLinkLauncher : IExternalLinkLauncher
{
    private static readonly HashSet<string> AllowedSchemes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "https",
        };

    private static readonly HashSet<string> AllowedHosts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "github.com",
            "raw.githubusercontent.com",
            "zapret2pilot.github.io",
        };

    private readonly IProcessLauncher launcher;

    public ExternalLinkLauncher(IProcessLauncher? launcher = null)
    {
        this.launcher = launcher ?? new ProcessLauncher();
    }

    public Task<Result<Unit>> LaunchAsync(string url, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken; // No async work yet; the seam is here for future use.

        if (string.IsNullOrWhiteSpace(url))
        {
            return Task.FromResult(Result.Failure<Unit>(new ErrorInfo(
                "Z2P.INPUT.LINK_INVALID_URI",
                "Link URL is required.",
                ErrorSeverity.Error,
                ErrorCategory.Validation)));
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return Task.FromResult(Result.Failure<Unit>(new ErrorInfo(
                "Z2P.INPUT.LINK_INVALID_URI",
                "Link URL is not a valid absolute URI.",
                ErrorSeverity.Error,
                ErrorCategory.Validation)));
        }

        if (!AllowedSchemes.Contains(uri.Scheme))
        {
            return Task.FromResult(Result.Failure<Unit>(new ErrorInfo(
                "Z2P.INPUT.LINK_SCHEME_NOT_ALLOWED",
                $"Link scheme '{uri.Scheme}' is not allowed.",
                ErrorSeverity.Error,
                ErrorCategory.Security)));
        }

        if (string.IsNullOrEmpty(uri.Host) || !AllowedHosts.Contains(uri.Host))
        {
            return Task.FromResult(Result.Failure<Unit>(new ErrorInfo(
                "Z2P.INPUT.LINK_HOST_NOT_ALLOWED",
                $"Link host '{uri.Host}' is not in the allowlist.",
                ErrorSeverity.Error,
                ErrorCategory.Security)));
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true,
        };

        try
        {
            Process? process = launcher.Start(startInfo);
            if (process is null)
            {
                return Task.FromResult(Result.Failure<Unit>(new ErrorInfo(
                    "Z2P.INPUT.LINK_LAUNCH_FAILED",
                    "External link launcher returned no process.",
                    ErrorSeverity.Error,
                    ErrorCategory.Application)));
            }

            return Task.FromResult(Result.Success(Unit.Instance));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result.Failure<Unit>(new ErrorInfo(
                "Z2P.INPUT.LINK_LAUNCH_FAILED",
                ex.Message,
                ErrorSeverity.Error,
                ErrorCategory.Application)));
        }
    }
}
