using System;

namespace Zapret2Pilot.Runtime.Ownership;

public sealed class RuntimeOwnershipThreadAffinityException : Exception
{
    public RuntimeOwnershipThreadAffinityException()
        : base("Runtime ownership lease must be used on the same managed thread that acquired it.")
    {
    }
}
