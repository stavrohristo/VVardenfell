using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime;
using VVardenfell.Runtime.AI;
using VVardenfell.Runtime.Combat;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Pathfinding;
using VVardenfell.Runtime.Streaming;
using CapsuleCollider = Unity.Physics.CapsuleCollider;
using Collider = Unity.Physics.Collider;

namespace VVardenfell.Runtime.Components
{
    public static class LogicalRefAuthoringUtility
    {
        public static bool QueueAttach(
            EntityManager entityManager,
            ref EntityCommandBuffer ecb,
            Entity logicalEntity,
            RuntimeContentDatabase contentDb,
            ContentReference contentReference,
            float3 worldPosition = default,
            bool isInterior = false,
            int2 exteriorCell = default,
            FixedString128Bytes interiorCellId = default,
            uint placedRefId = 0u,
            bool attachDoorInteractable = false,
            DoorInteractable doorInteractable = default)
        {
            if (contentDb == null || !contentDb.IsValid(contentReference))
                return false;

            switch (contentReference.Kind)
            {
                case ContentReferenceKind.Actor:
                {
                    var handle = new ActorDefHandle { Value = contentReference.HandleValue };
                    ref readonly var actor = ref contentDb.Get(handle);
                    ecb.AddComponent(logicalEntity, new ActorSpawnSource { Definition = handle });
                    ecb.AddComponent(logicalEntity, BuildPassiveActorPresence(actor));
                    QueueActorRuntimeComponents(
                        entityManager,
                        ref ecb,
                        logicalEntity,
                        contentDb,
                        handle,
                        actor,
                        worldPosition,
                        isInterior,
                        exteriorCell,
                        interiorCellId,
                        placedRefId);
                    MorrowindScriptRuntimeAuthoringUtility.TryQueueObjectScript(ref ecb, logicalEntity, contentDb, actor.ScriptId);
                    return true;
                }

                case ContentReferenceKind.Activator:
                {
                    var handle = new ActivatorDefHandle { Value = contentReference.HandleValue };
                    ref readonly var def = ref contentDb.Get(handle);
                    ecb.AddComponent(logicalEntity, new ActivatorAuthoring { Definition = handle });
                    TryQueueScriptedDoorMotion(ref ecb, logicalEntity, contentDb, def.ScriptId);
                    MorrowindScriptRuntimeAuthoringUtility.TryQueueObjectScript(ref ecb, logicalEntity, contentDb, def.ScriptId);
                    TryQueueAudioEmitterAuthoring(ref ecb, logicalEntity, contentDb, def.SoundId, def.AuxSoundId);
                    return true;
                }

                case ContentReferenceKind.Door:
                {
                    var handle = new DoorDefHandle { Value = contentReference.HandleValue };
                    ref readonly var def = ref contentDb.Get(handle);
                    ecb.AddComponent(logicalEntity, new DoorAuthoring { Definition = handle });
                    bool hasScriptedDoorMotion = TryQueueScriptedDoorMotion(ref ecb, logicalEntity, contentDb, def.ScriptId);
                    if (!hasScriptedDoorMotion && attachDoorInteractable && doorInteractable.IsTeleport == 0)
                        QueueDoorMotion(ref ecb, logicalEntity, DoorMotionAuthoringUtility.BuildOpenMwDoorMotion());
                    MorrowindScriptRuntimeAuthoringUtility.TryQueueObjectScript(ref ecb, logicalEntity, contentDb, def.ScriptId);
                    TryQueueAudioEmitterAuthoring(ref ecb, logicalEntity, contentDb, def.SoundId, def.AuxSoundId);
                    if (attachDoorInteractable)
                        ecb.AddComponent(logicalEntity, doorInteractable);
                    return true;
                }

                case ContentReferenceKind.Container:
                {
                    var handle = new ContainerDefHandle { Value = contentReference.HandleValue };
                    ref readonly var def = ref contentDb.Get(handle);
                    ecb.AddComponent(logicalEntity, new ContainerAuthoring { Definition = handle });
                    MorrowindScriptRuntimeAuthoringUtility.TryQueueObjectScript(ref ecb, logicalEntity, contentDb, def.ScriptId);
                    TryQueueAudioEmitterAuthoring(ref ecb, logicalEntity, contentDb, def.SoundId, def.AuxSoundId);
                    return true;
                }

                case ContentReferenceKind.Item:
                {
                    var handle = new ItemDefHandle { Value = contentReference.HandleValue };
                    ref readonly var def = ref contentDb.Get(handle);
                    ecb.AddComponent(logicalEntity, new ItemPickupAuthoring { Definition = handle });
                    MorrowindScriptRuntimeAuthoringUtility.TryQueueObjectScript(ref ecb, logicalEntity, contentDb, def.ScriptId);
                    if (RuntimeContentMetadataResolver.IsBook(def))
                        ecb.AddComponent<BookTag>(logicalEntity);
                    TryQueueAudioEmitterAuthoring(ref ecb, logicalEntity, contentDb, def.SoundId, def.AuxSoundId);
                    return true;
                }

                case ContentReferenceKind.Light:
                {
                    var handle = new LightDefHandle { Value = contentReference.HandleValue };
                    ref readonly var def = ref contentDb.Get(handle);
                    ecb.AddComponent(logicalEntity, new LightSourceAuthoring { Definition = handle });
                    MorrowindScriptRuntimeAuthoringUtility.TryQueueObjectScript(ref ecb, logicalEntity, contentDb, def.ScriptId);
                    var flags = BuildLightInstanceFlags(def.Flags);
                    ecb.AddComponent(logicalEntity, flags);
                    ecb.AddComponent(logicalEntity, BuildLightInstanceState(def));
                    if (IsAnimatedLight(flags))
                        ecb.AddComponent<LightInstanceAnimated>(logicalEntity);
                    ecb.AddComponent(logicalEntity, new LightPresentationLink { Slot = -1 });
                    TryQueueAudioEmitterAuthoring(ref ecb, logicalEntity, contentDb, def.SoundId, null);
                    return true;
                }

                case ContentReferenceKind.Static:
                {
                    var handle = new GenericRecordDefHandle { Value = contentReference.HandleValue };
                    ref readonly var def = ref contentDb.GetStatic(handle);
                    ecb.AddComponent(logicalEntity, new StaticRefAuthoring { Definition = handle });
                    MorrowindScriptRuntimeAuthoringUtility.TryQueueObjectScript(ref ecb, logicalEntity, contentDb, def.ScriptId);
                    return true;
                }

                case ContentReferenceKind.LeveledItem:
                {
                    var handle = new ItemLeveledListDefHandle { Value = contentReference.HandleValue };
                    ecb.AddComponent(logicalEntity, new LeveledItemAuthoring { Definition = handle });
                    return true;
                }

                case ContentReferenceKind.LeveledCreature:
                {
                    var handle = new CreatureLeveledListDefHandle { Value = contentReference.HandleValue };
                    ecb.AddComponent(logicalEntity, new LeveledCreatureAuthoring { Definition = handle });
                    return true;
                }

                default:
                    return false;
            }
        }

