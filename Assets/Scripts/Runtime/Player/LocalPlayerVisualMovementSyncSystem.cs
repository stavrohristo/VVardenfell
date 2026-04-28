#if !VVARDENFELL_OLD_ACTOR_ANIMATION
using Unity.Entities;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Player
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateBefore(typeof(ActorAnimationControllerSystem))]
    public partial class LocalPlayerVisualMovementSyncSystem : SystemBase
    {
        EntityQuery _playerQuery;

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<MorrowindMovementState>());

            RequireForUpdate(_playerQuery);
            RequireForUpdate<LocalPlayerVisual>();
        }

        protected override void OnUpdate()
        {
            Entity player = _playerQuery.GetSingletonEntity();
            var playerMovementState = _playerQuery.GetSingleton<MorrowindMovementState>();

            foreach (var (visual, movementState) in
                     SystemAPI.Query<RefRO<LocalPlayerVisual>, RefRW<MorrowindMovementState>>())
            {
                if (visual.ValueRO.Player == player)
                    movementState.ValueRW = playerMovementState;
            }
        }
    }
}
#endif
