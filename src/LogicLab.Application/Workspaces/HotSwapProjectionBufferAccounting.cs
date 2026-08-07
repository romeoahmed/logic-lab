using LogicLab.Engine.Simulation;

namespace LogicLab.Application.Workspaces;

internal static class HotSwapProjectionBufferAccounting
{
    private const ulong OwnedReferenceSlotBytes = sizeof(ulong);
    private const ulong UnpackedLogicValueBytes = sizeof(byte);

    public static HotSwapConsumerBufferRequirements RequirementsFor(
        SimulationProjection retainedProjection)
    {
        ArgumentNullException.ThrowIfNull(retainedProjection);

        try
        {
            var retainedBytes = checked(
                (ulong)retainedProjection.Probes.Count * OwnedReferenceSlotBytes);
            foreach (var probe in retainedProjection.Probes)
            {
                retainedBytes = checked(
                    retainedBytes
                    + ((ulong)probe.Value.Count * UnpackedLogicValueBytes));
            }

            return new HotSwapConsumerBufferRequirements(
                retainedBytes,
                ownedReferenceSlotsPerObservedProbe: 1,
                ownedBytesPerObservedProbeBit: UnpackedLogicValueBytes);
        }
        catch (OverflowException)
        {
            return new HotSwapConsumerBufferRequirements(
                ulong.MaxValue,
                ownedReferenceSlotsPerObservedProbe: 1,
                ownedBytesPerObservedProbeBit: UnpackedLogicValueBytes);
        }
    }
}
