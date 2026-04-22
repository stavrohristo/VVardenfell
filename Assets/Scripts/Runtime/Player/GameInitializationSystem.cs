using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using VVardenfell.Runtime.Streaming;
using Collider = Unity.Physics.Collider;
using CapsuleCollider = Unity.Physics.CapsuleCollider;

namespace VVardenfell.Runtime.Player
{
    public struct NewGameInitializationSingleton : IComponentData
    {
    }

    public struct ContinueGameInitializationSingleton : IComponentData
    {
    }

    public struct GameInitializationSingleton : IComponentData
    {
        public PlayerCharacterComponent PlayerSettings;
        public float3 PlayerPosition;
        public quaternion PlayerRotation;
        public float PlayerPitchDegrees;

        // Placeholder for future save restore support. Once we serialize runtime state,
        // this singleton becomes the handoff point for mutating ECS world state first.
        public bool HasSerializedSavePayload;
    }

    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class GameInitializationSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<GameInitializationSingleton>();
        }

        protected override void OnUpdate()
        {
            bool hasNewGameRequest = SystemAPI.HasSingleton<NewGameInitializationSingleton>();
            bool hasContinueRequest = SystemAPI.HasSingleton<ContinueGameInitializationSingleton>();
            if (!hasNewGameRequest && !hasContinueRequest)
                return;

            var initEntity = SystemAPI.GetSingletonEntity<GameInitializationSingleton>();
            var init = SystemAPI.GetComponent<GameInitializationSingleton>(initEntity);
            var em = EntityManager;

            if (hasContinueRequest || init.HasSerializedSavePayload)
            {
                // Future hook: deserialize save payload and mutate ECS world state here
                // before creating the player from the restored data.
            }

            var standingBlob = CreatePlayerCapsule(init.PlayerSettings.Radius, init.PlayerSettings.StandingHeight);
            var crouchingBlob = CreatePlayerCapsule(init.PlayerSettings.Radius, init.PlayerSettings.CrouchingHeight);

            var player = em.CreateEntity();
            em.SetName(player, "VVardenfell.Player");
            em.AddComponentData(player, new PlayerTag());
            em.AddComponentData(player, LocalTransform.FromPositionRotationScale(init.PlayerPosition, init.PlayerRotation, 1f));
            em.AddComponentData(player, new LocalToWorld
            {
                Value = float4x4.TRS(init.PlayerPosition, init.PlayerRotation, new float3(1f))
            });
            em.AddComponentData(player, init.PlayerSettings);
            em.AddComponentData(player, new PlayerCharacterControl());
            em.AddComponentData(player, new PlayerCharacterState());
            em.AddComponentData(player, new PlayerStanceColliders
            {
                Standing = standingBlob,
                Crouching = crouchingBlob,
            });
            em.AddComponentData(player, new PhysicsCollider { Value = standingBlob });

            var viewEntity = em.CreateEntity();
            em.SetName(viewEntity, "VVardenfell.PlayerView");
            var initialEyeOffset = new float3(0f, init.PlayerSettings.StandingEyeHeight, 0f);
            em.AddComponentData(viewEntity, new Parent { Value = player });
            em.AddComponentData(viewEntity, LocalTransform.FromPositionRotationScale(initialEyeOffset, quaternion.identity, 1f));
            em.AddComponentData(viewEntity, new LocalToWorld
            {
                Value = float4x4.TRS(init.PlayerPosition + math.rotate(init.PlayerRotation, initialEyeOffset), init.PlayerRotation, new float3(1f))
            });
            em.AddComponentData(viewEntity, new PlayerViewComponent
            {
                ControlledCharacter = player,
                LocalPitchDegrees = init.PlayerPitchDegrees,
                LocalViewRotation = quaternion.identity,
                LocalEyeOffset = initialEyeOffset,
            });

            var cam = Camera.main;
            if (cam != null)
            {
                cam.farClipPlane = Mathf.Max(cam.farClipPlane, 4000f);
            }
            else
            {
                Debug.LogWarning("[VVardenfell] Camera.main missing - player camera sync will have no target.");
            }

            if (hasNewGameRequest)
                em.DestroyEntity(SystemAPI.GetSingletonEntity<NewGameInitializationSingleton>());
            if (hasContinueRequest)
                em.DestroyEntity(SystemAPI.GetSingletonEntity<ContinueGameInitializationSingleton>());
            em.DestroyEntity(initEntity);
        }

        private static BlobAssetReference<Collider> CreatePlayerCapsule(float radius, float height)
        {
            return CapsuleCollider.Create(
                new CapsuleGeometry
                {
                    Vertex0 = new float3(0f, radius, 0f),
                    Vertex1 = new float3(0f, height - radius, 0f),
                    Radius = radius,
                },
                new CollisionFilter { BelongsTo = 1u << 1, CollidesWith = ~0u, GroupIndex = 0 });
        }
    }
}
