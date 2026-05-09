using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.AI;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Pathfinding;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(MorrowindDamageSystemGroup))]
    [UpdateBefore(typeof(ActorAiPlannerSystem))]
    [BurstCompile]
    public partial struct MorrowindCombatTargetSelectionSystem : ISystem
    {
        const ulong AttackVoiceDialogueHash = 0xB138769A1AF3A55DUL; // RuntimeContentStableHash.HashId("attack")
        EntityQuery _query;
        NativeList<CombatPackageApplyRequest> _applyRequests;
        NativeList<CombatPackageClearRequest> _clearRequests;
        NativeList<CombatVoiceQueueRequest> _voiceRequests;
        EntityTypeHandle _entityHandle;
        ComponentTypeHandle<ActorSpawnSource> _sourceHandle;
        ComponentTypeHandle<ActorDead> _deadHandle;
        ComponentTypeHandle<ActorActiveCombatTarget> _activeTargetHandle;
        ComponentTypeHandle<LocalTransform> _transformHandle;
        ComponentTypeHandle<ActorLocalBounds> _boundsHandle;
        ComponentTypeHandle<ActorAiState> _aiStateHandle;
        BufferTypeHandle<ActorCombatTarget> _combatTargetHandle;
        BufferTypeHandle<ActorAiPackageRuntime> _packageHandle;
        ComponentLookup<ActorDead> _deadLookup;
        ComponentLookup<PlacedRefRuntimeState> _placedRefStateLookup;
        ComponentLookup<PlacedRefIdentity> _placedRefLookup;
        ComponentLookup<LocalTransform> _transformLookup;
        ComponentLookup<ActorLocalBounds> _boundsLookup;
        ComponentLookup<PlayerCharacterComponent> _playerLookup;
        BufferLookup<ActorEquipmentSlot> _equipmentLookup;
        float _meleeFollowInset;

        public void OnCreate(ref SystemState systemState)
        {
            _query = systemState.GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ActorSpawnSource>(),
                    ComponentType.ReadOnly<ActorDead>(),
                    ComponentType.ReadWrite<ActorActiveCombatTarget>(),
                    ComponentType.ReadOnly<LocalTransform>(),
                    ComponentType.ReadOnly<ActorLocalBounds>(),
                    ComponentType.ReadOnly<ActorAiState>(),
                    ComponentType.ReadWrite<ActorCombatTarget>(),
                    ComponentType.ReadOnly<ActorAiPackageRuntime>(),
                },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });

            _applyRequests = new NativeList<CombatPackageApplyRequest>(Allocator.Persistent);
            _clearRequests = new NativeList<CombatPackageClearRequest>(Allocator.Persistent);
            _voiceRequests = new NativeList<CombatVoiceQueueRequest>(Allocator.Persistent);
            _entityHandle = systemState.GetEntityTypeHandle();
            _sourceHandle = systemState.GetComponentTypeHandle<ActorSpawnSource>(isReadOnly: true);
            _deadHandle = systemState.GetComponentTypeHandle<ActorDead>(isReadOnly: true);
            _activeTargetHandle = systemState.GetComponentTypeHandle<ActorActiveCombatTarget>(isReadOnly: false);
            _transformHandle = systemState.GetComponentTypeHandle<LocalTransform>(isReadOnly: true);
            _boundsHandle = systemState.GetComponentTypeHandle<ActorLocalBounds>(isReadOnly: true);
            _aiStateHandle = systemState.GetComponentTypeHandle<ActorAiState>(isReadOnly: true);
            _combatTargetHandle = systemState.GetBufferTypeHandle<ActorCombatTarget>(isReadOnly: false);
            _packageHandle = systemState.GetBufferTypeHandle<ActorAiPackageRuntime>(isReadOnly: true);
            _deadLookup = systemState.GetComponentLookup<ActorDead>(isReadOnly: true);
            _placedRefStateLookup = systemState.GetComponentLookup<PlacedRefRuntimeState>(isReadOnly: true);
            _placedRefLookup = systemState.GetComponentLookup<PlacedRefIdentity>(isReadOnly: true);
            _transformLookup = systemState.GetComponentLookup<LocalTransform>(isReadOnly: true);
            _boundsLookup = systemState.GetComponentLookup<ActorLocalBounds>(isReadOnly: true);
            _playerLookup = systemState.GetComponentLookup<PlayerCharacterComponent>(isReadOnly: true);
            _equipmentLookup = systemState.GetBufferLookup<ActorEquipmentSlot>(isReadOnly: true);

            systemState.RequireForUpdate(_query);
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<MorrowindCombatRuntimeState>();
            systemState.RequireForUpdate<PathGridTraversalSettings>();
        }

        public void OnDestroy(ref SystemState systemState)
        {
            if (_applyRequests.IsCreated)
                _applyRequests.Dispose();
            if (_clearRequests.IsCreated)
                _clearRequests.Dispose();
            if (_voiceRequests.IsCreated)
                _voiceRequests.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][CombatTarget] target selection requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;

            Entity scriptRuntimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            if (!systemState.EntityManager.HasBuffer<MorrowindCombatVoiceResolveRequest>(scriptRuntimeEntity))
                throw new InvalidOperationException("[VVardenfell][CombatTarget] Script runtime has no MorrowindCombatVoiceResolveRequest buffer.");
            if (!systemState.EntityManager.HasBuffer<MorrowindScriptActiveSay>(scriptRuntimeEntity))
                throw new InvalidOperationException("[VVardenfell][CombatTarget] Script runtime has no MorrowindScriptActiveSay buffer.");

            ref var combatState = ref SystemAPI.GetSingletonRW<MorrowindCombatRuntimeState>().ValueRW;
            _meleeFollowInset = math.max(0f, SystemAPI.GetSingleton<PathGridTraversalSettings>().FinalArrivalDistance);
            var random = new Unity.Mathematics.Random(combatState.RandomState == 0u ? 0x6E624EB7u : combatState.RandomState);
            uint voiceSeed = random.NextUInt();
            combatState.RandomState = random.state == 0u ? 0x6E624EB7u : random.state;

            int actorCount = _query.CalculateEntityCount();
            EnsureCapacity(ref _applyRequests, actorCount);
            EnsureCapacity(ref _clearRequests, actorCount);
            EnsureCapacity(ref _voiceRequests, actorCount);
            _applyRequests.Clear();
            _clearRequests.Clear();
            _voiceRequests.Clear();

            _entityHandle.Update(ref systemState);
            _sourceHandle.Update(ref systemState);
            _deadHandle.Update(ref systemState);
            _activeTargetHandle.Update(ref systemState);
            _transformHandle.Update(ref systemState);
            _boundsHandle.Update(ref systemState);
            _aiStateHandle.Update(ref systemState);
            _combatTargetHandle.Update(ref systemState);
            _packageHandle.Update(ref systemState);
            _deadLookup.Update(ref systemState);
            _placedRefStateLookup.Update(ref systemState);
            _placedRefLookup.Update(ref systemState);
            _transformLookup.Update(ref systemState);
            _boundsLookup.Update(ref systemState);
            _playerLookup.Update(ref systemState);
            _equipmentLookup.Update(ref systemState);

            systemState.Dependency = new CombatTargetSelectionJob
            {
                Content = contentBlobReference.Blob,
                VoiceSeed = voiceSeed == 0u ? 1u : voiceSeed,
                EntityHandle = _entityHandle,
                SourceHandle = _sourceHandle,
                DeadHandle = _deadHandle,
                ActiveTargetHandle = _activeTargetHandle,
                TransformHandle = _transformHandle,
                BoundsHandle = _boundsHandle,
                AiStateHandle = _aiStateHandle,
                CombatTargetHandle = _combatTargetHandle,
                PackageHandle = _packageHandle,
                DeadLookup = _deadLookup,
                PlacedRefStateLookup = _placedRefStateLookup,
                PlacedRefLookup = _placedRefLookup,
                TransformLookup = _transformLookup,
                BoundsLookup = _boundsLookup,
                PlayerLookup = _playerLookup,
                EquipmentLookup = _equipmentLookup,
                MeleeFollowInset = _meleeFollowInset,
                ApplyRequests = _applyRequests.AsParallelWriter(),
                ClearRequests = _clearRequests.AsParallelWriter(),
                VoiceRequests = _voiceRequests.AsParallelWriter(),
            }.ScheduleParallel(_query, systemState.Dependency);

            systemState.Dependency.Complete();
            ApplyRequests(ref systemState, ref content, scriptRuntimeEntity);
        }

        void ApplyRequests(ref SystemState systemState, ref RuntimeContentBlob content, Entity scriptRuntimeEntity)
        {
            EntityManager entityManager = systemState.EntityManager;
            for (int i = 0; i < _clearRequests.Length; i++)
                MorrowindScriptAiPackageUtility.ClearCombatPackages(entityManager, _clearRequests[i].Actor);

            for (int i = 0; i < _applyRequests.Length; i++)
            {
                CombatPackageApplyRequest request = _applyRequests[i];
                if (!MorrowindScriptAiPackageUtility.TryApplyCombatRequest(
                        ref content,
                        entityManager,
                        request.Actor,
                        request.ActorPlacedRefId,
                        request.Target,
                        request.TargetPlacedRefId,
                        request.TargetPosition,
                        request.FollowDistance))
                {
                    throw new InvalidOperationException($"[VVardenfell][CombatTarget] Failed to apply combat package for actor ref={request.ActorPlacedRefId}.");
                }
            }

            var combatVoiceRequests = entityManager.GetBuffer<MorrowindCombatVoiceResolveRequest>(scriptRuntimeEntity);
            var activeSays = entityManager.GetBuffer<MorrowindScriptActiveSay>(scriptRuntimeEntity, true);
            for (int i = 0; i < _voiceRequests.Length; i++)
            {
                CombatVoiceQueueRequest request = _voiceRequests[i];
                if (IsActorSayingOrPending(activeSays, combatVoiceRequests, request.Actor, request.PlacedRefId))
                    continue;

                combatVoiceRequests.Add(new MorrowindCombatVoiceResolveRequest
                {
                    TargetEntity = request.Actor,
                    TargetPlacedRefId = request.PlacedRefId,
                    Actor = request.ActorDef,
                    DialogueIdHash = AttackVoiceDialogueHash,
                    RandomState = request.RandomState,
                });
            }
        }

        static void EnsureCapacity<T>(ref NativeList<T> list, int capacity)
            where T : unmanaged
        {
            if (list.Capacity < capacity)
                list.Capacity = capacity;
        }

        static bool IsActorSayingOrPending(
            DynamicBuffer<MorrowindScriptActiveSay> activeSays,
            DynamicBuffer<MorrowindCombatVoiceResolveRequest> pendingRequests,
            Entity actor,
            uint placedRefId)
        {
            for (int i = 0; i < activeSays.Length; i++)
            {
                var active = activeSays[i];
                if (active.SourceEntity == actor || (placedRefId != 0u && active.SourcePlacedRefId == placedRefId))
                    return true;
            }

            for (int i = 0; i < pendingRequests.Length; i++)
            {
                var pending = pendingRequests[i];
                if (pending.TargetEntity == actor || (placedRefId != 0u && pending.TargetPlacedRefId == placedRefId))
                    return true;
            }

            return false;
        }

        static float3 FlatDelta(float3 target, float3 source)
        {
            float3 delta = target - source;
            delta.y = 0f;
            return delta;
        }

        struct CombatPackageClearRequest
        {
            public Entity Actor;
        }

        struct CombatPackageApplyRequest
        {
            public Entity Actor;
            public uint ActorPlacedRefId;
            public Entity Target;
            public uint TargetPlacedRefId;
            public float3 TargetPosition;
            public float FollowDistance;
        }

        struct CombatVoiceQueueRequest
        {
            public Entity Actor;
            public uint PlacedRefId;
            public ActorDefHandle ActorDef;
            public uint RandomState;
        }

        [BurstCompile]
        struct CombatTargetSelectionJob : IJobChunk
        {
            [ReadOnly] public BlobAssetReference<RuntimeContentBlob> Content;
            public uint VoiceSeed;
            [ReadOnly] public EntityTypeHandle EntityHandle;
            [ReadOnly] public ComponentTypeHandle<ActorSpawnSource> SourceHandle;
            [ReadOnly] public ComponentTypeHandle<ActorDead> DeadHandle;
            public ComponentTypeHandle<ActorActiveCombatTarget> ActiveTargetHandle;
            [ReadOnly] public ComponentTypeHandle<LocalTransform> TransformHandle;
            [ReadOnly] public ComponentTypeHandle<ActorLocalBounds> BoundsHandle;
            [ReadOnly] public ComponentTypeHandle<ActorAiState> AiStateHandle;
            public BufferTypeHandle<ActorCombatTarget> CombatTargetHandle;
            [ReadOnly] public BufferTypeHandle<ActorAiPackageRuntime> PackageHandle;
            [ReadOnly] public ComponentLookup<ActorDead> DeadLookup;
            [ReadOnly] public ComponentLookup<PlacedRefRuntimeState> PlacedRefStateLookup;
            [ReadOnly] public ComponentLookup<PlacedRefIdentity> PlacedRefLookup;
            [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
            [ReadOnly] public ComponentLookup<ActorLocalBounds> BoundsLookup;
            [ReadOnly] public ComponentLookup<PlayerCharacterComponent> PlayerLookup;
            [ReadOnly] public BufferLookup<ActorEquipmentSlot> EquipmentLookup;
            public float MeleeFollowInset;
            public NativeList<CombatPackageApplyRequest>.ParallelWriter ApplyRequests;
            public NativeList<CombatPackageClearRequest>.ParallelWriter ClearRequests;
            public NativeList<CombatVoiceQueueRequest>.ParallelWriter VoiceRequests;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
            {
                var entities = chunk.GetNativeArray(EntityHandle);
                var sources = chunk.GetNativeArray(ref SourceHandle);
                var activeTargets = chunk.GetNativeArray(ref ActiveTargetHandle);
                var transforms = chunk.GetNativeArray(ref TransformHandle);
                var bounds = chunk.GetNativeArray(ref BoundsHandle);
                var aiStates = chunk.GetNativeArray(ref AiStateHandle);
                var targets = chunk.GetBufferAccessor(ref CombatTargetHandle);
                var packages = chunk.GetBufferAccessor(ref PackageHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    Entity actor = entities[i];
                    DynamicBuffer<ActorCombatTarget> targetBuffer = targets[i];
                    bool wasInCombat = chunk.IsComponentEnabled(ref ActiveTargetHandle, i);

                    if (chunk.IsComponentEnabled(ref DeadHandle, i) || IsDisabled(actor))
                    {
                        ClearActiveCombat(actor, targetBuffer, ref activeTargets, chunk, i, wasInCombat);
                        continue;
                    }

                    RemoveInvalidTargets(targetBuffer);
                    if (targetBuffer.Length == 0)
                    {
                        ClearActiveCombat(actor, targetBuffer, ref activeTargets, chunk, i, wasInCombat);
                        continue;
                    }

                    int selectedIndex = SelectNearestTarget(transforms[i].Position, targetBuffer);
                    if (selectedIndex < 0)
                    {
                        ClearActiveCombat(actor, targetBuffer, ref activeTargets, chunk, i, wasInCombat);
                        continue;
                    }

                    ActorCombatTarget selected = targetBuffer[selectedIndex];
                    if (selectedIndex != 0)
                    {
                        targetBuffer.RemoveAt(selectedIndex);
                        targetBuffer.Insert(0, selected);
                    }

                    ActorActiveCombatTarget active = activeTargets[i];
                    bool changed = active.TargetEntity != selected.TargetEntity || active.TargetPlacedRefId != selected.TargetPlacedRefId;
                    active.TargetEntity = selected.TargetEntity;
                    active.TargetPlacedRefId = selected.TargetPlacedRefId;
                    activeTargets[i] = active;
                    chunk.SetComponentEnabled(ref ActiveTargetHandle, i, true);

                    if (!wasInCombat)
                        TryQueueAttackVoice(actor, sources[i], selected.Sequence);

                    float followDistance = ResolveCombatFollowDistance(actor, sources[i], bounds[i], transforms[i], selected.TargetEntity);
                    if (changed || !HasCurrentCombatPackage(packages[i], aiStates[i], selected, followDistance))
                    {
                        ApplyRequests.AddNoResize(new CombatPackageApplyRequest
                        {
                            Actor = actor,
                            ActorPlacedRefId = PlacedRefId(actor),
                            Target = selected.TargetEntity,
                            TargetPlacedRefId = selected.TargetPlacedRefId,
                            TargetPosition = TransformLookup[selected.TargetEntity].Position,
                            FollowDistance = followDistance,
                        });
                    }
                }
            }

            void ClearActiveCombat(
                Entity actor,
                DynamicBuffer<ActorCombatTarget> targets,
                ref NativeArray<ActorActiveCombatTarget> activeTargets,
                ArchetypeChunk chunk,
                int index,
                bool wasInCombat)
            {
                if (targets.Length > 0)
                    targets.Clear();

                activeTargets[index] = default;
                chunk.SetComponentEnabled(ref ActiveTargetHandle, index, false);
                if (wasInCombat)
                    ClearRequests.AddNoResize(new CombatPackageClearRequest { Actor = actor });
            }

            void RemoveInvalidTargets(DynamicBuffer<ActorCombatTarget> targets)
            {
                for (int i = targets.Length - 1; i >= 0; i--)
                {
                    Entity target = targets[i].TargetEntity;
                    if (target == Entity.Null
                        || !TransformLookup.HasComponent(target)
                        || IsDead(target)
                        || IsDisabled(target))
                    {
                        targets.RemoveAt(i);
                    }
                }
            }

            int SelectNearestTarget(float3 actorPosition, DynamicBuffer<ActorCombatTarget> targets)
            {
                int bestIndex = -1;
                float bestDistanceSq = float.PositiveInfinity;
                for (int i = 0; i < targets.Length; i++)
                {
                    Entity target = targets[i].TargetEntity;
                    if (target == Entity.Null || !TransformLookup.HasComponent(target))
                        continue;

                    float distanceSq = math.lengthsq(FlatDelta(TransformLookup[target].Position, actorPosition));
                    if (distanceSq < bestDistanceSq)
                    {
                        bestDistanceSq = distanceSq;
                        bestIndex = i;
                    }
                }

                return bestIndex;
            }

            static bool HasCurrentCombatPackage(DynamicBuffer<ActorAiPackageRuntime> packages, in ActorAiState aiState, in ActorCombatTarget selected, float followDistance)
            {
                if ((uint)aiState.CurrentPackageIndex >= (uint)packages.Length)
                    return false;

                var package = packages[aiState.CurrentPackageIndex];
                return package.Type == (byte)ActorAiRuntimePackageType.Combat
                       && package.FollowTargetEntity == selected.TargetEntity
                       && package.FollowTargetPlacedRefId == selected.TargetPlacedRefId
                       && math.abs(package.FollowDistance - followDistance) <= 0.01f;
            }

            float ResolveCombatFollowDistance(
                Entity actor,
                in ActorSpawnSource source,
                in ActorLocalBounds actorBounds,
                in LocalTransform actorTransform,
                Entity target)
            {
                float reach;
                if (EquipmentLookup.HasBuffer(actor))
                {
                    var equipment = EquipmentLookup[actor];
                    ref RuntimeContentBlob content = ref Content.Value;
                    int weaponType = ActorWeaponAnimationUtility.ResolveEquippedWeaponType(ref content, equipment, out var weaponContent);
                    if (!ActorWeaponAnimationUtility.IsSupportedMelee(weaponType))
                        return MorrowindCombatTargetUtility.CombatPursuitDistanceMeters;

                    MorrowindMeleeCombatMechanics.ResolveWeaponEquipment(
                        ref content,
                        weaponContent,
                        out bool hasWeapon,
                        out _,
                        out var weapon,
                        out _);
                    reach = MorrowindMeleeCombatMechanics.ComputeMeleeReach(ref content, hasWeapon, weapon);
                    return math.max(0.5f, MorrowindMeleeCombatMechanics.ComputeMeleeApproachReach(reach, MeleeFollowInset) + ResolveActorRadius(actorBounds, actorTransform) + ResolveTargetRadius(target));
                }

                if (ResolveActorKind(source) == ActorDefKind.Creature)
                {
                    ref RuntimeContentBlob content = ref Content.Value;
                    reach = MorrowindMeleeCombatMechanics.ComputeMeleeReach(ref content, false, default);
                    return math.max(0.5f, MorrowindMeleeCombatMechanics.ComputeMeleeApproachReach(reach, MeleeFollowInset) + ResolveActorRadius(actorBounds, actorTransform) + ResolveTargetRadius(target));
                }

                throw new InvalidOperationException($"[VVardenfell][CombatTarget] NPC ref=0x{PlacedRefId(actor):X8} has no ActorEquipmentSlot buffer.");
            }

            static float ResolveActorRadius(in ActorLocalBounds bounds, in LocalTransform transform)
                => math.max(bounds.Extents.x, bounds.Extents.z) * math.max(0.01f, transform.Scale);

            float ResolveTargetRadius(Entity target)
            {
                if (BoundsLookup.HasComponent(target))
                {
                    ActorLocalBounds bounds = BoundsLookup[target];
                    LocalTransform transform = TransformLookup[target];
                    return math.max(bounds.Extents.x, bounds.Extents.z) * math.max(0.01f, transform.Scale);
                }

                if (PlayerLookup.HasComponent(target))
                    return math.max(0.01f, PlayerLookup[target].Radius);

                throw new InvalidOperationException($"[VVardenfell][CombatTarget] Target entity={target.Index}:{target.Version} has no ActorLocalBounds or PlayerCharacterComponent.");
            }

            void TryQueueAttackVoice(Entity actor, in ActorSpawnSource source, uint targetSequence)
            {
                if (ResolveActorKind(source) != ActorDefKind.Npc)
                    return;

                uint placedRefId = PlacedRefId(actor);
                uint seed = math.hash(new uint4(VoiceSeed, placedRefId, targetSequence, (uint)(actor.Index + actor.Version + 1)));
                VoiceRequests.AddNoResize(new CombatVoiceQueueRequest
                {
                    Actor = actor,
                    PlacedRefId = placedRefId,
                    ActorDef = source.Definition,
                    RandomState = seed == 0u ? 1u : seed,
                });
            }

            ActorDefKind ResolveActorKind(in ActorSpawnSource source)
            {
                if (!source.Definition.IsValid)
                    throw new InvalidOperationException("[VVardenfell][CombatTarget] Actor has invalid ActorSpawnSource.");
                if (!Content.IsCreated || (uint)source.Definition.Index >= (uint)Content.Value.Actors.Length)
                    throw new InvalidOperationException("[VVardenfell][CombatTarget] Actor has out-of-range ActorSpawnSource.");
                return Content.Value.Actors[source.Definition.Index].Kind;
            }

            bool IsDead(Entity actor)
                => DeadLookup.HasComponent(actor) && DeadLookup.IsComponentEnabled(actor);

            bool IsDisabled(Entity actor)
                => PlacedRefStateLookup.HasComponent(actor) && PlacedRefStateLookup[actor].Disabled != 0;

            uint PlacedRefId(Entity entity)
                => PlacedRefLookup.HasComponent(entity) ? PlacedRefLookup[entity].Value : 0u;
        }
    }
}
