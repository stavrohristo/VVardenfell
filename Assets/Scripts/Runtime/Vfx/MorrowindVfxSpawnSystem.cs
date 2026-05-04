using System;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Vfx
{
    [UpdateInGroup(typeof(MorrowindPresentationBuildSystemGroup))]
    public partial class MorrowindVfxSpawnSystem : SystemBase
    {
        EntityQuery _spawnQuery;
        EntityQuery _removeQuery;
        EntityQuery _wakeQuery;
        EntityQuery _runtimeQuery;

        protected override void OnCreate()
        {
            MorrowindVfxRenderDispatch.EnsureRegistered();
            _spawnQuery = GetEntityQuery(ComponentType.ReadOnly<MorrowindVfxSpawnRequest>());
            _removeQuery = GetEntityQuery(ComponentType.ReadOnly<MorrowindVfxRemoveRequest>());
            _runtimeQuery = GetEntityQuery(ComponentType.ReadOnly<MorrowindVfxRuntimeState>());
            _wakeQuery = GetEntityQuery(new EntityQueryDesc
            {
                Any = new[]
                {
                    ComponentType.ReadOnly<MorrowindVfxSpawnRequest>(),
                    ComponentType.ReadOnly<MorrowindVfxRemoveRequest>(),
                    ComponentType.ReadOnly<MorrowindVfxRuntimeState>(),
                },
            });
            RequireForUpdate(_wakeQuery);
        }

        protected override void OnUpdate()
        {
            CacheLoader cache = WorldResources.Cache;
            if (cache == null)
            {
                if (!_spawnQuery.IsEmptyIgnoreFilter || !_removeQuery.IsEmptyIgnoreFilter)
                    throw new InvalidOperationException("[VVardenfell][VFX] Runtime cache is not loaded.");

                using var staleRuntime = _runtimeQuery.ToEntityArray(Allocator.Temp);
                if (staleRuntime.Length > 0)
                    EntityManager.DestroyEntity(staleRuntime);
                return;
            }

            MorrowindVfxRenderDispatch.EnsureRegistered();
            var resources = WorldResources.Vfx;
            if (resources == null)
            {
                resources = new MorrowindVfxResources(cache);
                WorldResources.Vfx = resources;
            }
            EnsureRuntimeState();

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

            resources.Tick(SystemAPI.Time.DeltaTime, EntityManager);
            if (resources.InstanceCount <= 0)
                ClearRuntimeState(ref ecb);
            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        protected override void OnDestroy()
        {
            WorldResources.Vfx?.Dispose();
            WorldResources.Vfx = null;
        }

        void EnsureRuntimeState()
        {
            if (SystemAPI.HasSingleton<MorrowindVfxRuntimeState>())
                return;

            Entity entity = EntityManager.CreateEntity();
            EntityManager.SetName(entity, "VVardenfell.MorrowindVfxRuntime");
            EntityManager.AddComponentData(entity, new MorrowindVfxRuntimeState());
        }

        void ClearRuntimeState(ref EntityCommandBuffer ecb)
        {
            if (!SystemAPI.HasSingleton<MorrowindVfxRuntimeState>())
                return;

            ecb.DestroyEntity(SystemAPI.GetSingletonEntity<MorrowindVfxRuntimeState>());
        }
    }
}