        static bool TryQueueScriptedDoorMotion(
            ref EntityCommandBuffer ecb,
            Entity logicalEntity,
            RuntimeContentDatabase contentDb,
            string scriptId)
        {
            if (!DoorMotionAuthoringUtility.TryBuild(contentDb, scriptId, out DoorMotionState motion))
                return false;

            QueueDoorMotion(ref ecb, logicalEntity, motion);
            return true;
        }

        static void QueueDoorMotion(ref EntityCommandBuffer ecb, Entity logicalEntity, DoorMotionState motion)
        {
            ecb.AddComponent(logicalEntity, motion);
            ecb.AddComponent<DoorActivated>(logicalEntity);
            ecb.SetComponentEnabled<DoorActivated>(logicalEntity, false);
        }

        static PassiveActorPresence BuildPassiveActorPresence(in ActorDef actor)
        {
            bool canTalk = actor.Kind == ActorDefKind.Npc;
            return new PassiveActorPresence
            {
                Family = (byte)(actor.Kind == ActorDefKind.Npc ? PassiveActorFamily.Npc : PassiveActorFamily.Creature),
                CanTalk = (byte)(canTalk ? 1 : 0),
            };
        }

        static void QueueActorRuntimeComponents(
            EntityManager entityManager,
            ref EntityCommandBuffer ecb,
            Entity logicalEntity,
            RuntimeContentDatabase contentDb,
            ActorDefHandle actorHandle,
            in ActorDef actor,
            float3 worldPosition,
            bool isInterior,
            int2 exteriorCell,
            FixedString128Bytes interiorCellId,
            uint placedRefId)
        {
            var statSeed = MorrowindActorMovementStats.CreateSeedFromActor(contentDb, actor);
            ecb.AddComponent(logicalEntity, statSeed.Attributes);
            ecb.AddComponent(logicalEntity, statSeed.Skills);
            ecb.AddComponent(logicalEntity, statSeed.Vitals);
            ecb.AddComponent(logicalEntity, statSeed.EffectModifiers);
            ecb.AddComponent(logicalEntity, new ActorDispositionState
            {
                BaseDisposition = actor.Disposition,
            });
            ecb.AddComponent(logicalEntity, new ActorAiSettingsState
            {
                Hello = actor.AiData.Hello,
                Fight = actor.AiData.Fight,
                Flee = actor.AiData.Flee,
                Alarm = actor.AiData.Alarm,
            });
            ecb.AddComponent(logicalEntity, new ActorScriptEventState());
            ecb.AddComponent(logicalEntity, new ActorHitAftermathState());
            ecb.AddComponent(logicalEntity, ActorCrimeState.Default);
            ecb.AddComponent(logicalEntity, new ActorFriendlyHitState());
            ecb.AddComponent(logicalEntity, new ActorBlockState());
            ecb.AddComponent(logicalEntity, new ActorMeleeCombatAiState());
            ecb.AddComponent(logicalEntity, new ActorAiGreetingState());
            var derivedMovement = MorrowindActorMovementStats.BuildDerived(
                contentDb,
                statSeed.Attributes,
                statSeed.Skills,
                statSeed.Vitals,
                statSeed.EffectModifiers,
                inventoryWeight: 0f);
            ecb.AddComponent(logicalEntity, derivedMovement);
            var knownSpells = ecb.AddBuffer<ActorKnownSpell>(logicalEntity);
            var actorSpells = MorrowindActorMovementStats.BuildKnownSpellListFromActor(contentDb, actorHandle);
            for (int i = 0; i < actorSpells.Length; i++)
                knownSpells.Add(actorSpells[i]);
            ecb.AddBuffer<ActorActiveMagicEffect>(logicalEntity);
            ecb.AddComponent<ActorActiveMagicEffectDirty>(logicalEntity);
            QueueActorFactionMembership(ref ecb, logicalEntity, contentDb, actor);

            QueueActorCollider(entityManager, ref ecb, logicalEntity);
            QueueActorPickCollider(
                entityManager,
                ref ecb,
                logicalEntity,
                isInterior,
                exteriorCell);

            if (ActorAiRuntimeAuthoringUtility.HasPackage(contentDb, actorHandle))
            {
                var anchor = BuildActorAiAnchor(contentDb, isInterior, exteriorCell, interiorCellId);
                ecb.AddComponent(logicalEntity, new ActorAiState
                {
                    HomePosition = worldPosition,
                    CurrentNodeIndex = -1,
                    GoalNodeIndex = -1,
                    RandomSeed = BuildActorAiSeed(actorHandle, worldPosition, exteriorCell, isInterior),
                    Status = (byte)ActorAiPlannerStatus.Idle,
                });
                ecb.AddComponent(logicalEntity, anchor);
                ecb.AddComponent<ActorAiNavigationAnchorDirty>(logicalEntity);
                ecb.SetComponentEnabled<ActorAiNavigationAnchorDirty>(logicalEntity, false);
                var packages = ecb.AddBuffer<ActorAiPackageRuntime>(logicalEntity);
                ActorAiRuntimeAuthoringUtility.HydratePackages(contentDb, actorHandle, anchor, packages);
                if (packages.Length > 0)
                {
                    ActorMovementAuthoringUtility.QueueEnsureMovableActor(
                        ref ecb,
                        logicalEntity,
                        MorrowindActorMovementStats.BuildMovementSpeed(
                            contentDb,
                            actor.Kind,
                            statSeed.Attributes,
                            statSeed.Skills,
                            statSeed.Vitals,
                            statSeed.EffectModifiers,
                            derivedMovement));
                }
            }

            QueueActorInventoryAndEquipment(ref ecb, logicalEntity, contentDb, actorHandle, placedRefId);
        }

