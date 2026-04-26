using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.AI;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Inventory;
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
                    ecb.AddComponent(logicalEntity, new DialogueSpeakerAuthoring { Definition = handle });
                    ecb.AddComponent(logicalEntity, BuildPassiveActorPresence(handle, actor));
                    QueueActorRuntimeComponents(
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
                    return true;
                }

                case ContentReferenceKind.Activator:
                {
                    var handle = new ActivatorDefHandle { Value = contentReference.HandleValue };
                    ref readonly var def = ref contentDb.Get(handle);
                    ecb.AddComponent(logicalEntity, new ActivatorAuthoring { Definition = handle });
                    TryQueueAudioEmitterAuthoring(ref ecb, logicalEntity, contentDb, def.SoundId, def.AuxSoundId);
                    return true;
                }

                case ContentReferenceKind.Door:
                {
                    var handle = new DoorDefHandle { Value = contentReference.HandleValue };
                    ref readonly var def = ref contentDb.Get(handle);
                    ecb.AddComponent(logicalEntity, new DoorAuthoring { Definition = handle });
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
                    TryQueueAudioEmitterAuthoring(ref ecb, logicalEntity, contentDb, def.SoundId, def.AuxSoundId);
                    return true;
                }

                case ContentReferenceKind.Item:
                {
                    var handle = new ItemDefHandle { Value = contentReference.HandleValue };
                    ref readonly var def = ref contentDb.Get(handle);
                    ecb.AddComponent(logicalEntity, new ItemPickupAuthoring { Definition = handle });
                    TryQueueAudioEmitterAuthoring(ref ecb, logicalEntity, contentDb, def.SoundId, def.AuxSoundId);
                    return true;
                }

                case ContentReferenceKind.Light:
                {
                    var handle = new LightDefHandle { Value = contentReference.HandleValue };
                    ref readonly var def = ref contentDb.Get(handle);
                    ecb.AddComponent(logicalEntity, new LightSourceAuthoring { Definition = handle });
                    ecb.AddComponent(logicalEntity, BuildLightInstanceFlags(def.Flags));
                    ecb.AddComponent(logicalEntity, BuildLightInstanceState(def));
                    ecb.AddComponent(logicalEntity, new LightPresentationLink { Slot = -1 });
                    TryQueueAudioEmitterAuthoring(ref ecb, logicalEntity, contentDb, def.SoundId, null);
                    if (contentDb.TryGetSoundHandle(def.SoundId, out SoundDefHandle ambientSound) && ambientSound.IsValid)
                    {
                        ecb.AddComponent(logicalEntity, new InteriorAmbientSourceAuthoring
                        {
                            AmbientSound = ambientSound,
                        });
                    }
                    return true;
                }

                case ContentReferenceKind.Static:
                {
                    var handle = new GenericRecordDefHandle { Value = contentReference.HandleValue };
                    ecb.AddComponent(logicalEntity, new StaticRefAuthoring { Definition = handle });
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

        static PassiveActorPresence BuildPassiveActorPresence(ActorDefHandle handle, in ActorDef actor)
        {
            string displayName = !string.IsNullOrWhiteSpace(actor.Name)
                ? actor.Name.Trim()
                : !string.IsNullOrWhiteSpace(actor.Id)
                    ? actor.Id.Trim()
                    : "Actor";
            if (displayName.Length > 127)
                displayName = displayName.Substring(0, 127);

            bool canTalk = actor.Kind == ActorDefKind.Npc;
            return new PassiveActorPresence
            {
                Definition = handle,
                Family = (byte)(actor.Kind == ActorDefKind.Npc ? PassiveActorFamily.Npc : PassiveActorFamily.Creature),
                CanTalk = (byte)(canTalk ? 1 : 0),
                DisplayName = displayName,
            };
        }

        static void QueueActorRuntimeComponents(
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
            ecb.AddComponent(logicalEntity, MorrowindActorMovementStats.BuildDerived(
                contentDb,
                statSeed.Attributes,
                statSeed.Skills,
                statSeed.Vitals,
                statSeed.EffectModifiers,
                inventoryWeight: 0f));
            ecb.AddComponent(logicalEntity, MorrowindActorMovementStats.CreateIdentityFromActor(actor));
            ecb.AddBuffer<ActorActiveMagicEffect>(logicalEntity);

            ecb.AddComponent(logicalEntity, new MorrowindMovementIntent());
            ecb.AddComponent(logicalEntity, new MorrowindActorKinematicState());
            ecb.AddComponent(logicalEntity, MorrowindMovementTuning.OpenMwDefaults());
            ecb.AddComponent(logicalEntity, new MorrowindMovementFrameTrace());
            QueueActorCollider(ref ecb, logicalEntity);

            ecb.AddComponent(logicalEntity, PathGridTraversalSettings.Defaults);
            ecb.AddComponent(logicalEntity, new PathGridTraversalState());
            ecb.AddComponent(logicalEntity, new PathGridTraversalPendingRequest());
            ecb.SetComponentEnabled<PathGridTraversalPendingRequest>(logicalEntity, false);
            ecb.AddComponent(logicalEntity, new PathGridTraversalAwaitingResult());
            ecb.SetComponentEnabled<PathGridTraversalAwaitingResult>(logicalEntity, false);
            ecb.AddBuffer<PathGridTraversalNode>(logicalEntity);

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
            var packages = ecb.AddBuffer<ActorAiPackageRuntime>(logicalEntity);
            ActorAiRuntimeAuthoringUtility.HydratePackages(contentDb, actorHandle, anchor, packages);

            var inventory = ecb.AddBuffer<ActorInventoryItem>(logicalEntity);
            var equipment = ecb.AddBuffer<ActorEquipmentSlot>(logicalEntity);
            HydrateActorInventoryAndEquipment(contentDb, actorHandle, placedRefId, inventory, equipment);
        }

        static void HydrateActorInventoryAndEquipment(
            RuntimeContentDatabase contentDb,
            ActorDefHandle actorHandle,
            uint placedRefId,
            DynamicBuffer<ActorInventoryItem> inventory,
            DynamicBuffer<ActorEquipmentSlot> equipment)
        {
            if (contentDb == null || !actorHandle.IsValid)
                return;

            ReadOnlySpan<ContainerItemDef> authoredItems = contentDb.GetActorInventoryItems(actorHandle);
            if (authoredItems.Length == 0)
                return;

            const int SlotCapacity = 32;
            var bestScores = new long[SlotCapacity];
            var bestInventoryIndices = new int[SlotCapacity];
            for (int i = 0; i < SlotCapacity; i++)
            {
                bestScores[i] = long.MinValue;
                bestInventoryIndices[i] = -1;
            }

            uint resolutionSeed = placedRefId != 0u ? placedRefId : (uint)actorHandle.Value;
            ref readonly var actor = ref contentDb.Get(actorHandle);
            for (int i = 0; i < authoredItems.Length; i++)
            {
                var authored = authoredItems[i];
                if (authored.Count <= 0 || string.IsNullOrWhiteSpace(authored.ItemId))
                    continue;

                if (!TryResolveActorInventoryContent(contentDb, authored.ItemId, resolutionSeed, out var content) || !content.IsValid)
                    continue;

                int inventoryIndex = inventory.Length;
                inventory.Add(new ActorInventoryItem
                {
                    Content = content,
                    Count = authored.Count,
                    AuthoredOrder = i,
                });

                if (content.Kind != ContentReferenceKind.Item)
                    continue;

                var itemHandle = new ItemDefHandle { Value = content.HandleValue };
                if (!contentDb.TryGetItemEquipment(itemHandle, out var itemEquipment))
                    continue;
                int slot = (int)itemEquipment.Slot;
                if ((uint)slot >= SlotCapacity || slot == (int)ItemEquipmentSlot.None)
                    continue;

                long score = ScoreInitialEquipment(itemEquipment, i);
                if (score <= bestScores[slot])
                    continue;

                bestScores[slot] = score;
                bestInventoryIndices[slot] = inventoryIndex;
            }

            for (int slot = 0; slot < SlotCapacity; slot++)
            {
                int inventoryIndex = bestInventoryIndices[slot];
                if (inventoryIndex < 0 || inventoryIndex >= inventory.Length)
                    continue;

                var item = inventory[inventoryIndex];
                var itemHandle = new ItemDefHandle { Value = item.Content.HandleValue };
                if (!contentDb.TryGetItemEquipment(itemHandle, out var itemEquipment))
                    continue;

                equipment.Add(new ActorEquipmentSlot
                {
                    Slot = (ItemEquipmentSlot)slot,
                    Content = item.Content,
                    InventoryIndex = inventoryIndex,
                    VisualMode = ResolveEquipmentVisualMode(itemEquipment),
                });
            }
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

        static long ScoreInitialEquipment(in ItemEquipmentDef equipment, int authoredOrder)
        {
            long tieBreaker = 1000 - System.Math.Min(999, authoredOrder);
            return equipment.Kind switch
            {
                ItemEquipmentKind.Weapon => 3_000_000_000L + equipment.DamageMax * 1_000_000L + equipment.Value * 100L + tieBreaker,
                ItemEquipmentKind.Armor => 2_000_000_000L + equipment.Armor * 1_000_000L + equipment.Value * 100L + tieBreaker,
                ItemEquipmentKind.Clothing => 1_000_000_000L + equipment.Value * 100L + tieBreaker,
                _ => tieBreaker,
            };
        }

        static byte ResolveEquipmentVisualMode(in ItemEquipmentDef equipment)
        {
            if (equipment.Kind == ItemEquipmentKind.Weapon || equipment.Slot == ItemEquipmentSlot.Shield)
                return 2;
            if (equipment.Kind == ItemEquipmentKind.Armor || equipment.Kind == ItemEquipmentKind.Clothing)
                return 1;
            return 0;
        }

        static void QueueActorCollider(ref EntityCommandBuffer ecb, Entity logicalEntity)
        {
            var collider = EnsureActorCapsuleCollider();
            if (!collider.IsCreated)
                return;

            ecb.AddComponent(logicalEntity, new RuntimeColliderSource
            {
                Value = collider,
                Kind = RuntimeColliderKind.Actor,
            });
            ecb.AddComponent(logicalEntity, new PhysicsCollider { Value = collider });
            ecb.AddSharedComponent(logicalEntity, new PhysicsWorldIndex { Value = 0 });
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
                if (contentDb.TryGetInteriorPathGridHandle(interiorCellId.ToString(), out var handle) && handle.IsValid)
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
