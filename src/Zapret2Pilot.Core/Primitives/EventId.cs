using Zapret2Pilot.Core.Internal;

namespace Zapret2Pilot.Core.Primitives;

public sealed record class EventId
{
    public EventId(string value)
    {
        Value = Guard.NotNullOrWhiteSpace(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString()
    {
        return Value;
    }
}