        static void QueueActorFactionMembership(
            ref EntityCommandBuffer ecb,
            Entity logicalEntity,
            RuntimeContentDatabase contentDb,
            in ActorDef actor)
        {
            if (string.IsNullOrWhiteSpace(actor.FactionId))
                return;

            if (!contentDb.TryGetFactionHandle(actor.FactionId, out var factionHandle) || !factionHandle.IsValid)
                throw new InvalidOperationException($"Actor '{actor.Id}' references unknown faction '{actor.FactionId}'.");

            var factions = ecb.AddBuffer<ActorFactionMembership>(logicalEntity);
            factions.Add(new ActorFactionMembership
            {
                FactionIndex = factionHandle.Index,
                Rank = actor.Rank,
                Joined = 1,
            });
        }

        static void QueueActorInventoryAndEquipment(
            ref EntityCommandBuffer ecb,
            Entity logicalEntity,
            RuntimeContentDatabase contentDb,
            ActorDefHandle actorHandle,
            uint placedRefId)
        {
            var inventory = new NativeList<ActorInventoryItem>(Allocator.Temp);
            var equipment = new NativeList<ActorEquipmentSlot>(Allocator.Temp);
            try
            {
                HydrateActorInventoryAndEquipment(contentDb, actorHandle, placedRefId, ref inventory, ref equipment);

                if (inventory.Length > 0)
                {
                    var inventoryBuffer = ecb.AddBuffer<ActorInventoryItem>(logicalEntity);
                    for (int i = 0; i < inventory.Length; i++)
                        inventoryBuffer.Add(inventory[i]);
                }

                if (equipment.Length > 0)
                {
                    var equipmentBuffer = ecb.AddBuffer<ActorEquipmentSlot>(logicalEntity);
                    for (int i = 0; i < equipment.Length; i++)
                        equipmentBuffer.Add(equipment[i]);
                }
            }
            finally
            {
                if (equipment.IsCreated)
                    equipment.Dispose();
                if (inventory.IsCreated)
                    inventory.Dispose();
            }
        }

