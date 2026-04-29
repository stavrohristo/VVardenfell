using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Rendering
{
    [UpdateInGroup(typeof(MorrowindPresentationSystemGroup))]
    public partial struct ActorEntitiesGraphicsVisibilitySyncSystem : ISystem
    {
        ComponentLookup<ActorRenderVisible> _visibleLookup;
        ComponentLookup<LocalPlayerVisual> _localPlayerVisualLookup;

        public void OnCreate(ref SystemState state)
        {
            _visibleLookup = state.GetComponentLookup<ActorRenderVisible>(isReadOnly: true);
            _localPlayerVisualLookup = state.GetComponentLookup<LocalPlayerVisual>(isReadOnly: true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _visibleLookup.Update(ref state);
            _localPlayerVisualLookup.Update(ref state);
            bool hasLocalPlayerPresentation = SystemAPI.TryGetSingleton<LocalPlayerPresentationState>(out var localPlayerPresentation);
            state.Dependency = new SyncVisibilityJob
            {
                VisibleLookup = _visibleLookup,
                LocalPlayerVisualLookup = _localPlayerVisualLookup,
                HasLocalPlayerPresentation = hasLocalPlayerPresentation,
                LocalPlayerPresentation = localPlayerPresentation,
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
        partial struct SyncVisibilityJob : IJobEntity
        {
            [ReadOnly] public ComponentLookup<ActorRenderVisible> VisibleLookup;
            [ReadOnly] public ComponentLookup<LocalPlayerVisual> LocalPlayerVisualLookup;
            public bool HasLocalPlayerPresentation;
            public LocalPlayerPresentationState LocalPlayerPresentation;

            void Execute(in ActorRenderMeshInstance instance, EnabledRefRW<MaterialMeshInfo> materialMesh)
            {
                bool visible = instance.Actor != Entity.Null
                    && VisibleLookup.HasComponent(instance.Actor)
                    && VisibleLookup.IsComponentEnabled(instance.Actor);
                if (visible
                    && HasLocalPlayerPresentation
                    && LocalPlayerVisualLookup.HasComponent(instance.Actor))
                {
                    var visual = LocalPlayerVisualLookup[instance.Actor];
                    var expectedMode = visual.FirstPerson != 0
                        ? PlayerViewMode.FirstPerson
                        : PlayerViewMode.ThirdPerson;
                    visible = LocalPlayerPresentation.Mode == expectedMode;
                }
                materialMesh.ValueRW = visible;
            }
        }
    }
}
