using System;
using System.Collections.Generic;
using Zapret2Pilot.Core.Results;

namespace Zapret2Pilot.Runtime.Kernel;

public sealed record RuntimeTransition(
    RuntimeKernelState Previous,
    RuntimeKernelState Next,
    IReadOnlyList<RuntimeEffectIntent> Effects,
    IReadOnlyList<object> Events,
    Result<Unit> Outcome);
