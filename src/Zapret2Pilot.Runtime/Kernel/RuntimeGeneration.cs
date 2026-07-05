namespace Zapret2Pilot.Runtime.Kernel;

public readonly record struct RuntimeGeneration(long Value)
{
    public static RuntimeGeneration Initial => new(0);

    public RuntimeGeneration Next() => new(Value + 1);
}
