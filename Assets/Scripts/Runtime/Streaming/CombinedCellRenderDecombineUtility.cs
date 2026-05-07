using Unity.Entities;
using Unity.Rendering;

namespace VVardenfell.Runtime.Streaming
{
    internal static class CombinedCellRenderDecombineUtility
    {
        public static void DecombineIfLinked(EntityManager em, Entity logicalEntity)
        {
            if (logicalEntity == Entity.Null || !em.Exists(logicalEntity) || !em.HasBuffer<CombinedCellRenderLink>(logicalEntity))
                return;

            var links = em.GetBuffer<CombinedCellRenderLink>(logicalEntity);
            for (int i = 0; i < links.Length; i++)
            {
                Entity chunkEntity = links[i].Chunk;
                if (chunkEntity == Entity.Null || !em.Exists(chunkEntity) || !em.HasComponent<CombinedCellRenderChunk>(chunkEntity))
                    continue;

                var chunk = em.GetComponentData<CombinedCellRenderChunk>(chunkEntity);
                bool chunkWasVisible = em.HasComponent<MaterialMeshInfo>(chunkEntity)
                    && em.IsComponentEnabled<MaterialMeshInfo>(chunkEntity);
                if (chunk.Disabled == 0)
                {
                    chunk.Disabled = 1;
                    em.SetComponentData(chunkEntity, chunk);
                    if (!em.HasComponent<CombinedCellRenderSuppressed>(chunkEntity))
                        em.AddComponent<CombinedCellRenderSuppressed>(chunkEntity);
                    if (em.HasComponent<MaterialMeshInfo>(chunkEntity))
                        em.SetComponentEnabled<MaterialMeshInfo>(chunkEntity, false);
                }

                if (!em.HasBuffer<CombinedCellRenderChunkMember>(chunkEntity))
                    continue;

                var members = em.GetBuffer<CombinedCellRenderChunkMember>(chunkEntity);
                for (int m = 0; m < members.Length; m++)
                {
                    Entity renderEntity = members[m].RenderEntity;
                    if (renderEntity == Entity.Null || !em.Exists(renderEntity) || !em.HasComponent<MaterialMeshInfo>(renderEntity))
                        continue;

                    if (em.HasComponent<CombinedCellRenderSuppressed>(renderEntity))
                        em.RemoveComponent<CombinedCellRenderSuppressed>(renderEntity);
                    em.SetComponentEnabled<MaterialMeshInfo>(renderEntity, chunkWasVisible);
                }
            }
        }
    }
}
