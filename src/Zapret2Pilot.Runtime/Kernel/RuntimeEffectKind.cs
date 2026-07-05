namespace Zapret2Pilot.Runtime.Kernel;

public enum RuntimeEffectKind
{
    StartProcess,
    StopProcess,
    RecordGuardSuccess,
    RecordGuardFailure,
    PublishState,
}
