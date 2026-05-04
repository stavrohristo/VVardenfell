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

        protected override void OnCreate()
        {
            MorrowindVfxRenderDispatch.EnsureRegistered();
            _spawnQuery = GetEntityQuery(ComponentType.ReadOnly<MorrowindVfxSpawnRequest>());
            _removeQuery = GetEntityQuery(ComponentType.ReadOnly<MorrowindVfxRemoveRequest>());
        }

        protected override void OnUpdate()
        {
            CacheLoader cache = WorldResources.Cache
                ?? throw new InvalidOperationException("[VVardenfell][VFX] Runtime cache is not loaded.");

            MorrowindVfxRenderDispatch.EnsureRegistered();
            var resources = WorldResources.Vfx;
            if (resources == null)
            {
                resources = new MorrowindVfxResources(cache);
                WorldResources.Vfx = resources;
            }

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
            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        protected override void OnDestroy()
        {
            WorldResources.Vfx?.Dispose();
            WorldResources.Vfx = null;
        }
    }
}
