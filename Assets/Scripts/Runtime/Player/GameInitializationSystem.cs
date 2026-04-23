using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using Collider = Unity.Physics.Collider;
using CapsuleCollider = Unity.Physics.CapsuleCollider;

using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.WorldState;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

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

        public bool HasSerializedSavePayload;
        public FixedString128Bytes SerializedSavePayloadStatus;
    }

    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    [UpdateAfter(typeof(InteractionRuntimeBootstrapSystem))]
    [UpdateAfter(typeof(RuntimeSpawnBootstrapSystem))]
    [UpdateAfter(typeof(ContainerLootBootstrapSystem))]
    [UpdateAfter(typeof(RuntimeShellBootstrapSystem))]
    public partial class GameInitializationSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<GameInitializationSingleton>();
            RequireForUpdate<WorldJournalState>();
            RequireForUpdate<RuntimeSpawnState>();
            RequireForUpdate<RuntimeSpawnedRef>();
            RequireForUpdate<ContainerSessionHeader>();
            RequireForUpdate<ContainerSessionItem>();
            RequireForUpdate<PlayerInventoryItem>();
            RequireForUpdate<PickedItemRecord>();
            RequireForUpdate<InteriorTransitionState>();
            RequireForUpdate<InteriorSpawnedEntity>();
            RequireForUpdate<StreamingConfig>();
            RequireForUpdate<LogicalRefLookup>();
            RequireForUpdate<LoadedCellsMap>();
            RequireForUpdate<AvailableCells>();
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

            if (hasContinueRequest)
            {
                if (init.HasSerializedSavePayload)
                {
                    if (!WorldSaveReplayUtility.TryRestoreContinueSave(World, em, ref init, out string loadError))
                        Debug.LogWarning($"[VVardenfell][Save] continue load failed; starting from default bootstrap state instead. {loadError}");
                }
                else
                {
                    Debug.LogWarning("[VVardenfell][Save] continue requested, but no serialized save payload was available.");
                }
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
            em.AddComponentData(player, new MorrowindMovementIntent());
            em.AddComponentData(player, new MorrowindActorKinematicState
            {
                Grounded = true,
            });
            em.AddComponentData(player, MorrowindMovementTuning.OpenMwDefaults());
            em.AddComponentData(player, new MorrowindMovementFrameTrace());
            em.AddComponentData(player, new PlayerStanceColliders
            {
                Standing = standingBlob,
                Crouching = crouchingBlob,
            });
            em.AddComponentData(player, new PhysicsCollider { Value = standingBlob });
            em.AddSharedComponent(player, new PhysicsWorldIndex { Value = 0 });

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
            em.AddComponentData(viewEntity, new PlayerPhysicsViewPose
            {
                Position = init.PlayerPosition + math.rotate(init.PlayerRotation, initialEyeOffset),
                Rotation = init.PlayerRotation,
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

            Entity streamingEntity = SystemAPI.GetSingletonEntity<StreamingConfig>();
            var streamingConfig = em.GetComponentData<StreamingConfig>(streamingEntity);
            streamingConfig.CameraCell = WorldBootstrap.WorldPositionToCell(init.PlayerPosition);
            if (SystemAPI.HasSingleton<InteriorTransitionState>())
            {
                var transition = SystemAPI.GetSingleton<InteriorTransitionState>();
                streamingConfig.ExteriorStreamingPaused = transition.InteriorActive != 0;
            }
            else
            {
                streamingConfig.ExteriorStreamingPaused = false;
            }
            em.SetComponentData(streamingEntity, streamingConfig);

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
                InteractionCollisionLayers.PlayerBodyFilter);
        }
    }
}
