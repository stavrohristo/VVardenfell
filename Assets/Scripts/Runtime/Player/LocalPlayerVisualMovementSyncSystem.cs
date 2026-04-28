#if !VVARDENFELL_OLD_ACTOR_ANIMATION
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
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

    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateBefore(typeof(ActorAnimationControllerSystem))]
    public partial class LocalPlayerVisualTransformSyncSystem : SystemBase
    {
        EntityQuery _playerQuery;
        EntityQuery _viewQuery;

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            _viewQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerViewComponent>());

            RequireForUpdate(_playerQuery);
            RequireForUpdate(_viewQuery);
            RequireForUpdate<LocalPlayerVisual>();
        }

        protected override void OnUpdate()
        {
            Entity player = _playerQuery.GetSingletonEntity();
            LocalTransform playerTransform = _playerQuery.GetSingleton<LocalTransform>();
            PlayerViewComponent view = _viewQuery.GetSingleton<PlayerViewComponent>();
            if (view.ControlledCharacter != player)
                return;

            float3 viewPosition = playerTransform.Position + math.rotate(playerTransform.Rotation, view.LocalEyeOffset);
            quaternion viewRotation = math.normalize(math.mul(playerTransform.Rotation, view.LocalViewRotation));

            foreach (var (visual, transform, localToWorld) in
                     SystemAPI.Query<RefRO<LocalPlayerVisual>, RefRW<LocalTransform>, RefRW<LocalToWorld>>())
            {
                if (visual.ValueRO.Player != player)
                    continue;

                bool firstPerson = visual.ValueRO.FirstPerson != 0;
                float3 position = firstPerson ? viewPosition : playerTransform.Position;
                quaternion rotation = firstPerson ? viewRotation : playerTransform.Rotation;
                transform.ValueRW = LocalTransform.FromPositionRotationScale(position, rotation, playerTransform.Scale);
                localToWorld.ValueRW = new LocalToWorld
                {
                    Value = float4x4.TRS(position, rotation, new float3(playerTransform.Scale)),
                };
            }
        }
    }
}
#endif
