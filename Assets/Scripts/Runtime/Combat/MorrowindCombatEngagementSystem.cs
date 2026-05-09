using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.AI;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(MorrowindDamageSystemGroup))]
    [UpdateBefore(typeof(MorrowindCombatTargetSelectionSystem))]
    public partial struct MorrowindCombatEngagementSystem : ISystem
    {
        const float ActorsProcessingRangeMw = 7168f;
        const int MaxEngagementCandidates = 8;
        const uint DefaultRandomSeed = 0x6E624EB7u;

        EntityQuery _actorQuery;
        NativeList<CombatEngagementStartRequest> _startRequests;
        NativeList<MorrowindFactionReactionOverride> _factionReactionOverrides;

        EntityTypeHandle _entityHandle;
        ComponentTypeHandle<LocalTransform> _transformHandle;
        ComponentTypeHandle<ActorAiState> _aiStateHandle;

        ComponentLookup<ActorVitalSet> _vitalLookup;
        ComponentLookup<ActorDead> _deadLookup;
        ComponentLookup<LocalTransform> _transformLookup;
        ComponentLookup<PlacedRefRuntimeState> _placedRefRuntimeLookup;
        ComponentLookup<PlacedRefIdentity> _placedRefLookup;
        ComponentLookup<ActorActiveCombatTarget> _activeCombatTargetLookup;
        ComponentLookup<ActorAiSettingsState> _aiSettingsLookup;
        ComponentLookup<ActorDispositionState> _dispositionLookup;
        ComponentLookup<ActorAttributeSet> _attributeLookup;
        ComponentLookup<ActorSkillSet> _skillLookup;
        ComponentLookup<ActorDerivedMovementStats> _derivedMovementLookup;
        ComponentLookup<MorrowindMovementState> _movementStateLookup;
        ComponentLookup<PlayerTag> _playerLookup;
        ComponentLookup<BattleSimulatorTeam> _battleTeamLookup;

        BufferLookup<ActorCombatTarget> _combatTargetLookup;
        BufferLookup<ActorFactionMembership> _actorFactionLookup;
        BufferLookup<PlayerFactionMembership> _playerFactionLookup;
        BufferLookup<ActorActiveMagicEffect> _activeMagicEffectLookup;
        BufferLookup<ActorEquipmentSlot> _equipmentLookup;

        short _blindEffectId;
        short _calmHumanoidEffectId;
        short _chameleonEffectId;
        short _invisibilityEffectId;

        public void OnCreate(ref SystemState state)
        {
            _actorQuery = state.GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ActorVitalSet>(),
                    ComponentType.ReadOnly<ActorDead>(),
                    ComponentType.ReadWrite<ActorAiState>(),
                    ComponentType.ReadOnly<ActorAiSettingsState>(),
                    ComponentType.ReadOnly<LocalTransform>(),
                    ComponentType.ReadOnly<ActorActiveMagicEffect>(),
                    ComponentType.ReadOnly<ActorCombatTarget>(),
                    ComponentType.ReadOnly<ActorActiveCombatTarget>(),
                    ComponentType.ReadOnly<PhysicsCollider>(),
                    ComponentType.ReadOnly<ActorSpawnSource>(),
                    ComponentType.ReadOnly<PlacedRefIdentity>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<PlayerTag>(),
                },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });
            _entityHandle = state.GetEntityTypeHandle();
            _transformHandle = state.GetComponentTypeHandle<LocalTransform>(isReadOnly: true);
            _aiStateHandle = state.GetComponentTypeHandle<ActorAiState>(isReadOnly: false);

            _vitalLookup = state.GetComponentLookup<ActorVitalSet>(isReadOnly: true);
            _deadLookup = state.GetComponentLookup<ActorDead>(isReadOnly: true);
            _transformLookup = state.GetComponentLookup<LocalTransform>(isReadOnly: true);
            _placedRefRuntimeLookup = state.GetComponentLookup<PlacedRefRuntimeState>(isReadOnly: true);
            _placedRefLookup = state.GetComponentLookup<PlacedRefIdentity>(isReadOnly: true);
            _activeCombatTargetLookup = state.GetComponentLookup<ActorActiveCombatTarget>(isReadOnly: true);
            _aiSettingsLookup = state.GetComponentLookup<ActorAiSettingsState>(isReadOnly: true);
            _dispositionLookup = state.GetComponentLookup<ActorDispositionState>(isReadOnly: true);
            _attributeLookup = state.GetComponentLookup<ActorAttributeSet>(isReadOnly: true);
            _skillLookup = state.GetComponentLookup<ActorSkillSet>(isReadOnly: true);
            _derivedMovementLookup = state.GetComponentLookup<ActorDerivedMovementStats>(isReadOnly: true);
            _movementStateLookup = state.GetComponentLookup<MorrowindMovementState>(isReadOnly: true);
            _playerLookup = state.GetComponentLookup<PlayerTag>(isReadOnly: true);
            _battleTeamLookup = state.GetComponentLookup<BattleSimulatorTeam>(isReadOnly: true);

            _combatTargetLookup = state.GetBufferLookup<ActorCombatTarget>(isReadOnly: true);
            _actorFactionLookup = state.GetBufferLookup<ActorFactionMembership>(isReadOnly: true);
            _playerFactionLookup = state.GetBufferLookup<PlayerFactionMembership>(isReadOnly: true);
            _activeMagicEffectLookup = state.GetBufferLookup<ActorActiveMagicEffect>(isReadOnly: true);
            _equipmentLookup = state.GetBufferLookup<ActorEquipmentSlot>(isReadOnly: true);

            _startRequests = new NativeList<CombatEngagementStartRequest>(Allocator.Persistent);
            _factionReactionOverrides = new NativeList<MorrowindFactionReactionOverride>(Allocator.Persistent);
            _blindEffectId = RequireEffectId("sEffectBlind");
            _calmHumanoidEffectId = RequireEffectId("sEffectCalmHumanoid");
            _chameleonEffectId = RequireEffectId("sEffectChameleon");
            _invisibilityEffectId = RequireEffectId("sEffectInvisibility");

            state.RequireForUpdate(_actorQuery);
            state.RequireForUpdate<RuntimeContentBlobReference>();
            state.RequireForUpdate<MorrowindCombatRuntimeState>();
            state.RequireForUpdate<MorrowindDialogueState>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
        }

        public void OnDestroy(ref SystemState state)
        {
            if (_startRequests.IsCreated)
                _startRequests.Dispose();
            if (_factionReactionOverrides.IsCreated)
                _factionReactionOverrides.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][CombatEngage] requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;

            int actorCount = _actorQuery.CalculateEntityCount();
            if (actorCount == 0)
                return;

            state.Dependency.Complete();

            EnsureCapacity(ref _startRequests, actorCount * MaxEngagementCandidates);
            _startRequests.Clear();
            CopyFactionReactionOverrides(ref state);

            _entityHandle.Update(ref state);
            _transformHandle.Update(ref state);
            _aiStateHandle.Update(ref state);

            _vitalLookup.Update(ref state);
            _deadLookup.Update(ref state);
            _transformLookup.Update(ref state);
            _placedRefRuntimeLookup.Update(ref state);
            _placedRefLookup.Update(ref state);
            _activeCombatTargetLookup.Update(ref state);
            _aiSettingsLookup.Update(ref state);
            _dispositionLookup.Update(ref state);
            _attributeLookup.Update(ref state);
            _skillLookup.Update(ref state);
            _derivedMovementLookup.Update(ref state);
            _movementStateLookup.Update(ref state);
            _playerLookup.Update(ref state);
            _battleTeamLookup.Update(ref state);

            _combatTargetLookup.Update(ref state);
            _actorFactionLookup.Update(ref state);
            _playerFactionLookup.Update(ref state);
            _activeMagicEffectLookup.Update(ref state);
            _equipmentLookup.Update(ref state);

            ref var combatState = ref SystemAPI.GetSingletonRW<MorrowindCombatRuntimeState>().ValueRW;
            uint randomSeed = combatState.RandomState == 0u ? DefaultRandomSeed : combatState.RandomState;
            float elapsedTime = (float)SystemAPI.Time.ElapsedTime;
            float range = ActorsProcessingRangeMw * WorldScale.MwUnitsToMeters;

            var job = new CombatEngagementJob
            {
                Content = contentBlobReference.Blob,
                CollisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld,
                EntityHandle = _entityHandle,
                TransformHandle = _transformHandle,
                AiStateHandle = _aiStateHandle,
                VitalLookup = _vitalLookup,
                DeadLookup = _deadLookup,
                TransformLookup = _transformLookup,
                PlacedRefRuntimeLookup = _placedRefRuntimeLookup,
                PlacedRefLookup = _placedRefLookup,
                ActiveCombatTargetLookup = _activeCombatTargetLookup,
                AiSettingsLookup = _aiSettingsLookup,
                DispositionLookup = _dispositionLookup,
                AttributeLookup = _attributeLookup,
                SkillLookup = _skillLookup,
                DerivedMovementLookup = _derivedMovementLookup,
                MovementStateLookup = _movementStateLookup,
                PlayerLookup = _playerLookup,
                BattleTeamLookup = _battleTeamLookup,
                CombatTargetLookup = _combatTargetLookup,
                ActorFactionLookup = _actorFactionLookup,
                PlayerFactionLookup = _playerFactionLookup,
                ActiveMagicEffectLookup = _activeMagicEffectLookup,
                EquipmentLookup = _equipmentLookup,
                FactionReactionOverrides = _factionReactionOverrides,
                StartRequests = _startRequests.AsParallelWriter(),
                ElapsedTime = elapsedTime,
                ProcessingRange = range,
                RandomSeed = randomSeed,
                FightDistanceBase = RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref content, RuntimeContentKnownHashes.iFightDistanceBase),
                FightDistanceMultiplier = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fFightDistanceMultiplier),
                FightDispositionMultiplier = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fFightDispMult),
                SneakSkillMultiplier = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fSneakSkillMult),
                SneakBootMultiplier = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fSneakBootMult),
                SneakDistanceBase = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fSneakDistanceBase),
                SneakDistanceMultiplier = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fSneakDistanceMultiplier),
                SneakNoViewMultiplier = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fSneakNoViewMult),
                SneakViewMultiplier = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fSneakViewMult),
                BlindEffectId = _blindEffectId,
                CalmHumanoidEffectId = _calmHumanoidEffectId,
                ChameleonEffectId = _chameleonEffectId,
                InvisibilityEffectId = _invisibilityEffectId,
            };

            state.Dependency = job.ScheduleParallel(_actorQuery, state.Dependency);
            state.Dependency.Complete();
            state.Dependency = default;

            for (int i = 0; i < _startRequests.Length; i++)
            {
                var request = _startRequests[i];
                if (!MorrowindCombatTargetUtility.TryStartCombat(
                        ref content,
                        state.EntityManager,
                        request.Actor,
                        request.ActorPlacedRefId,
                        request.Target,
                        request.TargetPlacedRefId))
                {
                    throw new InvalidOperationException($"[VVardenfell][CombatEngage] Failed to start combat actor=0x{request.ActorPlacedRefId:X8} target=0x{request.TargetPlacedRefId:X8}.");
                }
            }

            combatState.RandomState = NextSeed(randomSeed);
        }

        void CopyFactionReactionOverrides(ref SystemState state)
        {
            Entity dialogueEntity = SystemAPI.GetSingletonEntity<MorrowindDialogueState>();
            var overrides = state.EntityManager.GetBuffer<MorrowindFactionReactionOverride>(dialogueEntity, true);
            EnsureCapacity(ref _factionReactionOverrides, overrides.Length);
            _factionReactionOverrides.Clear();
            for (int i = 0; i < overrides.Length; i++)
                _factionReactionOverrides.AddNoResize(overrides[i]);
        }

        static void EnsureCapacity<T>(ref NativeList<T> list, int capacity)
            where T : unmanaged
        {
            if (list.Capacity < capacity)
                list.Capacity = capacity;
        }

        static short RequireEffectId(string gmstId)
        {
            if (!MorrowindMagicEffectTextUtility.TryResolveEffectId(gmstId, out short effectId))
                throw new InvalidOperationException($"[VVardenfell][CombatEngage] Required magic effect GMST '{gmstId}' is not mapped.");
            return effectId;
        }

        static uint NextSeed(uint seed)
        {
            uint value = seed == 0u ? DefaultRandomSeed : seed;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            return value == 0u ? DefaultRandomSeed : value;
        }

        struct CombatEngagementStartRequest
        {
            public Entity Actor;
            public Entity Target;
            public uint ActorPlacedRefId;
            public uint TargetPlacedRefId;
        }

        struct CombatCandidate
        {
            public Entity Entity;
            public uint PlacedRefId;
            public float DistanceSq;
        }

        [BurstCompile]
        struct CombatEngagementJob : IJobChunk
        {
            [ReadOnly] public BlobAssetReference<RuntimeContentBlob> Content;
            [ReadOnly] public CollisionWorld CollisionWorld;
            [ReadOnly] public EntityTypeHandle EntityHandle;
            [ReadOnly] public ComponentTypeHandle<LocalTransform> TransformHandle;
            public ComponentTypeHandle<ActorAiState> AiStateHandle;
            [ReadOnly] public ComponentLookup<ActorVitalSet> VitalLookup;
            [ReadOnly] public ComponentLookup<ActorDead> DeadLookup;
            [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
            [ReadOnly] public ComponentLookup<PlacedRefRuntimeState> PlacedRefRuntimeLookup;
            [ReadOnly] public ComponentLookup<PlacedRefIdentity> PlacedRefLookup;
            [ReadOnly] public ComponentLookup<ActorActiveCombatTarget> ActiveCombatTargetLookup;
            [ReadOnly] public ComponentLookup<ActorAiSettingsState> AiSettingsLookup;
            [ReadOnly] public ComponentLookup<ActorDispositionState> DispositionLookup;
            [ReadOnly] public ComponentLookup<ActorAttributeSet> AttributeLookup;
            [ReadOnly] public ComponentLookup<ActorSkillSet> SkillLookup;
            [ReadOnly] public ComponentLookup<ActorDerivedMovementStats> DerivedMovementLookup;
            [ReadOnly] public ComponentLookup<MorrowindMovementState> MovementStateLookup;
            [ReadOnly] public ComponentLookup<PlayerTag> PlayerLookup;
            [ReadOnly] public ComponentLookup<BattleSimulatorTeam> BattleTeamLookup;
            [ReadOnly] public BufferLookup<ActorCombatTarget> CombatTargetLookup;
            [ReadOnly] public BufferLookup<ActorFactionMembership> ActorFactionLookup;
            [ReadOnly] public BufferLookup<PlayerFactionMembership> PlayerFactionLookup;
            [ReadOnly] public BufferLookup<ActorActiveMagicEffect> ActiveMagicEffectLookup;
            [ReadOnly] public BufferLookup<ActorEquipmentSlot> EquipmentLookup;
            [ReadOnly] public NativeList<MorrowindFactionReactionOverride> FactionReactionOverrides;
            public NativeList<CombatEngagementStartRequest>.ParallelWriter StartRequests;
            public float ElapsedTime;
            public float ProcessingRange;
            public uint RandomSeed;
            public int FightDistanceBase;
            public float FightDistanceMultiplier;
            public float FightDispositionMultiplier;
            public float SneakSkillMultiplier;
            public float SneakBootMultiplier;
            public float SneakDistanceBase;
            public float SneakDistanceMultiplier;
            public float SneakNoViewMultiplier;
            public float SneakViewMultiplier;
            public short BlindEffectId;
            public short CalmHumanoidEffectId;
            public short ChameleonEffectId;
            public short InvisibilityEffectId;

            [BurstCompile]
            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
            {
                var entities = chunk.GetEntityDataPtrRO(EntityHandle);
                var transforms = chunk.GetComponentDataPtrRO(ref TransformHandle);
                var aiStates = chunk.GetComponentDataPtrRW(ref AiStateHandle);
                

                for (int i = 0; i < chunk.Count; i++)
                {
                    var actor = entities + i;
                    var transform = transforms + i;
                    var aiState = aiStates + i;
                    if (IsDeadOrDisabled(*actor) || HasLiveActiveCombatTarget(*actor))
                        continue;

                    uint actorSeed = Hash(RandomSeed ^ (uint)actor->Index * 0x9E3779B9u ^ (uint)actor->Version * 0x85EBCA6Bu);
                    if (aiState->NextCombatEngagementTime <= 0f)
                    {
                        aiState->NextCombatEngagementTime = ElapsedTime + RandomFloat01(actorSeed) * 0.25f;
                        continue;
                    }

                    if (ElapsedTime < aiState->NextCombatEngagementTime)
                        continue;

                    aiState->NextCombatEngagementTime = ElapsedTime + 1f + RandomFloat01(Hash(actorSeed ^ 0xD1B54A35u)) * 0.5f - 0.25f;

                    var collector = CreateCollector(*actor, transform->Position);
                    CollisionWorld.OverlapSphereCustom(
                        transform->Position,
                        ProcessingRange,
                        ref collector,
                        InteractionCollisionLayers.ActorBodyQueryFilter);

                    bool startedAnyCombat = false;
                    for (int candidateIndex = 0; candidateIndex < collector.Candidates.Length; candidateIndex++)
                    {
                        CombatCandidate candidate = collector.Candidates[candidateIndex];
                        Entity target = candidate.Entity;
                        if (!TransformLookup.HasComponent(target))
                            continue;
                        LocalTransform targetTransform = TransformLookup[target];
                        if (!HasLineOfSight(transform->Position, targetTransform.Position))
                            continue;
                        uint awarenessRoll = Hash(actorSeed ^ (uint)target.Index * 0xC2B2AE35u ^ (uint)target.Version * 0x27D4EB2Fu) % 100u;
                        if (!AwarenessCheck(target, *actor, targetTransform, *transform, awarenessRoll))
                            continue;

                        StartRequests.AddNoResize(new CombatEngagementStartRequest
                        {
                            Actor = *actor,
                            Target = target,
                            ActorPlacedRefId = PlacedRefId(*actor),
                            TargetPlacedRefId = candidate.PlacedRefId,
                        });
                        startedAnyCombat = true;
                    }

                    if (startedAnyCombat)
                        aiState->NextCombatEngagementTime = ElapsedTime + 1f + RandomFloat01(Hash(actorSeed ^ 0x91E10DA5u)) * 0.5f;
                }
            }

            NearestCombatCandidateCollector CreateCollector(Entity actor, float3 actorPosition)
            {
                return new NearestCombatCandidateCollector
                {
                    Source = actor,
                    SourcePosition = actorPosition,
                    MaxDistance = ProcessingRange,
                    Content = Content,
                    VitalLookup = VitalLookup,
                    DeadLookup = DeadLookup,
                    TransformLookup = TransformLookup,
                    PlacedRefRuntimeLookup = PlacedRefRuntimeLookup,
                    PlacedRefLookup = PlacedRefLookup,
                    AiSettingsLookup = AiSettingsLookup,
                    DispositionLookup = DispositionLookup,
                    BattleTeamLookup = BattleTeamLookup,
                    CombatTargetLookup = CombatTargetLookup,
                    ActorFactionLookup = ActorFactionLookup,
                    PlayerFactionLookup = PlayerFactionLookup,
                    ActiveMagicEffectLookup = ActiveMagicEffectLookup,
                    FactionReactionOverrides = FactionReactionOverrides,
                    FightDistanceBase = FightDistanceBase,
                    FightDistanceMultiplier = FightDistanceMultiplier,
                    FightDispositionMultiplier = FightDispositionMultiplier,
                    CalmHumanoidEffectId = CalmHumanoidEffectId,
                    Candidates = default,
                };
            }

            bool HasLiveActiveCombatTarget(Entity actor)
            {
                if (!ActiveCombatTargetLookup.HasComponent(actor) || !ActiveCombatTargetLookup.IsComponentEnabled(actor))
                    return false;
                Entity target = ActiveCombatTargetLookup[actor].TargetEntity;
                return target != Entity.Null && IsCombatTargetCapable(target);
            }

            bool IsCombatTargetCapable(Entity entity)
                => entity != Entity.Null
                   && VitalLookup.HasComponent(entity)
                   && TransformLookup.HasComponent(entity)
                   && ActiveMagicEffectLookup.HasBuffer(entity)
                   && !IsDeadOrDisabled(entity);

            bool IsDeadOrDisabled(Entity entity)
            {
                if (!VitalLookup.HasComponent(entity))
                    return true;
                if (DeadLookup.HasComponent(entity) && DeadLookup.IsComponentEnabled(entity))
                    return true;
                if (PlacedRefRuntimeLookup.HasComponent(entity) && PlacedRefRuntimeLookup[entity].Disabled != 0)
                    return true;
                return false;
            }

            bool HasLineOfSight(float3 sourcePosition, float3 targetPosition)
            {
                var input = new RaycastInput
                {
                    Start = Eye(sourcePosition),
                    End = Eye(targetPosition),
                    Filter = InteractionCollisionLayers.LineOfSightQueryFilter,
                };
                return !CollisionWorld.CastRay(input);
            }

            bool AwarenessCheck(Entity target, Entity observer, in LocalTransform targetTransform, in LocalTransform observerTransform, uint roll0To99)
            {
                if (!AttributeLookup.HasComponent(target)
                    || !SkillLookup.HasComponent(target)
                    || !DerivedMovementLookup.HasComponent(target)
                    || !MovementStateLookup.HasComponent(target)
                    || !AttributeLookup.HasComponent(observer)
                    || !SkillLookup.HasComponent(observer)
                    || !DerivedMovementLookup.HasComponent(observer)
                    || !ActiveMagicEffectLookup.HasBuffer(target)
                    || !ActiveMagicEffectLookup.HasBuffer(observer))
                {
                    return false;
                }

                var targetAttributes = AttributeLookup[target];
                var targetSkills = SkillLookup[target];
                var targetDerived = DerivedMovementLookup[target];
                var targetEffects = ActiveMagicEffectLookup[target];
                var movement = MovementStateLookup[target];

                float sneakTerm = 0f;
                if (movement.SneakHeld)
                {
                    float bootWeight = 0f;
                    if (movement.Grounded)
                        bootWeight = ResolveFootwearWeight(target);

                    sneakTerm =
                        SneakSkillMultiplier * targetSkills.Sneak
                        + 0.2f * targetAttributes.Agility
                        + 0.1f * targetAttributes.Luck
                        + bootWeight * SneakBootMultiplier;
                }

                float distanceMw = math.distance(targetTransform.Position, observerTransform.Position) / WorldScale.MwUnitsToMeters;
                float distanceTerm = SneakDistanceBase + SneakDistanceMultiplier * distanceMw;
                float x = sneakTerm * distanceTerm * targetDerived.FatigueTerm
                          + SumEffectMagnitude(targetEffects, ChameleonEffectId);
                if (SumEffectMagnitude(targetEffects, InvisibilityEffectId) > 0f)
                    x += 100f;

                var observerAttributes = AttributeLookup[observer];
                var observerSkills = SkillLookup[observer];
                var observerDerived = DerivedMovementLookup[observer];
                var observerEffects = ActiveMagicEffectLookup[observer];

                float observerTerm = observerSkills.Sneak
                                     + 0.2f * observerAttributes.Agility
                                     + 0.1f * observerAttributes.Luck
                                     - SumEffectMagnitude(observerEffects, BlindEffectId);

                float3 toTarget = targetTransform.Position - observerTransform.Position;
                toTarget.y = 0f;
                float3 observerForward = math.normalizesafe(math.rotate(observerTransform.Rotation, new float3(0f, 0f, 1f)), new float3(0f, 0f, 1f));
                observerForward.y = 0f;
                observerForward = math.normalizesafe(observerForward, new float3(0f, 0f, 1f));
                bool targetBehindObserver = math.lengthsq(toTarget) > 0.0001f
                                            && math.dot(observerForward, math.normalize(toTarget)) < 0f;

                float viewMultiplier = targetBehindObserver ? SneakNoViewMultiplier : SneakViewMultiplier;
                float y = observerTerm * observerDerived.FatigueTerm * viewMultiplier;
                return roll0To99 >= x - y;
            }

            float ResolveFootwearWeight(Entity target)
            {
                if (!EquipmentLookup.HasBuffer(target))
                    return 0f;

                DynamicBuffer<ActorEquipmentSlot> equipment = EquipmentLookup[target];
                if (TryGetEquipmentInSlot(equipment, ItemEquipmentSlot.Boots, out var boots))
                    return ResolveCarryWeight(boots.Content);
                if (TryGetEquipmentInSlot(equipment, ItemEquipmentSlot.Shoes, out var shoes))
                    return ResolveCarryWeight(shoes.Content);
                return 0f;
            }

            float ResolveCarryWeight(ContentReference content)
            {
                if (!Content.IsCreated)
                    return 0f;

                ref RuntimeContentBlob contentBlob = ref Content.Value;
                if (content.Kind == ContentReferenceKind.Item
                    && (uint)(content.HandleValue - 1) < (uint)contentBlob.Items.Length)
                {
                    return contentBlob.Items[content.HandleValue - 1].Float0;
                }

                if (content.Kind == ContentReferenceKind.Light
                    && (uint)(content.HandleValue - 1) < (uint)contentBlob.Lights.Length)
                {
                    return contentBlob.Lights[content.HandleValue - 1].Weight;
                }

                return 0f;
            }

            static bool TryGetEquipmentInSlot(DynamicBuffer<ActorEquipmentSlot> equipment, ItemEquipmentSlot target, out ActorEquipmentSlot result)
            {
                for (int i = 0; i < equipment.Length; i++)
                {
                    var slot = equipment[i];
                    if (slot.Slot == target)
                    {
                        result = slot;
                        return true;
                    }
                }

                result = default;
                return false;
            }

            uint PlacedRefId(Entity entity)
                => PlacedRefLookup.HasComponent(entity) ? PlacedRefLookup[entity].Value : 0u;
        }

        [BurstCompile]
        struct NearestCombatCandidateCollector : ICollector<DistanceHit>
        {
            public Entity Source;
            public float3 SourcePosition;
            public float MaxDistance;
            [ReadOnly] public BlobAssetReference<RuntimeContentBlob> Content;
            [ReadOnly] public ComponentLookup<ActorVitalSet> VitalLookup;
            [ReadOnly] public ComponentLookup<ActorDead> DeadLookup;
            [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
            [ReadOnly] public ComponentLookup<PlacedRefRuntimeState> PlacedRefRuntimeLookup;
            [ReadOnly] public ComponentLookup<PlacedRefIdentity> PlacedRefLookup;
            [ReadOnly] public ComponentLookup<ActorAiSettingsState> AiSettingsLookup;
            [ReadOnly] public ComponentLookup<ActorDispositionState> DispositionLookup;
            [ReadOnly] public ComponentLookup<BattleSimulatorTeam> BattleTeamLookup;
            [ReadOnly] public BufferLookup<ActorCombatTarget> CombatTargetLookup;
            [ReadOnly] public BufferLookup<ActorFactionMembership> ActorFactionLookup;
            [ReadOnly] public BufferLookup<PlayerFactionMembership> PlayerFactionLookup;
            [ReadOnly] public BufferLookup<ActorActiveMagicEffect> ActiveMagicEffectLookup;
            [ReadOnly] public NativeList<MorrowindFactionReactionOverride> FactionReactionOverrides;
            public int FightDistanceBase;
            public float FightDistanceMultiplier;
            public float FightDispositionMultiplier;
            public short CalmHumanoidEffectId;
            public FixedList512Bytes<CombatCandidate> Candidates;

            public bool EarlyOutOnFirstHit => false;
            public float MaxFraction => MaxDistance;
            public int NumHits => Candidates.Length;

            public bool AddHit(DistanceHit hit)
            {
                Entity target = hit.Entity;
                if (target == Source || !IsCombatTargetCapable(target))
                    return false;

                float3 targetPosition = TransformLookup[target].Position;
                float distanceSq = math.lengthsq(FlatDelta(targetPosition, SourcePosition));
                float maxDistanceSq = MaxDistance * MaxDistance;
                if (distanceSq > maxDistanceSq)
                    return false;

                bool battleRelation = TryResolveBattleRelation(Source, target, out bool sameBattleTeam, out bool opposingBattleTeam);
                if (sameBattleTeam)
                    return false;

                if (IsAlreadyInCombatWith(target)
                    || (!battleRelation && ShareJoinedFaction(Source, target))
                    || (!opposingBattleTeam && !IsAggressive(Source, target, SourcePosition, targetPosition)))
                {
                    return false;
                }

                InsertCandidate(new CombatCandidate
                {
                    Entity = target,
                    PlacedRefId = PlacedRefLookup.HasComponent(target) ? PlacedRefLookup[target].Value : 0u,
                    DistanceSq = distanceSq,
                });
                return true;
            }

            bool IsCombatTargetCapable(Entity entity)
                => entity != Entity.Null
                   && VitalLookup.HasComponent(entity)
                   && TransformLookup.HasComponent(entity)
                   && ActiveMagicEffectLookup.HasBuffer(entity)
                   && !IsDeadOrDisabled(entity);

            bool IsDeadOrDisabled(Entity entity)
            {
                if (DeadLookup.HasComponent(entity) && DeadLookup.IsComponentEnabled(entity))
                    return true;
                if (PlacedRefRuntimeLookup.HasComponent(entity) && PlacedRefRuntimeLookup[entity].Disabled != 0)
                    return true;
                return false;
            }

            bool IsAlreadyInCombatWith(Entity target)
            {
                if (!CombatTargetLookup.HasBuffer(Source))
                    return false;
                DynamicBuffer<ActorCombatTarget> targets = CombatTargetLookup[Source];
                for (int i = 0; i < targets.Length; i++)
                {
                    if (targets[i].TargetEntity == target)
                        return true;
                    if (targets[i].TargetPlacedRefId != 0u
                        && PlacedRefLookup.HasComponent(target)
                        && targets[i].TargetPlacedRefId == PlacedRefLookup[target].Value)
                    {
                        return true;
                    }
                }

                return false;
            }

            bool TryResolveBattleRelation(Entity actor, Entity target, out bool sameTeam, out bool opposingTeam)
            {
                sameTeam = false;
                opposingTeam = false;
                if (!BattleTeamLookup.HasComponent(actor) || !BattleTeamLookup.HasComponent(target))
                    return false;

                byte actorTeam = BattleTeamLookup[actor].Value;
                byte targetTeam = BattleTeamLookup[target].Value;
                if (actorTeam == (byte)BattleSimulatorTeamId.None || targetTeam == (byte)BattleSimulatorTeamId.None)
                    return false;

                sameTeam = actorTeam == targetTeam;
                opposingTeam = actorTeam != targetTeam;
                return true;
            }

            bool ShareJoinedFaction(Entity actor, Entity target)
            {
                if (!ActorFactionLookup.HasBuffer(actor))
                    return false;

                DynamicBuffer<ActorFactionMembership> actorFactions = ActorFactionLookup[actor];
                for (int i = 0; i < actorFactions.Length; i++)
                {
                    ActorFactionMembership actorFaction = actorFactions[i];
                    if (actorFaction.Joined == 0)
                        continue;

                    if (ActorFactionLookup.HasBuffer(target))
                    {
                        DynamicBuffer<ActorFactionMembership> targetFactions = ActorFactionLookup[target];
                        for (int j = 0; j < targetFactions.Length; j++)
                        {
                            ActorFactionMembership targetFaction = targetFactions[j];
                            if (targetFaction.Joined != 0 && targetFaction.FactionIndex == actorFaction.FactionIndex)
                                return true;
                        }
                    }

                    if (PlayerFactionLookup.HasBuffer(target))
                    {
                        DynamicBuffer<PlayerFactionMembership> targetFactions = PlayerFactionLookup[target];
                        for (int j = 0; j < targetFactions.Length; j++)
                        {
                            PlayerFactionMembership targetFaction = targetFactions[j];
                            if (targetFaction.Joined != 0 && targetFaction.FactionIndex == actorFaction.FactionIndex)
                                return true;
                        }
                    }
                }

                return false;
            }

            bool IsAggressive(Entity actor, Entity target, float3 actorPosition, float3 targetPosition)
            {
                if (!AiSettingsLookup.HasComponent(actor) || !ActiveMagicEffectLookup.HasBuffer(actor))
                    return false;
                if (SumEffectMagnitude(ActiveMagicEffectLookup[actor], CalmHumanoidEffectId) > 0f)
                    return false;

                int disposition = ResolveDisposition(actor, target);
                float distanceMw = math.distance(actorPosition, targetPosition) / WorldScale.MwUnitsToMeters;
                int fight = AiSettingsLookup[actor].Fight;
                float fightTerm = fight
                                  + FightDistanceBase
                                  - FightDistanceMultiplier * distanceMw
                                  + (50f - disposition) * FightDispositionMultiplier;
                return fightTerm >= 100f;
            }

            int ResolveDisposition(Entity actor, Entity target)
            {
                int disposition = DispositionLookup.HasComponent(actor) ? DispositionLookup[actor].BaseDisposition : 50;
                int reaction = 0;
                bool foundReaction = false;

                if (!ActorFactionLookup.HasBuffer(actor))
                    return math.clamp(disposition, 0, 100);

                DynamicBuffer<ActorFactionMembership> sourceFactions = ActorFactionLookup[actor];
                for (int i = 0; i < sourceFactions.Length; i++)
                {
                    if (sourceFactions[i].Joined == 0)
                        continue;

                    if (ActorFactionLookup.HasBuffer(target))
                    {
                        DynamicBuffer<ActorFactionMembership> targetFactions = ActorFactionLookup[target];
                        for (int j = 0; j < targetFactions.Length; j++)
                        {
                            if (targetFactions[j].Joined == 0)
                                continue;
                            int current = GetFactionReaction(sourceFactions[i].FactionIndex, targetFactions[j].FactionIndex);
                            reaction = foundReaction ? math.min(reaction, current) : current;
                            foundReaction = true;
                        }
                    }

                    if (PlayerFactionLookup.HasBuffer(target))
                    {
                        DynamicBuffer<PlayerFactionMembership> targetFactions = PlayerFactionLookup[target];
                        for (int j = 0; j < targetFactions.Length; j++)
                        {
                            if (targetFactions[j].Joined == 0)
                                continue;
                            int current = GetFactionReaction(sourceFactions[i].FactionIndex, targetFactions[j].FactionIndex);
                            reaction = foundReaction ? math.min(reaction, current) : current;
                            foundReaction = true;
                        }
                    }
                }

                return math.clamp(disposition + (foundReaction ? reaction : 0), 0, 100);
            }

            int GetFactionReaction(int sourceFactionIndex, int targetFactionIndex)
            {
                for (int i = 0; i < FactionReactionOverrides.Length; i++)
                {
                    var reaction = FactionReactionOverrides[i];
                    if (reaction.SourceFactionIndex == sourceFactionIndex && reaction.TargetFactionIndex == targetFactionIndex)
                        return reaction.Reaction;
                }

                if (!Content.IsCreated)
                    return 0;

                ref RuntimeContentBlob content = ref Content.Value;
                if ((uint)sourceFactionIndex >= (uint)content.Factions.Length
                    || (uint)targetFactionIndex >= (uint)content.Factions.Length)
                {
                    return 0;
                }

                ref RuntimeFactionDefBlob source = ref content.Factions[sourceFactionIndex];
                ulong targetIdHash = content.Factions[targetFactionIndex].IdHash;
                if (targetIdHash == 0UL)
                    return 0;

                if (source.FirstReactionIndex < 0
                    || source.ReactionCount < 0
                    || source.FirstReactionIndex + source.ReactionCount > content.FactionReactions.Length)
                {
                    return 0;
                }

                for (int i = 0; i < source.ReactionCount; i++)
                {
                    ref RuntimeFactionReactionDefBlob reaction = ref content.FactionReactions[source.FirstReactionIndex + i];
                    if (reaction.FactionIdHash == targetIdHash)
                        return reaction.Reaction;
                }

                return 0;
            }

            void InsertCandidate(in CombatCandidate candidate)
            {
                int insertAt = -1;
                int count = Candidates.Length;
                for (int i = 0; i < count; i++)
                {
                    if (candidate.DistanceSq < Candidates[i].DistanceSq)
                    {
                        insertAt = i;
                        break;
                    }
                }

                if (insertAt < 0)
                {
                    if (count >= MaxEngagementCandidates)
                        return;
                    insertAt = count;
                }

                if (count < MaxEngagementCandidates)
                    Candidates.Add(default);

                int last = math.min(count, MaxEngagementCandidates - 1);
                for (int i = last; i > insertAt; i--)
                    Candidates[i] = Candidates[i - 1];

                Candidates[insertAt] = candidate;
            }
        }

        static float SumEffectMagnitude(DynamicBuffer<ActorActiveMagicEffect> effects, short effectId)
        {
            float total = 0f;
            for (int i = 0; i < effects.Length; i++)
            {
                var effect = effects[i];
                if (effect.EffectId != effectId || effect.Applied == 0)
                    continue;
                if (effect.DurationSeconds >= 0f && effect.TimeLeftSeconds <= 0f)
                    continue;

                total += math.max(0f, effect.Magnitude);
            }

            return total;
        }

        static float3 Eye(float3 position) => position + new float3(0f, 1.6f, 0f);

        static float3 FlatDelta(float3 target, float3 source)
        {
            float3 delta = target - source;
            delta.y = 0f;
            return delta;
        }

        static uint Hash(uint value)
        {
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return value == 0u ? DefaultRandomSeed : value;
        }

        static float RandomFloat01(uint value)
            => (Hash(value) & 0x00FFFFFFu) / 16777216f;
    }
}