        static void HydrateActorInventoryAndEquipment(
            RuntimeContentDatabase contentDb,
            ActorDefHandle actorHandle,
            uint placedRefId,
            ref NativeList<ActorInventoryItem> inventory,
            ref NativeList<ActorEquipmentSlot> equipment)
        {
            if (contentDb == null || !actorHandle.IsValid)
                return;

            ReadOnlySpan<ContainerItemDef> authoredItems = contentDb.GetActorInventoryItems(actorHandle);
            if (authoredItems.Length == 0)
                return;

            uint resolutionSeed = placedRefId != 0u ? placedRefId : (uint)actorHandle.Value;
            ref readonly var actor = ref contentDb.Get(actorHandle);
            for (int i = 0; i < authoredItems.Length; i++)
            {
                var authored = authoredItems[i];
                if (authored.Count <= 0 || string.IsNullOrWhiteSpace(authored.ItemId))
                    continue;

                if (!TryResolveActorInventoryContent(contentDb, authored.ItemId, resolutionSeed, out var content) || !content.IsValid)
                    continue;

                inventory.Add(new ActorInventoryItem
                {
                    Content = content,
                    Count = authored.Count,
                    Condition = InventoryConditionUtility.ResolveInitialCondition(contentDb, content),
                    AuthoredOrder = i,
                });

                if (content.Kind != ContentReferenceKind.Item)
                    continue;
            }

            MorrowindEquipmentAutoEquipUtility.SelectInitialEquipment(contentDb, actor, inventory.AsArray(), equipment);
        }

