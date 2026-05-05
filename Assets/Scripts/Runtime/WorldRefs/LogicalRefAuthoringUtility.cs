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
        const int MaxLeveledResolutionDepth = 16;
        const int ItemLeveledAllLevelsFlag = 0x02;
        static readonly uint BookRecordTag = MakeTag('B', 'O', 'O', 'K');

        public static bool QueueAttach(
            EntityManager entityManager,
            ref EntityCommandBuffer ecb,
            Entity logicalEntity,
            ref RuntimeContentBlob content,
            ContentReference contentReference,
            float3 worldPosition = default,
            bool isInterior = false,
            int2 exteriorCell = default,
            FixedString128Bytes interiorCellId = default,
            uint placedRefId = 0u,
            bool attachDoorInteractable = false,
            DoorInteractable doorInteractable = default)
        {
            if (!RuntimeContentBlobUtility.IsValid(ref content, contentReference))
                return false;

            switch (contentReference.Kind)
            {
                case ContentReferenceKind.Actor:
                {
                    var handle = new ActorDefHandle { Value = contentReference.HandleValue };
                    ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref content, handle);
                    ecb.AddComponent(logicalEntity, new ActorSpawnSource { Definition = handle });
                    ecb.AddComponent(logicalEntity, BuildPassiveActorPresence(ref actor));
                    QueueActorRuntimeComponents(
                        entityManager,
                        ref ecb,
                        logicalEntity,
                        ref content,
                        handle,
                        ref actor,
                        worldPosition,
                        isInterior,
                        exteriorCell,
                        interiorCellId,
                        placedRefId);
                    MorrowindScriptRuntimeAuthoringUtility.TryQueueObjectScriptByIdHash(ref ecb, logicalEntity, ref content, actor.ScriptIdHash);
                    return true;
                }

                case ContentReferenceKind.Activator:
                {
                    var handle = new ActivatorDefHandle { Value = contentReference.HandleValue };
                    ref RuntimeBaseDefBlob def = ref RuntimeContentBlobUtility.Get(ref content, handle);
                    ecb.AddComponent(logicalEntity, new ActivatorAuthoring { Definition = handle });
                    TryQueueScriptedDoorMotion(ref ecb, logicalEntity, ref content, def.ScriptIdHash);
                    MorrowindScriptRuntimeAuthoringUtility.TryQueueObjectScriptByIdHash(ref ecb, logicalEntity, ref content, def.ScriptIdHash);
                    TryQueueAudioEmitterAuthoring(ref ecb, logicalEntity, ref content, def.SoundIdHash, def.AuxSoundIdHash);
                    return true;
                }

                case ContentReferenceKind.Door:
                {
                    var handle = new DoorDefHandle { Value = contentReference.HandleValue };
                    ref RuntimeBaseDefBlob def = ref RuntimeContentBlobUtility.Get(ref content, handle);
                    ecb.AddComponent(logicalEntity, new DoorAuthoring { Definition = handle });
                    bool hasScriptedDoorMotion = TryQueueScriptedDoorMotion(ref ecb, logicalEntity, ref content, def.ScriptIdHash);
                    if (!hasScriptedDoorMotion && attachDoorInteractable && doorInteractable.IsTeleport == 0)
                        QueueDoorMotion(ref ecb, logicalEntity, DoorMotionAuthoringUtility.BuildOpenMwDoorMotion());
                    MorrowindScriptRuntimeAuthoringUtility.TryQueueObjectScriptByIdHash(ref ecb, logicalEntity, ref content, def.ScriptIdHash);
                    TryQueueAudioEmitterAuthoring(ref ecb, logicalEntity, ref content, def.SoundIdHash, def.AuxSoundIdHash);
                    if (attachDoorInteractable)
                        ecb.AddComponent(logicalEntity, doorInteractable);
                    return true;
                }

                case ContentReferenceKind.Container:
                {
                    var handle = new ContainerDefHandle { Value = contentReference.HandleValue };
                    ref RuntimeBaseDefBlob def = ref RuntimeContentBlobUtility.Get(ref content, handle);
                    ecb.AddComponent(logicalEntity, new ContainerAuthoring { Definition = handle });
                    MorrowindScriptRuntimeAuthoringUtility.TryQueueObjectScriptByIdHash(ref ecb, logicalEntity, ref content, def.ScriptIdHash);
                    TryQueueAudioEmitterAuthoring(ref ecb, logicalEntity, ref content, def.SoundIdHash, def.AuxSoundIdHash);
                    return true;
                }

                case ContentReferenceKind.Item:
                {
                    var handle = new ItemDefHandle { Value = contentReference.HandleValue };
                    ref RuntimeBaseDefBlob def = ref RuntimeContentBlobUtility.Get(ref content, handle);
                    ecb.AddComponent(logicalEntity, new ItemPickupAuthoring { Definition = handle });
                    MorrowindScriptRuntimeAuthoringUtility.TryQueueObjectScriptByIdHash(ref ecb, logicalEntity, ref content, def.ScriptIdHash);
                    if (def.RecordTag == BookRecordTag)
                        ecb.AddComponent<BookTag>(logicalEntity);
                    TryQueueAudioEmitterAuthoring(ref ecb, logicalEntity, ref content, def.SoundIdHash, def.AuxSoundIdHash);
                    return true;
                }

                case ContentReferenceKind.Light:
                {
                    var handle = new LightDefHandle { Value = contentReference.HandleValue };
                    ref RuntimeLightDefBlob def = ref RuntimeContentBlobUtility.Get(ref content, handle);
                    ecb.AddComponent(logicalEntity, new LightSourceAuthoring { Definition = handle });
                    MorrowindScriptRuntimeAuthoringUtility.TryQueueObjectScriptByIdHash(ref ecb, logicalEntity, ref content, def.ScriptIdHash);
                    var flags = BuildLightInstanceFlags(def.Flags);
                    ecb.AddComponent(logicalEntity, flags);
                    ecb.AddComponent(logicalEntity, BuildLightInstanceState(ref def));
                    if (IsAnimatedLight(flags))
                        ecb.AddComponent<LightInstanceAnimated>(logicalEntity);
                    if (LightPresentationOffsetUtility.TryResolveAttachLightOffset(ref content, handle, out float3 lightOffset))
                        ecb.AddComponent(logicalEntity, new LightPresentationOffset { LocalPosition = lightOffset });
                    ecb.AddComponent(logicalEntity, new LightPresentationLink { Slot = -1 });
                    TryQueueAudioEmitterAuthoring(ref ecb, logicalEntity, ref content, def.SoundIdHash, 0UL);
                    return true;
                }

                case ContentReferenceKind.Static:
                {
                    var handle = new GenericRecordDefHandle { Value = contentReference.HandleValue };
                    ref RuntimeGenericRecordDefBlob def = ref RuntimeContentBlobUtility.GetStatic(ref content, handle);
                    ecb.AddComponent(logicalEntity, new StaticRefAuthoring { Definition = handle });
                    MorrowindScriptRuntimeAuthoringUtility.TryQueueObjectScriptByIdHash(ref ecb, logicalEntity, ref content, def.ScriptIdHash);
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
            ref RuntimeContentBlob content,
            ulong scriptIdHash)
        {
            if (!DoorMotionAuthoringUtility.TryBuildByScriptIdHash(ref content, scriptIdHash, out DoorMotionState motion))
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

        static PassiveActorPresence BuildPassiveActorPresence(ref RuntimeActorDefBlob actor)
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
            ref RuntimeContentBlob content,
            ActorDefHandle actorHandle,
            ref RuntimeActorDefBlob actor,
            float3 worldPosition,
            bool isInterior,
            int2 exteriorCell,
            FixedString128Bytes interiorCellId,
            uint placedRefId)
        {
            var statSeed = MorrowindActorMovementStats.CreateSeedFromActor(ref content, ref actor);
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
                ref content,
                statSeed.Attributes,
                statSeed.Skills,
                statSeed.Vitals,
                statSeed.EffectModifiers,
                inventoryWeight: 0f);
            ecb.AddComponent(logicalEntity, derivedMovement);
            var knownSpells = ecb.AddBuffer<ActorKnownSpell>(logicalEntity);
            var actorSpells = MorrowindActorMovementStats.BuildKnownSpellListFromActor(ref content, actorHandle);
            for (int i = 0; i < actorSpells.Length; i++)
                knownSpells.Add(actorSpells[i]);
            ecb.AddBuffer<ActorActiveMagicEffect>(logicalEntity);
            ecb.AddBuffer<ActorActiveSpell>(logicalEntity);
            ecb.AddBuffer<ActorUsedPower>(logicalEntity);
            ecb.AddComponent(logicalEntity, new ActorMagicCastState());
            ecb.AddComponent<ActorActiveMagicEffectDirty>(logicalEntity);
            QueueActorFactionMembership(ref ecb, logicalEntity, ref content, ref actor);

            QueueActorCollider(entityManager, ref ecb, logicalEntity);
            QueueActorPickCollider(
                entityManager,
                ref ecb,
                logicalEntity,
                isInterior,
                exteriorCell);

            if (ActorAiRuntimeAuthoringUtility.HasPackage(ref content, actorHandle))
            {
                var anchor = BuildActorAiAnchor(ref content, isInterior, exteriorCell, interiorCellId);
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
                ActorAiRuntimeAuthoringUtility.HydratePackages(ref content, actorHandle, anchor, packages);
                if (packages.Length > 0)
                {
                    ActorMovementAuthoringUtility.QueueEnsureMovableActor(
                            ref ecb,
                            logicalEntity,
                            MorrowindActorMovementStats.BuildMovementSpeed(
                            ref content,
                            actor.Kind,
                            statSeed.Attributes,
                            statSeed.Skills,
                            statSeed.Vitals,
                            statSeed.EffectModifiers,
                            derivedMovement));
                }
            }

            QueueActorInventoryAndEquipment(ref ecb, logicalEntity, ref content, actorHandle, placedRefId);
        }

        static void QueueActorFactionMembership(
            ref EntityCommandBuffer ecb,
            Entity logicalEntity,
            ref RuntimeContentBlob content,
            ref RuntimeActorDefBlob actor)
        {
            if (actor.FactionIdHash == 0UL)
                return;

            if (!RuntimeContentBlobUtility.TryGetFactionHandleByIdHash(ref content, actor.FactionIdHash, out var factionHandle) || !factionHandle.IsValid)
                throw new InvalidOperationException($"Actor hash {actor.IdHash} references unknown faction hash {actor.FactionIdHash}.");

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
            ref RuntimeContentBlob content,
            ActorDefHandle actorHandle,
            uint placedRefId)
        {
            var inventory = new NativeList<ActorInventoryItem>(Allocator.Temp);
            var equipment = new NativeList<ActorEquipmentSlot>(Allocator.Temp);
            try
            {
                HydrateActorInventoryAndEquipment(ref content, actorHandle, placedRefId, ref inventory, ref equipment);

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
            ref RuntimeContentBlob content,
            ActorDefHandle actorHandle,
            uint placedRefId,
            ref NativeList<ActorInventoryItem> inventory,
            ref NativeList<ActorEquipmentSlot> equipment)
        {
            if (!actorHandle.IsValid)
                return;

            ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref content, actorHandle);
            RuntimeContentBlobUtility.RequireRange(actor.FirstInventoryIndex, actor.InventoryCount, content.ActorInventoryItems.Length, "actor inventory");
            if (actor.InventoryCount == 0)
                return;

            uint resolutionSeed = placedRefId != 0u ? placedRefId : (uint)actorHandle.Value;
            for (int i = 0; i < actor.InventoryCount; i++)
            {
                ref RuntimeContainerItemDefBlob authored = ref content.ActorInventoryItems[actor.FirstInventoryIndex + i];
                if (authored.Count <= 0)
                    continue;
                if (authored.ItemIdHash == 0UL)
                    throw new InvalidOperationException($"[VVardenfell][WorldRefs] actor hash {actor.IdHash} has an authored inventory item with no id at offset {i}.");

                if (!TryResolveActorInventoryContent(ref content, authored.ItemIdHash, resolutionSeed, out var itemContent) || !itemContent.IsValid)
                    throw new InvalidOperationException($"[VVardenfell][WorldRefs] actor hash {actor.IdHash} references unresolved inventory item hash {authored.ItemIdHash}.");

                inventory.Add(new ActorInventoryItem
                {
                    Content = itemContent,
                    Count = authored.Count,
                    Condition = InventoryConditionUtility.ResolveInitialCondition(ref content, itemContent),
                    AuthoredOrder = i,
                });

                if (itemContent.Kind != ContentReferenceKind.Item)
                    continue;
            }

            MorrowindEquipmentAutoEquipUtility.SelectInitialEquipment(ref content, ref actor, inventory.AsArray(), equipment);
        }

        static bool TryResolveActorInventoryContent(
            ref RuntimeContentBlob contentBlob,
            ulong itemIdHash,
            uint resolutionSeed,
            out ContentReference content)
        {
            content = default;
            if (RuntimeContentBlobUtility.TryResolvePlaceableByIdHash(ref contentBlob, itemIdHash, out content)
                && (content.Kind == ContentReferenceKind.Item
                    || content.Kind == ContentReferenceKind.Light
                    || content.Kind == ContentReferenceKind.LeveledItem))
            {
                return true;
            }

            if (!RuntimeContentBlobUtility.TryGetItemLeveledListHandleByIdHash(ref contentBlob, itemIdHash, out var listHandle))
                return false;

            var visited = new NativeList<int>(Allocator.Temp);
            try
            {
                return TryResolveLooseLeveledCarryable(ref contentBlob, listHandle, resolutionSeed, 0, ref visited, out content);
            }
            finally
            {
                if (visited.IsCreated)
                    visited.Dispose();
            }
        }

        static bool TryResolveLooseLeveledCarryable(
            ref RuntimeContentBlob contentBlob,
            ItemLeveledListDefHandle listHandle,
            uint seed,
            int depth,
            ref NativeList<int> visited,
            out ContentReference content)
        {
            content = default;
            if (!listHandle.IsValid)
                return false;
            if (depth >= MaxLeveledResolutionDepth)
                throw new InvalidOperationException($"[VVardenfell][WorldRefs] item leveled-list recursion cap reached at depth {MaxLeveledResolutionDepth}.");

            for (int i = 0; i < visited.Length; i++)
            {
                if (visited[i] == listHandle.Value)
                    throw new InvalidOperationException($"[VVardenfell][WorldRefs] item leveled-list cycle detected at handle {listHandle.Value}.");
            }

            visited.Add(listHandle.Value);
            ref RuntimeItemLeveledListDefBlob list = ref RuntimeContentBlobUtility.Get(ref contentBlob, listHandle);
            RuntimeContentBlobUtility.RequireRange(list.FirstEntryIndex, list.EntryCount, contentBlob.ItemLeveledListEntries.Length, "item leveled-list entry");
            try
            {
                if (RollPercent(seed) < list.ChanceNone || list.EntryCount == 0)
                    return false;

                bool allLevels = (list.Flags & ItemLeveledAllLevelsFlag) != 0;
                int highestEligibleLevel = 0;
                bool hasEligible = false;
                for (int i = 0; i < list.EntryCount; i++)
                {
                    int level = contentBlob.ItemLeveledListEntries[list.FirstEntryIndex + i].Level;
                    if (level > highestEligibleLevel && level <= ContainerLootUtility.FixedLeveledLootPlayerLevel)
                    {
                        highestEligibleLevel = level;
                        hasEligible = true;
                    }
                }

                if (!hasEligible)
                    return false;

                Span<int> candidateEntryOffsets = stackalloc int[list.EntryCount];
                int candidateCount = 0;
                for (int i = 0; i < list.EntryCount; i++)
                {
                    int level = contentBlob.ItemLeveledListEntries[list.FirstEntryIndex + i].Level;
                    if (level > ContainerLootUtility.FixedLeveledLootPlayerLevel)
                        continue;
                    if (allLevels || level == highestEligibleLevel)
                        candidateEntryOffsets[candidateCount++] = i;
                }

                if (candidateCount == 0)
                    return false;

                int candidateIndex = NextRandomIndex(ref seed, candidateCount);
                ref RuntimeItemLeveledListEntryDefBlob selected = ref contentBlob.ItemLeveledListEntries[list.FirstEntryIndex + candidateEntryOffsets[candidateIndex]];
                if (selected.ItemIdHash == 0UL)
                    throw new InvalidOperationException($"[VVardenfell][WorldRefs] leveled-list hash {list.IdHash} has an entry with no item id.");

                if (RuntimeContentBlobUtility.TryResolvePlaceableByIdHash(ref contentBlob, selected.ItemIdHash, out content)
                    && (content.Kind == ContentReferenceKind.Item || content.Kind == ContentReferenceKind.Light))
                {
                    return true;
                }

                if (!RuntimeContentBlobUtility.TryGetItemLeveledListHandleByIdHash(ref contentBlob, selected.ItemIdHash, out ItemLeveledListDefHandle nestedHandle))
                    throw new InvalidOperationException($"[VVardenfell][WorldRefs] missing leveled-list target hash {selected.ItemIdHash} referenced by list hash {list.IdHash}.");

                seed = math.hash(new uint2(seed, (uint)candidateIndex + 1u));
                return TryResolveLooseLeveledCarryable(ref contentBlob, nestedHandle, seed, depth + 1, ref visited, out content);
            }
            finally
            {
                if (visited.Length > 0)
                    visited.RemoveAt(visited.Length - 1);
            }
        }

        static int RollPercent(uint seed)
        {
            uint state = seed == 0u ? 0xA341316Cu : seed;
            state = state * 1664525u + 1013904223u;
            return (int)(state % 100u);
        }

        static int NextRandomIndex(ref uint seed, int count)
        {
            seed = seed == 0u ? 0xC8013EA4u : seed;
            seed = seed * 1664525u + 1013904223u;
            return count <= 1 ? 0 : (int)(seed % (uint)count);
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
            ref RuntimeContentBlob content,
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
                if (RuntimeContentBlobUtility.TryGetInteriorPathGridHandleByCellHash(ref content, interiorCellHash, out var handle) && handle.IsValid)
                {
                    anchor.PathGridIndex = handle.Index;
                    anchor.IsResolved = 1;
                }
            }
            else if (RuntimeContentBlobUtility.TryGetExteriorPathGridHandle(ref content, exteriorCell.x, exteriorCell.y, out var handle) && handle.IsValid)
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

        static uint MakeTag(char a, char b, char c, char d)
            => (uint)a | ((uint)b << 8) | ((uint)c << 16) | ((uint)d << 24);

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

        static LightInstanceState BuildLightInstanceState(ref RuntimeLightDefBlob def)
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
            ref RuntimeContentBlob content,
            ulong primarySoundIdHash,
            ulong secondarySoundIdHash)
        {
            TryGetSoundHandle(ref content, primarySoundIdHash, out SoundDefHandle primarySound);
            TryGetSoundHandle(ref content, secondarySoundIdHash, out SoundDefHandle secondarySound);
            if (!primarySound.IsValid && !secondarySound.IsValid)
                return;

            ecb.AddComponent(logicalEntity, new AudioEmitterAuthoring
            {
                PrimarySound = primarySound,
                SecondarySound = secondarySound,
            });
        }

        static bool TryGetSoundHandle(ref RuntimeContentBlob content, ulong soundIdHash, out SoundDefHandle handle)
        {
            handle = default;
            return soundIdHash != 0UL
                   && RuntimeContentBlobUtility.TryGetSoundHandleByIdHash(ref content, soundIdHash, out handle)
                   && handle.IsValid;
        }

    }
}
