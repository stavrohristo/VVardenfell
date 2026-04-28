using Unity.Entities;

namespace VVardenfell.Runtime.WorldState
{
    static class WorldStateStructuralUtility
    {
        public static void PlaybackAndDispose(EntityManager entityManager, ref EntityCommandBuffer ecb)
        {
            ecb.Playback(entityManager);
            ecb.Dispose();
        }
    }
}
