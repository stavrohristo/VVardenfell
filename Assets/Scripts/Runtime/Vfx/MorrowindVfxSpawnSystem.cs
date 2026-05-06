using System;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Vfx
{
    [UpdateInGroup(typeof(MorrowindPresentationBuildSystemGroup))]
    public partial struct MorrowindVfxSpawnSystem : ISystem
    {
        EntityQuery _spawnQuery;
        EntityQuery _removeQuery;
        EntityQuery _wakeQuery;
        EntityQuery _runtimeQuery;

        public void OnCreate(ref SystemState systemState)
        {
            MorrowindVfxRenderDispatch.EnsureRegistered();
            _spawnQuery = systemState.GetEntityQuery(ComponentType.ReadOnly<MorrowindVfxSpawnRequest>());
            _removeQuery = systemState.GetEntityQuery(ComponentType.ReadOnly<MorrowindVfxRemoveRequest>());
            _runtimeQuery = systemState.GetEntityQuery(ComponentType.ReadOnly<MorrowindVfxRuntimeState>());
            _wakeQuery = systemState.GetEntityQuery(new EntityQueryDesc
            {
                Any = new[]
                {
                    ComponentType.ReadOnly<MorrowindVfxSpawnRequest>(),
                    ComponentType.ReadOnly<MorrowindVfxRemoveRequest>(),
                    ComponentType.ReadOnly<MorrowindVfxRuntimeState>(),
                },
            });
            systemState.RequireForUpdate(_wakeQuery);
        }

        public void OnUpdate(ref SystemState systemState)
        {
            CacheLoader cache = WorldResources.Cache;
            if (cache == null)
            {
                if (!_spawnQuery.IsEmptyIgnoreFilter || !_removeQuery.IsEmptyIgnoreFilter)
                    throw new InvalidOperationException("[VVardenfell][VFX] Runtime cache is not loaded.");

                using var staleRuntime = _runtimeQuery.ToEntityArray(Allocator.Temp);
                if (staleRuntime.Length > 0)
                    systemState.EntityManager.DestroyEntity(staleRuntime);
                return;
            }

            MorrowindVfxRenderDispatch.EnsureRegistered();
            var resources = WorldResources.Vfx;
            if (resources == null)
            {
                resources = new MorrowindVfxResources(cache);
                WorldResources.Vfx = resources;
            }
            EnsureRuntimeState(ref systemState);

            using var spawnEntities = _spawnQuery.ToEntityArray(Allocator.Temp);
            using var spawns = _spawnQuery.ToComponentDataArray<MorrowindVfxSpawnRequest>(Allocator.Temp);
            using var removeEntities = _removeQuery.ToEntityArray(Allocator.Temp);
            using var removes = _removeQuery.ToComponentDataArray<MorrowindVfxRemoveRequest>(Allocator.Temp);

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            for (int i = 0; i < spawns.Length; i++)
            {
                resources.Spawn(cache, spawns[i]);
                ecb.DestroyEntity(spawnEntities[i]);
            }

            for (int i = 0; i < removes.Length; i++)
            {
                resources.Remove(removes[i].Owner, removes[i].EffectId);
                ecb.DestroyEntity(removeEntities[i]);
            }

            resources.Tick(SystemAPI.Time.DeltaTime, systemState.EntityManager);
            if (resources.InstanceCount <= 0)
                ClearRuntimeState(ref systemState, ref ecb);
            ecb.Playback(systemState.EntityManager);
            ecb.Dispose();
        }

        public void OnDestroy(ref SystemState systemState)
        {
            WorldResources.Vfx?.Dispose();
            WorldResources.Vfx = null;
        }

        void EnsureRuntimeState(ref SystemState systemState)
        {
            if (SystemAPI.HasSingleton<MorrowindVfxRuntimeState>())
                return;

            Entity entity = systemState.EntityManager.CreateEntity();
            systemState.EntityManager.SetName(entity, "VVardenfell.MorrowindVfxRuntime");
            systemState.EntityManager.AddComponentData(entity, new MorrowindVfxRuntimeState());
        }

        void ClearRuntimeState(ref SystemState systemState, ref EntityCommandBuffer ecb)
        {
            if (!SystemAPI.HasSingleton<MorrowindVfxRuntimeState>())
                return;

            ecb.DestroyEntity(SystemAPI.GetSingletonEntity<MorrowindVfxRuntimeState>());
        }
    }
}
