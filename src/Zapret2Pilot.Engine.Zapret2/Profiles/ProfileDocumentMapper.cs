using System;
using System.Collections.Generic;
using Zapret2Pilot.Core.Primitives;
using Zapret2Pilot.Core.Profiles;
using Zapret2Pilot.Core.Results;

namespace Zapret2Pilot.Engine.Zapret2.Profiles;

/// <summary>
/// Pure resolver implementation of <see cref="IProfileMapper"/>.
///
/// <para>
/// Responsibilities:
/// <list type="bullet">
///   <item>Validate that a profile document was supplied.</item>
///   <item>Resolve every <see cref="StrategyReference"/> against the
///   supplied <see cref="StrategyPackDocument"/>s by
///   <see cref="StrategyPackId"/>.</item>
///   <item>Resolve the named <see cref="StrategyDocument"/> inside the
///   matched pack and turn it into a <see cref="StrategyDefinition"/>.</item>
///   <item>Validate the structural shape of every
///   <see cref="HostlistReference"/> (non-null id, non-blank relative
///   path).</item>
///   <item>Assemble a <see cref="ProfileDefinition"/>.</item>
/// </list>
/// </para>
///
/// <para>
/// Out of scope (intentionally):
/// <list type="bullet">
///   <item>Path-traversal safety of hostlist relative paths — that is
///   <see cref="ProfileDocumentValidator"/>'s job.</item>
///   <item>Filesystem access or hostlist content loading.</item>
///   <item>Compiling to a <see cref="Zapret2Pilot.Core.Runtime.CompiledZapretPlan"/>.</item>
///   <item>Building <c>winws2</c> arguments.</item>
/// </list>
/// </para>
/// </summary>
public sealed class ProfileDocumentMapper : IProfileMapper
{
    /// <inheritdoc />
#pragma warning disable CA1822 // Mark members as static; intentionally instance per the planned public API.
    public Result<ProfileDefinition> Map(
        ProfileDocument profile,
        IReadOnlyList<StrategyPackDocument> strategyPacks)
#pragma warning restore CA1822
    {
        if (profile is null)
        {
            return Result.Failure<ProfileDefinition>(new ErrorInfo(
                "ProfileDocumentMissing",
                "Profile document is required.",
                ErrorSeverity.Error,
                ErrorCategory.Profile));
        }

        ArgumentNullException.ThrowIfNull(strategyPacks);

        List<StrategyAssignment> strategyAssignments = new(profile.StrategyReferences.Count);
        foreach (StrategyReference reference in profile.StrategyReferences)
        {
            Result<StrategyAssignment> resolved = ResolveStrategy(reference, strategyPacks);
            if (resolved.IsFailure)
            {
                return Result.Failure<ProfileDefinition>(resolved.Error);
            }

            strategyAssignments.Add(resolved.Value);
        }

        List<HostlistAssignment> hostlistAssignments = new(profile.HostlistReferences.Count);
        foreach (HostlistReference reference in profile.HostlistReferences)
        {
            Result<HostlistAssignment> resolved = ResolveHostlist(reference);
            if (resolved.IsFailure)
            {
                return Result.Failure<ProfileDefinition>(resolved.Error);
            }

            hostlistAssignments.Add(resolved.Value);
        }

        ProfileDefinition definition = new(
            id: profile.Id,
            displayName: profile.DisplayName,
            description: profile.Description,
            strategies: strategyAssignments,
            hostlists: hostlistAssignments);

        return Result.Success(definition);
    }

    private static Result<StrategyAssignment> ResolveStrategy(
        StrategyReference reference,
        IReadOnlyList<StrategyPackDocument> strategyPacks)
    {
        // The validator already guarantees the reference has a non-null
        // pack id and a non-blank strategy name; we still defend against
        // nulls here so the mapper is safe to call directly on a
        // hand-constructed document.
        if (reference is null)
        {
            return Result.Failure<StrategyAssignment>(new ErrorInfo(
                "StrategyReferenceInvalid",
                "Strategy reference is required.",
                ErrorSeverity.Error,
                ErrorCategory.StrategyPack));
        }

        StrategyPackDocument? pack = FindPack(reference.PackId, strategyPacks);
        if (pack is null)
        {
            return Result.Failure<StrategyAssignment>(new ErrorInfo(
                "StrategyPackMissing",
                $"Strategy pack '{reference.PackId.Value}' referenced by profile '{reference.StrategyName}' was not provided.",
                ErrorSeverity.Error,
                ErrorCategory.StrategyPack));
        }

        StrategyDocument? strategy = FindStrategy(reference.StrategyName, pack.Strategies);
        if (strategy is null)
        {
            return Result.Failure<StrategyAssignment>(new ErrorInfo(
                "StrategyMissing",
                $"Strategy '{reference.StrategyName}' was not found in pack '{reference.PackId.Value}'.",
                ErrorSeverity.Error,
                ErrorCategory.StrategyPack));
        }

        StrategyDefinition resolved = new(strategy.Name, strategy.Parameters);
        return Result.Success(new StrategyAssignment(reference.PackId, resolved));
    }

    private static StrategyPackDocument? FindPack(
        StrategyPackId packId,
        IReadOnlyList<StrategyPackDocument> strategyPacks)
    {
        foreach (StrategyPackDocument pack in strategyPacks)
        {
            if (pack is null)
            {
                continue;
            }

            if (Equals(pack.Id, packId))
            {
                return pack;
            }
        }

        return null;
    }

    private static StrategyDocument? FindStrategy(
        string strategyName,
        IReadOnlyList<StrategyDocument> strategies)
    {
        foreach (StrategyDocument strategy in strategies)
        {
            if (strategy is null)
            {
                continue;
            }

            if (string.Equals(strategy.Name, strategyName, StringComparison.Ordinal))
            {
                return strategy;
            }
        }

        return null;
    }

    private static Result<HostlistAssignment> ResolveHostlist(HostlistReference reference)
    {
        if (reference is null || reference.HostlistId is null ||
            string.IsNullOrWhiteSpace(reference.RelativePath))
        {
            return Result.Failure<HostlistAssignment>(new ErrorInfo(
                "HostlistReferenceInvalid",
                "Hostlist reference must specify a non-null hostlist id and a non-blank relative path.",
                ErrorSeverity.Error,
                ErrorCategory.Hostlist));
        }

        return Result.Success(new HostlistAssignment(reference.HostlistId, reference.RelativePath));
    }
}
