using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using Collider = Unity.Physics.Collider;
using CapsuleCollider = Unity.Physics.CapsuleCollider;

using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Combat;
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
        public PlayerCrimeState PlayerCrime;
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
            MorrowindRuntimeLifecycleUtility.RemoveRuntimeLifecycle(em);
            WorldSaveReplayUtility.ResetRuntimeForInitialization(World, em, preserveShell: true);
            if (init.SpawnLocalPlayer == 0)
            {
                ConfigureStreamingAfterInitialization(em, init);
                MorrowindRuntimeLifecycleUtility.EnsureActive(em);
                ClearInitializationRequests(hasNewGameRequest, hasContinueRequest, hasLoadRequest, initEntity);
                return;
            }

            if (hasLoadRequest)
            {
                var loadRequest = SystemAPI.GetSingleton<LoadGameInitializationSingleton>();
                string slotId = loadRequest.SlotId.ToString();
                if (string.IsNullOrWhiteSpace(slotId))
                    throw new System.InvalidOperationException("[VVardenfell][Save] load slot requested without a slot id.");

                if (!WorldSaveStorage.TryLoadSlot(slotId, out var payload, out string loadError))
                    throw new System.InvalidOperationException($"[VVardenfell][Save] load slot requested, but payload was unavailable. {loadError}");

                init.PlayerPosition = payload.PlayerPosition;
                init.PlayerRotation = payload.PlayerRotation;
                init.PlayerPitchDegrees = payload.PlayerPitchDegrees;
                init.PlayerActorStats = payload.ActorStats;
                init.PlayerIdentity = payload.PlayerIdentity.Level > 0 ? payload.PlayerIdentity : ActorIdentitySet.DefaultPlayer();
                init.PlayerCrime = payload.PlayerCrime;
                if (payload.PlayerFactions != null)
                    PopulateInitializationFactions(em, initEntity, payload.PlayerFactions);
                PopulateInitializationInventory(em, initEntity, payload.Inventory);
                PopulateInitializationEquipment(em, initEntity, payload.PlayerEquipment);
                PopulateInitializationSpellbook(em, initEntity, payload.KnownSpells);
                PopulateInitializationActiveSpells(em, initEntity, payload.ActiveSpells);
                PopulateInitializationActiveEffects(em, initEntity, payload.ActiveMagicEffects);
                PopulateInitializationUsedPowers(em, initEntity, payload.UsedPowers);
                WorldSaveReplayUtility.ApplyMapDiscoveryPayload(em, payload);
                if (!RuntimeSpawnProjectionUtility.TryRestoreWorldLocation(World, em, payload, out string locationError))
                    throw new System.InvalidOperationException($"[VVardenfell][Save] load slot location restore failed. {locationError}");

                RuntimeSpawnProjectionUtility.TryRestoreAliveRefsForCurrentWorld(em, RuntimeContentDatabase.Active);
            }
            else if (hasContinueRequest)
            {
                if (!init.HasSerializedSavePayload)
                    throw new System.InvalidOperationException("[VVardenfell][Save] continue requested, but no serialized save payload was available.");

                if (!WorldSaveReplayUtility.TryRestoreContinueSave(World, em, ref init, out string loadError))
                    throw new System.InvalidOperationException($"[VVardenfell][Save] continue load failed. {loadError}");
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
            em.AddComponentData(player, new ActorMagicCastState());
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
            em.AddComponentData(player, new ActorScriptEventState());
            em.AddComponentData(player, new ActorHitAftermathState());
            em.AddComponentData(player, ActorCrimeState.Default);
            em.AddComponentData(player, new ActorFriendlyHitState());
            em.AddComponentData(player, new ActorBlockState());
            em.AddComponentData(player, init.PlayerIdentity.Level > 0 ? init.PlayerIdentity : ActorIdentitySet.DefaultPlayer());
            em.AddComponentData(player, init.PlayerCrime);
            PopulatePlayerFactions(em, initEntity, player);
            var playerSpells = em.AddBuffer<ActorKnownSpell>(player);
            if (em.HasBuffer<ActorKnownSpell>(initEntity))
            {
                var initSpells = em.GetBuffer<ActorKnownSpell>(initEntity);
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
            em.AddComponent<ActorActiveMagicEffectDirty>(player);
            var activeSpells = em.AddBuffer<ActorActiveSpell>(player);
            if (em.HasBuffer<ActorActiveSpell>(initEntity))
            {
                var initActiveSpells = em.GetBuffer<ActorActiveSpell>(initEntity);
                for (int i = 0; i < initActiveSpells.Length; i++)
                    activeSpells.Add(initActiveSpells[i]);
            }
            var usedPowers = em.AddBuffer<ActorUsedPower>(player);
            if (em.HasBuffer<ActorUsedPower>(initEntity))
            {
                var initUsedPowers = em.GetBuffer<ActorUsedPower>(initEntity);
                for (int i = 0; i < initUsedPowers.Length; i++)
                    usedPowers.Add(initUsedPowers[i]);
            }
            PlayerEncumbranceDirtyUtility.EnsureMarker(em, player, enabled: true);
            em.AddComponent<LocalPlayerViewModeDirty>(player);
            var playerInventory = em.AddBuffer<PlayerInventoryItem>(player);
            PopulatePlayerInventory(em, initEntity, playerInventory);
            PopulatePlayerEquipment(em, initEntity, player, playerInventory);
            em.AddComponentData(player, new PlayerStanceColliders
            {
                Standing = standingBlob,
                Crouching = crouchingBlob,
            });
            em.AddComponentData(player, new PhysicsCollider { Value = standingBlob });
            em.AddSharedComponent(player, new PhysicsWorldIndex { Value = 0 });
            em.AddComponentData(player, new PhysicsVelocity());
            em.AddComponentData(player, PhysicsTemporalCoherenceInfo.Default);
            em.AddComponent<PhysicsTemporalCoherenceTag>(player);

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

            MorrowindRuntimeLifecycleUtility.EnsureActive(em);
            ClearInitializationRequests(hasNewGameRequest, hasContinueRequest, hasLoadRequest, initEntity);
        }

        static void PopulatePlayerFactions(EntityManager em, Entity initEntity, Entity player)
        {
            var factions = em.AddBuffer<PlayerFactionMembership>(player);
            if (em.HasBuffer<PlayerFactionMembership>(initEntity))
            {
                var initFactions = em.GetBuffer<PlayerFactionMembership>(initEntity);
                for (int i = 0; i < initFactions.Length; i++)
                {
                    if (initFactions[i].FactionIndex >= 0)
                        factions.Add(initFactions[i]);
                }

                return;
            }

            var contentDb = RuntimeContentDatabase.Active;
            if (contentDb == null
                || !contentDb.TryGetActorHandle("player", out var actorHandle)
                || !actorHandle.IsValid)
            {
                return;
            }

            ref readonly var actor = ref contentDb.Get(actorHandle);
            if (string.IsNullOrWhiteSpace(actor.FactionId)
                || !contentDb.TryGetFactionHandle(actor.FactionId, out var factionHandle)
                || !factionHandle.IsValid)
            {
                return;
            }

            factions.Add(new PlayerFactionMembership
            {
                FactionIndex = factionHandle.Index,
                Rank = actor.Rank,
                Reputation = actor.Reputation,
                Joined = 1,
            });
        }

        static void PopulateInitializationEquipment(EntityManager em, Entity initEntity, ActorEquipmentSlot[] equipment)
        {
            var buffer = em.HasBuffer<ActorEquipmentSlot>(initEntity)
                ? em.GetBuffer<ActorEquipmentSlot>(initEntity)
                : em.AddBuffer<ActorEquipmentSlot>(initEntity);

            buffer.Clear();
            if (equipment == null)
                return;

            for (int i = 0; i < equipment.Length; i++)
            {
                if (equipment[i].Content.IsValid)
                    buffer.Add(equipment[i]);
            }
        }

        static void PopulateInitializationInventory(EntityManager em, Entity initEntity, PlayerInventoryItem[] inventory)
        {
            if (inventory == null)
                throw new System.InvalidOperationException("[VVardenfell][Save] load payload is missing player inventory data.");

            var buffer = em.HasBuffer<PlayerInventoryItem>(initEntity)
                ? em.GetBuffer<PlayerInventoryItem>(initEntity)
                : em.AddBuffer<PlayerInventoryItem>(initEntity);

            buffer.Clear();
            for (int i = 0; i < inventory.Length; i++)
            {
                if (inventory[i].Count > 0 && inventory[i].Content.IsValid)
                    buffer.Add(inventory[i]);
            }
        }

        static void PopulatePlayerEquipment(EntityManager em, Entity initEntity, Entity player, DynamicBuffer<PlayerInventoryItem> inventory)
        {
            var equipment = em.AddBuffer<ActorEquipmentSlot>(player);
            if (em.HasBuffer<ActorEquipmentSlot>(initEntity))
            {
                var initEquipment = em.GetBuffer<ActorEquipmentSlot>(initEntity);
                for (int i = 0; i < initEquipment.Length; i++)
                {
                    if (initEquipment[i].Content.IsValid)
                        equipment.Add(initEquipment[i]);
                }

                return;
            }

            if (!inventory.IsCreated || inventory.Length == 0)
                return;
            if (RuntimeContentDatabase.Active == null || !RuntimeContentDatabase.Active.TryGetActorHandle("player", out var actorHandle))
                return;

            ref readonly var actor = ref RuntimeContentDatabase.Active.Get(actorHandle);
            using var actorInventory = new NativeList<ActorInventoryItem>(Allocator.Temp);
            for (int i = 0; i < inventory.Length; i++)
            {
                var item = inventory[i];
                if (item.Count <= 0 || !item.Content.IsValid)
                    continue;

                actorInventory.Add(new ActorInventoryItem
                {
                    Content = item.Content,
                    SoulId = item.SoulId,
                    SoulActorHandleValue = item.SoulActorHandleValue,
                    Count = item.Count,
                    Condition = item.Condition,
                    AuthoredOrder = i,
                });
            }

            using var selectedEquipment = new NativeList<ActorEquipmentSlot>(Allocator.Temp);
            MorrowindEquipmentAutoEquipUtility.SelectInitialEquipment(RuntimeContentDatabase.Active, actor, actorInventory.AsArray(), selectedEquipment);
            for (int i = 0; i < selectedEquipment.Length; i++)
                equipment.Add(selectedEquipment[i]);
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

        static void PopulatePlayerInventory(EntityManager em, Entity initEntity, DynamicBuffer<PlayerInventoryItem> playerInventory)
        {
            playerInventory.Clear();
            if (em.HasBuffer<PlayerInventoryItem>(initEntity))
            {
                var savedInventory = em.GetBuffer<PlayerInventoryItem>(initEntity);
                for (int i = 0; i < savedInventory.Length; i++)
                {
                    if (savedInventory[i].Count > 0 && savedInventory[i].Content.IsValid)
                        playerInventory.Add(savedInventory[i]);
                }

                return;
            }

            if (!em.HasBuffer<PlayerInitialInventoryItem>(initEntity))
                return;

            var initialInventory = em.GetBuffer<PlayerInitialInventoryItem>(initEntity);
            if (initialInventory.Length == 0)
                return;

            for (int i = 0; i < initialInventory.Length; i++)
            {
                var item = initialInventory[i];
                if (item.Count <= 0 || !item.Content.IsValid)
                    continue;

                playerInventory.Add(new PlayerInventoryItem
                {
                    Content = item.Content,
                    Count = item.Count,
                    Condition = InventoryConditionUtility.ResolveInitialCondition(RuntimeContentDatabase.Active, item.Content),
                });
            }
        }

        static void PopulateInitializationSpellbook(EntityManager em, Entity initEntity, ActorKnownSpell[] knownSpells)
        {
            var buffer = em.HasBuffer<ActorKnownSpell>(initEntity)
                ? em.GetBuffer<ActorKnownSpell>(initEntity)
                : em.AddBuffer<ActorKnownSpell>(initEntity);

            buffer.Clear();
            if (knownSpells == null)
                return;

            for (int i = 0; i < knownSpells.Length; i++)
            {
                if (knownSpells[i].Spell.IsValid)
                    buffer.Add(knownSpells[i]);
            }
        }

        static void PopulateInitializationFactions(EntityManager em, Entity initEntity, PlayerFactionMembership[] factions)
        {
            var buffer = em.HasBuffer<PlayerFactionMembership>(initEntity)
                ? em.GetBuffer<PlayerFactionMembership>(initEntity)
                : em.AddBuffer<PlayerFactionMembership>(initEntity);

            buffer.Clear();
            if (factions == null)
                return;

            for (int i = 0; i < factions.Length; i++)
            {
                if (factions[i].FactionIndex >= 0)
                    buffer.Add(factions[i]);
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

        static void PopulateInitializationActiveSpells(EntityManager em, Entity initEntity, ActorActiveSpell[] activeSpells)
        {
            var buffer = em.HasBuffer<ActorActiveSpell>(initEntity)
                ? em.GetBuffer<ActorActiveSpell>(initEntity)
                : em.AddBuffer<ActorActiveSpell>(initEntity);

            buffer.Clear();
            if (activeSpells == null)
                return;

            for (int i = 0; i < activeSpells.Length; i++)
            {
                if (activeSpells[i].ActiveSpellId != 0)
                    buffer.Add(activeSpells[i]);
            }
        }

        static void PopulateInitializationUsedPowers(EntityManager em, Entity initEntity, ActorUsedPower[] usedPowers)
        {
            var buffer = em.HasBuffer<ActorUsedPower>(initEntity)
                ? em.GetBuffer<ActorUsedPower>(initEntity)
                : em.AddBuffer<ActorUsedPower>(initEntity);

            buffer.Clear();
            if (usedPowers == null)
                return;

            for (int i = 0; i < usedPowers.Length; i++)
            {
                if (usedPowers[i].Spell.IsValid)
                    buffer.Add(usedPowers[i]);
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