        static bool TryResolveActorInventoryContent(
            RuntimeContentDatabase contentDb,
            string itemId,
            uint resolutionSeed,
            out ContentReference content)
        {
            content = default;
            if (ContainerLootUtility.TryResolveDirectCarryable(contentDb, itemId, out content, out _))
                return true;

            if (!contentDb.TryGetItemLeveledListHandle(itemId, out var listHandle))
                return false;

            return ContainerLootUtility.TryResolveLooseLeveledCarryable(contentDb, listHandle, resolutionSeed, out content, out _);
        }

        static void QueueActorCollider(EntityManager entityManager, ref EntityCommandBuffer ecb, Entity logicalEntity)
        {
            var collider = EnsureActorCapsuleCollider();
            if (!collider.IsCreated)
                return;

            RuntimeColliderAttachmentUtility.QueueAttachNewSource(
                entityManager,
                ref ecb,
                logicalEntity,
                collider,
                RuntimeColliderKind.Actor,
                active: true);
        }

        static void QueueActorPickCollider(
            EntityManager entityManager,
            ref EntityCommandBuffer ecb,
            Entity logicalEntity,
            bool isInterior,
            int2 exteriorCell)
        {
            var collider = EnsureActorPickCapsuleCollider();
            if (!collider.IsCreated)
                return;

            Entity pickEntity = ecb.CreateEntity();
            ecb.SetName(pickEntity, new FixedString64Bytes("ActorInteractionPick"));
            ecb.AddComponent<InteractionPickSurfaceTag>(pickEntity);
            ecb.AddComponent<InteractionActorPickSurfaceTag>(pickEntity);
            ecb.AddComponent(pickEntity, new LogicalRefParent { Value = logicalEntity });
            ecb.AppendToBuffer(logicalEntity, new LogicalRefChild { Value = pickEntity });
            ecb.AddComponent(pickEntity, LocalTransform.Identity);
            ecb.AddComponent(pickEntity, new LocalToWorld { Value = float4x4.identity });
            if (isInterior)
                ecb.AddComponent<InteriorCellMember>(pickEntity);
            else
                ecb.AddComponent(pickEntity, new CellLink { Value = exteriorCell });
            RuntimeColliderAttachmentUtility.QueueAttachNewSource(
                entityManager,
                ref ecb,
                pickEntity,
                collider,
                RuntimeColliderKind.InteractionPick,
                active: true);
        }

        static BlobAssetReference<Collider> EnsureActorCapsuleCollider()
        {
            if (WorldResources.ActorCapsuleCollider.IsCreated)
                return WorldResources.ActorCapsuleCollider;

            const float Radius = 0.35f;
            const float Height = 1.8f;
            WorldResources.ActorCapsuleCollider = CapsuleCollider.Create(
                new CapsuleGeometry
                {
                    Vertex0 = new float3(0f, Radius, 0f),
                    Vertex1 = new float3(0f, Height - Radius, 0f),
                    Radius = Radius,
                },
                InteractionCollisionLayers.PlayerBodyFilter);
            return WorldResources.ActorCapsuleCollider;
        }

        static BlobAssetReference<Collider> EnsureActorPickCapsuleCollider()
        {
            if (WorldResources.ActorPickCapsuleCollider.IsCreated)
                return WorldResources.ActorPickCapsuleCollider;

            const float Radius = 0.35f;
            const float Height = 1.8f;
            WorldResources.ActorPickCapsuleCollider = CapsuleCollider.Create(
                new CapsuleGeometry
                {
                    Vertex0 = new float3(0f, Radius, 0f),
                    Vertex1 = new float3(0f, Height - Radius, 0f),
                    Radius = Radius,
                },
                InteractionCollisionLayers.InteractionPickFilter);
            return WorldResources.ActorPickCapsuleCollider;
        }

