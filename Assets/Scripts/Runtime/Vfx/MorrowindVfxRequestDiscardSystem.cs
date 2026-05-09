using Unity.Entities;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Vfx
{
    [UpdateInGroup(typeof(MorrowindPresentationBuildSystemGroup))]
    public partial struct MorrowindVfxRequestDiscardSystem : ISystem
    {
        EntityQuery _spawnQuery;
        EntityQuery _removeQuery;
        EntityQuery _runtimeQuery;
        EntityQuery _wakeQuery;

        public void OnCreate(ref SystemState systemState)
        {
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

            systemState.RequireForUpdate<RuntimePresentationDisabled>();
            systemState.RequireForUpdate(_wakeQuery);
        }

        public void OnUpdate(ref SystemState systemState)
        {
            if (!_spawnQuery.IsEmptyIgnoreFilter)
                systemState.EntityManager.DestroyEntity(_spawnQuery);
            if (!_removeQuery.IsEmptyIgnoreFilter)
                systemState.EntityManager.DestroyEntity(_removeQuery);
            if (!_runtimeQuery.IsEmptyIgnoreFilter)
                systemState.EntityManager.DestroyEntity(_runtimeQuery);

            WorldResources.Vfx?.Dispose();
            WorldResources.Vfx = null;
        }
    }
}
