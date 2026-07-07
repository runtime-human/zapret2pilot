using System;
using Microsoft.Extensions.Logging;

namespace Zapret2Pilot.App.Lifecycle;

/// <summary>
/// Per-step execution context passed to
/// <see cref="IStartupStep.ExecuteAsync"/>. Bundles the live
/// service provider and a typed logger so each step can resolve
/// its own dependencies without coupling the coordinator to a
/// specific step's contract.
/// </summary>
/// <param name="Services">The application's root
/// <see cref="IServiceProvider"/>. The lifetime of the returned
/// services matches the host's lifetime (the coordinator is itself
/// an <c>IHostedService</c> started before the steps run).</param>
/// <param name="Logger">Logger that carries the step's
/// <see cref="IStartupStep.Name"/> as its category. All
/// step-scoped log output should be produced through this logger
/// so it can be filtered by step in production.</param>
public sealed record StartupStepContext(
    IServiceProvider Services,
    ILogger Logger);