        static ActorAiNavigationAnchor BuildActorAiAnchor(
            RuntimeContentDatabase contentDb,
            bool isInterior,
            int2 exteriorCell,
            FixedString128Bytes interiorCellId)
        {
            var anchor = new ActorAiNavigationAnchor
            {
                PathGridIndex = -1,
                GridX = exteriorCell.x,
                GridY = exteriorCell.y,
                IsInterior = (byte)(isInterior ? 1 : 0),
            };

            if (isInterior)
            {
                ulong interiorCellHash = InteriorCellIdHash.Hash(interiorCellId);
                anchor.InteriorCellHash = interiorCellHash;
                if (contentDb.TryGetInteriorPathGridHandle(interiorCellHash, out var handle) && handle.IsValid)
                {
                    anchor.PathGridIndex = handle.Index;
                    anchor.IsResolved = 1;
                }
            }
            else if (contentDb.TryGetExteriorPathGridHandle(exteriorCell.x, exteriorCell.y, out var handle) && handle.IsValid)
            {
                anchor.PathGridIndex = handle.Index;
                anchor.IsResolved = 1;
            }

            return anchor;
        }

        static uint BuildActorAiSeed(ActorDefHandle actor, float3 position, int2 exteriorCell, bool isInterior)
        {
            uint seed = math.hash(new uint4(
                (uint)actor.Value,
                math.asuint(position.x) ^ math.asuint(position.z),
                (uint)exteriorCell.x ^ ((uint)exteriorCell.y << 16),
                isInterior ? 1u : 0u));
            return seed == 0u ? 1u : seed;
        }

        static LightInstanceFlags BuildLightInstanceFlags(int flags)
        {
            const int Carry = 0x002;
            const int Negative = 0x004;
            const int Flicker = 0x008;
            const int OffDefault = 0x020;
            const int FlickerSlow = 0x040;
            const int Pulse = 0x080;
            const int PulseSlow = 0x100;

            return new LightInstanceFlags
            {
                Carry = (byte)((flags & Carry) != 0 ? 1 : 0),
                Negative = (byte)((flags & Negative) != 0 ? 1 : 0),
                Flicker = (byte)((flags & Flicker) != 0 ? 1 : 0),
                FlickerSlow = (byte)((flags & FlickerSlow) != 0 ? 1 : 0),
                Pulse = (byte)((flags & Pulse) != 0 ? 1 : 0),
                PulseSlow = (byte)((flags & PulseSlow) != 0 ? 1 : 0),
                OffDefault = (byte)((flags & OffDefault) != 0 ? 1 : 0),
            };
        }

        static bool IsAnimatedLight(in LightInstanceFlags flags)
            => flags.Flicker != 0
               || flags.FlickerSlow != 0
               || flags.Pulse != 0
               || flags.PulseSlow != 0;

        static LightInstanceState BuildLightInstanceState(in LightDef def)
        {
            float3 color = DecodeRgb(def.ColorRgba);
            float rangeMeters = math.max(0.25f, def.Radius * WorldScale.MwUnitsToMeters);
            float intensity = math.max(0.25f, math.cmax(color));
            bool enabled = (def.Flags & 0x020) == 0;

            return new LightInstanceState
            {
                Enabled = (byte)(enabled ? 1 : 0),
                BaseColorRgb = color,
                BaseIntensity = intensity,
                BaseRange = rangeMeters,
                CurrentIntensity = intensity,
                CurrentRange = rangeMeters,
                AnimationTime = 0f,
            };
        }

        static float3 DecodeRgb(uint value)
        {
            return new float3(
                ((value >> 0) & 0xFFu) / 255f,
                ((value >> 8) & 0xFFu) / 255f,
                ((value >> 16) & 0xFFu) / 255f);
        }

        static void TryQueueAudioEmitterAuthoring(
            ref EntityCommandBuffer ecb,
            Entity logicalEntity,
            RuntimeContentDatabase contentDb,
            string primarySoundId,
            string secondarySoundId)
        {
            contentDb.TryGetSoundHandle(primarySoundId, out SoundDefHandle primarySound);
            contentDb.TryGetSoundHandle(secondarySoundId, out SoundDefHandle secondarySound);
            if (!primarySound.IsValid && !secondarySound.IsValid)
                return;

            ecb.AddComponent(logicalEntity, new AudioEmitterAuthoring
            {
                PrimarySound = primarySound,
                SecondarySound = secondarySound,
            });
        }

    }
}
