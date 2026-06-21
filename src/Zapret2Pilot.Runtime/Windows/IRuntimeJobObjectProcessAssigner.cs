namespace Zapret2Pilot.Runtime.Windows;

public interface IRuntimeJobObjectProcessAssigner
{
    RuntimeJobObjectAssignmentResult Assign(
        IRuntimeJobObject jobObject,
        RuntimeProcessHandle processHandle);
}
