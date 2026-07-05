using System;

namespace Zapret2Pilot.Runtime.Kernel;

public readonly record struct RuntimeOperationId(Guid Value)
{
    public static RuntimeOperationId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N");
}
