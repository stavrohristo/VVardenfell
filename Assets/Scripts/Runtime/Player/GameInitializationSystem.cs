using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using Collider = Unity.Physics.Collider;
using CapsuleCollider = Unity.Physics.CapsuleCollider;

using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Rendering;
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

    public struct LoadGameInitializationSingleton : IComponentData
    {
        public FixedString128Bytes SlotId;
    }

    public struct GameInitializationSingleton : IComponentData
    {
        public PlayerCharacterComponent PlayerSettings;
        public ActorRuntimeStatSeed PlayerActorStats;
        public ActorIdentitySet PlayerIdentity;
        public float3 PlayerPosition;
        public quaternion PlayerRotation;
        public float PlayerPitchDegrees;
        public byte RuntimeMode;
        public byte SpawnLocalPlayer;

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
            RequireForUpdate<MainCameraSingleton>();
        }

        protected override void OnUpdate()
        {
            bool hasNewGameRequest = SystemAPI.HasSingleton<NewGameInitializationSingleton>();
            bool hasContinueRequest = SystemAPI.HasSingleton<ContinueGameInitializationSingleton>();
            bool hasLoadRequest = SystemAPI.HasSingleton<LoadGameInitializationSingleton>();
            if (!hasNewGameRequest && !hasContinueRequest && !hasLoadRequest)
                return;

            var initEntity = SystemAPI.GetSingletonEntity<GameInitializationSingleton>();
            var init = SystemAPI.GetComponent<GameInitializationSingleton>(initEntity);
            var em = EntityManager;
            WorldSaveReplayUtility.ResetRuntimeForInitialization(World, em, preserveShell: true);
            if (init.SpawnLocalPlayer == 0)
            {
                ConfigureStreamingAfterInitialization(em, init);
                ClearInitializationRequests(hasNewGameRequest, hasContinueRequest, hasLoadRequest, initEntity);
                return;
            }

            if (hasNewGameRequest)
                SeedInitialPlayerInventory(em, initEntity);

            if (hasLoadRequest)
            {
                var loadRequest = SystemAPI.GetSingleton<LoadGameInitializationSingleton>();
                string slotId = loadRequest.SlotId.ToString();
                string loadError = null;
                if (!string.IsNullOrWhiteSpace(slotId) && WorldSaveStorage.TryLoadSlot(slotId, out var payload, out loadError))
                {
                    init.PlayerPosition = payload.PlayerPosition;
                    init.PlayerRotation = payload.PlayerRotation;
                    init.PlayerPitchDegrees = payload.PlayerPitchDegrees;
                    init.PlayerActorStats = payload.ActorStats;
                    init.PlayerIdentity = payload.PlayerIdentity.Level > 0 ? payload.PlayerIdentity : ActorIdentitySet.DefaultPlayer();
                    PopulateInitializationSpellbook(em, initEntity, payload.KnownSpells);
                    PopulateInitializationActiveEffects(em, initEntity, payload.ActiveMagicEffects);
                    WorldSaveReplayUtility.ApplyMapDiscoveryPayload(em, payload);
                    if (!RuntimeSpawnProjectionUtility.TryRestoreWorldLocation(World, em, payload, out string locationError))
                        Debug.LogWarning($"[VVardenfell][Save] load slot location restore failed; starting from default bootstrap state instead. {locationError}");
                    else
                        RuntimeSpawnProjectionUtility.TryRestoreAliveRefsForCurrentWorld(em, RuntimeContentDatabase.Active);
                }
                else
                {
                    Debug.LogWarning($"[VVardenfell][Save] load slot requested, but payload was unavailable. {loadError}");
                }
            }
            else if (hasContinueRequest)
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
            var attributes = init.PlayerActorStats.Attributes;
            var skills = init.PlayerActorStats.Skills;
            var vitals = init.PlayerActorStats.Vitals;
            var effectModifiers = init.PlayerActorStats.EffectModifiers;
            MorrowindActorMovementStats.ApplyVitalBases(RuntimeContentDatabase.Active, attributes, ref vitals, initializeMissingCurrents: true);
            var derivedStats = MorrowindActorMovementStats.BuildDerived(RuntimeContentDatabase.Active, attributes, skills, vitals, effectModifiers, 0f);
            var movementSpeed = MorrowindActorMovementStats.BuildPlayerMovementSpeed(
                RuntimeContentDatabase.Active,
                attributes,
                skills,
                vitals,
                effectModifiers,
                derivedStats);
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
            em.AddComponentData(player, new MorrowindMovementInput());
            em.AddComponentData(player, new MorrowindMovementState
            {
                Grounded = hasNewGameRequest,
                GroundNormal = math.up(),
            });
            em.AddComponentData(player, movementSpeed);
            em.AddComponentData(player, attributes);
            em.AddComponentData(player, skills);
            em.AddComponentData(player, vitals);
            em.AddComponentData(player, effectModifiers);
            em.AddComponentData(player, derivedStats);
            em.AddComponentData(player, init.PlayerIdentity.Level > 0 ? init.PlayerIdentity : ActorIdentitySet.DefaultPlayer());
            var playerSpells = em.AddBuffer<PlayerKnownSpell>(player);
            if (em.HasBuffer<PlayerKnownSpell>(initEntity))
            {
                var initSpells = em.GetBuffer<PlayerKnownSpell>(initEntity);
                for (int i = 0; i < initSpells.Length; i++)
                {
                    if (initSpells[i].Spell.IsValid)
                        playerSpells.Add(initSpells[i]);
                }
            }
            var activeEffects = em.AddBuffer<ActorActiveMagicEffect>(player);
            if (em.HasBuffer<ActorActiveMagicEffect>(initEntity))
            {
                var initEffects = em.GetBuffer<ActorActiveMagicEffect>(initEntity);
                for (int i = 0; i < initEffects.Length; i++)
                {
                    if (initEffects[i].Applied != 0)
                        activeEffects.Add(initEffects[i]);
                }
            }
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

            Camera cam = SystemAPI.GetSingleton<MainCameraSingleton>().GetRequiredCamera();
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, 4000f);

            ConfigureStreamingAfterInitialization(em, init);

            ClearInitializationRequests(hasNewGameRequest, hasContinueRequest, hasLoadRequest, initEntity);
        }

        void ConfigureStreamingAfterInitialization(EntityManager em, in GameInitializationSingleton init)
        {
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
        }

        void ClearInitializationRequests(
            bool hasNewGameRequest,
            bool hasContinueRequest,
            bool hasLoadRequest,
            Entity initEntity)
        {
            if (hasNewGameRequest)
                EntityManager.DestroyEntity(SystemAPI.GetSingletonEntity<NewGameInitializationSingleton>());
            if (hasContinueRequest)
                EntityManager.DestroyEntity(SystemAPI.GetSingletonEntity<ContinueGameInitializationSingleton>());
            if (hasLoadRequest)
                EntityManager.DestroyEntity(SystemAPI.GetSingletonEntity<LoadGameInitializationSingleton>());
            EntityManager.DestroyEntity(initEntity);
        }

        static void SeedInitialPlayerInventory(EntityManager em, Entity initEntity)
        {
            if (!em.HasBuffer<PlayerInitialInventoryItem>(initEntity))
                return;

            Entity inventoryEntity = WorldStateEntityQueryUtility.GetSingletonBufferOwner<PlayerInventoryItem>(em);
            if (inventoryEntity == Entity.Null || !em.HasBuffer<PlayerInventoryItem>(inventoryEntity))
                return;

            var initialInventory = em.GetBuffer<PlayerInitialInventoryItem>(initEntity);
            if (initialInventory.Length == 0)
                return;

            var playerInventory = em.GetBuffer<PlayerInventoryItem>(inventoryEntity);
            for (int i = 0; i < initialInventory.Length; i++)
            {
                var item = initialInventory[i];
                if (item.Count <= 0 || !item.Content.IsValid)
                    continue;

                playerInventory.Add(new PlayerInventoryItem
                {
                    Content = item.Content,
                    Count = item.Count,
                });
            }
        }

        static void PopulateInitializationSpellbook(EntityManager em, Entity initEntity, PlayerKnownSpell[] knownSpells)
        {
            var buffer = em.HasBuffer<PlayerKnownSpell>(initEntity)
                ? em.GetBuffer<PlayerKnownSpell>(initEntity)
                : em.AddBuffer<PlayerKnownSpell>(initEntity);

            buffer.Clear();
            if (knownSpells == null)
                return;

            for (int i = 0; i < knownSpells.Length; i++)
            {
                if (knownSpells[i].Spell.IsValid)
                    buffer.Add(knownSpells[i]);
            }
        }

        static void PopulateInitializationActiveEffects(EntityManager em, Entity initEntity, ActorActiveMagicEffect[] activeEffects)
        {
            var buffer = em.HasBuffer<ActorActiveMagicEffect>(initEntity)
                ? em.GetBuffer<ActorActiveMagicEffect>(initEntity)
                : em.AddBuffer<ActorActiveMagicEffect>(initEntity);

            buffer.Clear();
            if (activeEffects == null)
                return;

            for (int i = 0; i < activeEffects.Length; i++)
            {
                if (activeEffects[i].Applied != 0)
                    buffer.Add(activeEffects[i]);
            }
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
