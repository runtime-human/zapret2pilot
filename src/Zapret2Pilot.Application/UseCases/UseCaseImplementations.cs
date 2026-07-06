using System;
using System.Threading;
using System.Threading.Tasks;
using Zapret2Pilot.Core.Results;

namespace Zapret2Pilot.Application.UseCases;

// ---------------------------------------------------------------------------
// Stub implementations.
//
// All use-case facades in 0.0.25 Packet 2 (Scope E) are wired into
// the production composition root as singletons. Their bodies are
// deliberately minimal: each method returns a failed
// Result<T> with code "Z2P.APPLICATION.NOT_IMPLEMENTED" so that
// DI graph resolution tests pass and the long-term contract is in
// place, while the real production logic is implemented in later
// packets. The classes are parameterless singletons so that no
// service-locator temptation is introduced into the composition
// root.
// ---------------------------------------------------------------------------

/// <summary>
/// Stub <see cref="IRuntimeUseCases"/> implementation. See the
/// file-level remarks for context.
/// </summary>
public sealed class RuntimeUseCases : IRuntimeUseCases
{
    /// <inheritdoc />
    public Task<Result<RuntimeSessionReadModel>> StartAsync(
        StartRuntimeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            Result.Failure<RuntimeSessionReadModel>(
                UseCaseErrors.NotImplemented(nameof(StartAsync))));
    }

    /// <inheritdoc />
    public Task<Result<Unit>> StopAsync(
        StopRuntimeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            Result.Failure<Unit>(
                UseCaseErrors.NotImplemented(nameof(StopAsync))));
    }

    /// <inheritdoc />
    public Task<Result<RuntimeStatusReadModel>> GetStatusAsync(
        GetRuntimeStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            Result.Failure<RuntimeStatusReadModel>(
                UseCaseErrors.NotImplemented(nameof(GetStatusAsync))));
    }
}

/// <summary>
/// Stub <see cref="IProfileUseCases"/> implementation. See the
/// file-level remarks for context.
/// </summary>
public sealed class ProfileUseCases : IProfileUseCases
{
    /// <inheritdoc />
    public Task<Result<ProfileReadModel>> ListAsync(
        ListProfilesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            Result.Failure<ProfileReadModel>(
                UseCaseErrors.NotImplemented(nameof(ListAsync))));
    }

    /// <inheritdoc />
    public Task<Result<ProfileReadModel>> ImportAsync(
        ImportProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            Result.Failure<ProfileReadModel>(
                UseCaseErrors.NotImplemented(nameof(ImportAsync))));
    }
}

/// <summary>
/// Stub <see cref="IRulesUseCases"/> implementation. See the
/// file-level remarks for context.
/// </summary>
public sealed class RulesUseCases : IRulesUseCases
{
    /// <inheritdoc />
    public Task<Result<RulesetReadModel>> ListAsync(
        ListRulesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            Result.Failure<RulesetReadModel>(
                UseCaseErrors.NotImplemented(nameof(ListAsync))));
    }
}

/// <summary>
/// Stub <see cref="IAutoDoctorUseCases"/> implementation. See the
/// file-level remarks for context.
/// </summary>
public sealed class AutoDoctorUseCases : IAutoDoctorUseCases
{
    /// <inheritdoc />
    public Task<Result<AutoDoctorSummaryReadModel>> RunQuickCheckAsync(
        RunQuickCheckRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            Result.Failure<AutoDoctorSummaryReadModel>(
                UseCaseErrors.NotImplemented(nameof(RunQuickCheckAsync))));
    }

    /// <inheritdoc />
    public Task<Result<AutoDoctorSummaryReadModel>> RunFullCheckAsync(
        RunFullCheckRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            Result.Failure<AutoDoctorSummaryReadModel>(
                UseCaseErrors.NotImplemented(nameof(RunFullCheckAsync))));
    }
}

/// <summary>
/// Stub <see cref="IDiagnosticsUseCases"/> implementation. See the
/// file-level remarks for context.
/// </summary>
public sealed class DiagnosticsUseCases : IDiagnosticsUseCases
{
    /// <inheritdoc />
    public Task<Result<DiagnosticsBundleReference>> ExportAsync(
        ExportDiagnosticsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            Result.Failure<DiagnosticsBundleReference>(
                UseCaseErrors.NotImplemented(nameof(ExportAsync))));
    }
}

/// <summary>
/// Stub <see cref="IRuntimeUpdateUseCases"/> implementation. See the
/// file-level remarks for context.
/// </summary>
public sealed class RuntimeUpdateUseCases : IRuntimeUpdateUseCases
{
    /// <inheritdoc />
    public Task<Result<RuntimeBundleReference>> CheckForUpdateAsync(
        CheckForUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            Result.Failure<RuntimeBundleReference>(
                UseCaseErrors.NotImplemented(nameof(CheckForUpdateAsync))));
    }

    /// <inheritdoc />
    public Task<Result<Unit>> ActivateCandidateAsync(
        ActivateCandidateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            Result.Failure<Unit>(
                UseCaseErrors.NotImplemented(nameof(ActivateCandidateAsync))));
    }
}

/// <summary>
/// Stub <see cref="IDataManagementUseCases"/> implementation. See the
/// file-level remarks for context.
/// </summary>
public sealed class DataManagementUseCases : IDataManagementUseCases
{
    /// <inheritdoc />
    public Task<Result<Unit>> CleanupOldDataAsync(
        CleanupOldDataRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            Result.Failure<Unit>(
                UseCaseErrors.NotImplemented(nameof(CleanupOldDataAsync))));
    }
}
