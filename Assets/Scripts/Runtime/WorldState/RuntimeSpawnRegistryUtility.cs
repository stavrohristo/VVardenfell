using Unity.Entities;

namespace VVardenfell.Runtime.WorldState
{
    public static class RuntimeSpawnRegistryUtility
    {
        const uint RuntimeRefHighBit = 0x80000000u;

        public static uint ComposeRuntimeRefId(uint ordinal)
        {
            uint nonZeroOrdinal = ordinal == 0u ? 1u : ordinal;
            return RuntimeRefHighBit | (nonZeroOrdinal & ~RuntimeRefHighBit);
        }

        public static bool IsRuntimeRefId(uint value) => (value & RuntimeRefHighBit) != 0;

        public static void MarkDestroyed(EntityManager entityManager, uint runtimeRefId)
        {
            RuntimeSpawnProjectionUtility.MarkDestroyed(entityManager, runtimeRefId);
        }
    }
}
