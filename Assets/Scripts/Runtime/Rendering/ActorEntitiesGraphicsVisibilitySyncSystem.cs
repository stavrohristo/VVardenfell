using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Rendering
{
    [UpdateInGroup(typeof(MorrowindPresentationSystemGroup))]
    public partial struct ActorEntitiesGraphicsVisibilitySyncSystem : ISystem
    {
        ComponentLookup<ActorRenderVisible> _visibleLookup;

        public void OnCreate(ref SystemState state)
        {
            _visibleLookup = state.GetComponentLookup<ActorRenderVisible>(isReadOnly: true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _visibleLookup.Update(ref state);
            state.Dependency = new SyncVisibilityJob
            {
                VisibleLookup = _visibleLookup,
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        partial struct SyncVisibilityJob : IJobEntity
        {
            [ReadOnly] public ComponentLookup<ActorRenderVisible> VisibleLookup;

            void Execute(in ActorRenderMeshInstance instance, EnabledRefRW<MaterialMeshInfo> materialMesh)
            {
                bool visible = instance.Actor != Entity.Null
                    && VisibleLookup.HasComponent(instance.Actor)
                    && VisibleLookup.IsComponentEnabled(instance.Actor);
                materialMesh.ValueRW = visible;
            }
        }
    }
}
