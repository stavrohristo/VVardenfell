using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Rendering
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindPresentationSystemGroup))]
    public partial struct ActorEntitiesGraphicsVisibilitySyncSystem : ISystem
    {
        EntityQuery _renderMeshQuery;
        ComponentLookup<ActorRenderVisible> _visibleLookup;
        ComponentLookup<LocalPlayerVisual> _localPlayerVisualLookup;

        public void OnCreate(ref SystemState state)
        {
            _renderMeshQuery = state.GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ActorRenderMeshInstance>(),
                    ComponentType.ReadWrite<MaterialMeshInfo>(),
                },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });
            _visibleLookup = state.GetComponentLookup<ActorRenderVisible>(isReadOnly: true);
            _localPlayerVisualLookup = state.GetComponentLookup<LocalPlayerVisual>(isReadOnly: true);
            state.RequireForUpdate(_renderMeshQuery);
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
                    bool firstAndThirdShareActor = LocalPlayerPresentation.FirstPersonVisual == LocalPlayerPresentation.ThirdPersonVisual;
                    if (!firstAndThirdShareActor)
                    {
                        var expectedMode = visual.FirstPerson != 0
                            ? PlayerViewMode.FirstPerson
                            : PlayerViewMode.ThirdPerson;
                        visible = LocalPlayerPresentation.Mode == expectedMode;
                    }

                    bool firstPersonActive = LocalPlayerPresentation.Mode == PlayerViewMode.FirstPerson
                                             && instance.Actor == LocalPlayerPresentation.FirstPersonVisual;
                    if (instance.VisibilityMode == ActorRenderMeshVisibilityMode.FirstPersonCameraHidden)
                        visible = visible && !firstPersonActive;
                    else if (instance.VisibilityMode == ActorRenderMeshVisibilityMode.FirstPersonShadowOnly)
                        visible = visible && firstPersonActive;
                }
                else if (instance.VisibilityMode == ActorRenderMeshVisibilityMode.FirstPersonShadowOnly)
                {
                    visible = false;
                }
                materialMesh.ValueRW = visible;
            }
        }
    }
}
