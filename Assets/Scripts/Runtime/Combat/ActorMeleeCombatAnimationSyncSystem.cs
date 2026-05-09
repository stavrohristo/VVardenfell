using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.AI;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateBefore(typeof(ActorWeaponAnimationSystem))]
    [BurstCompile]
        public partial struct ActorMeleeCombatAnimationSyncSystem : ISystem
    {
        const ulong AttackVoiceDialogueHash = 0xB138769A1AF3A55DUL; // RuntimeContentStableHash.HashId("attack")

        EntityQuery _combatActorQuery;
        NativeList<MeleeCombatVoiceQueueRequest> _voiceRequests;
        EntityTypeHandle _entityHandle;
        ComponentTypeHandle<ActorSpawnSource> _sourceHandle;
        ComponentTypeHandle<ActorDead> _deadHandle;
        ComponentTypeHandle<ActorActiveCombatTarget> _activeCombatTargetHandle;
        ComponentTypeHandle<ActorMeleeCombatAiState> _meleeAiHandle;
        ComponentTypeHandle<LocalTransform> _transformHandle;
        ComponentTypeHandle<ActorLocalBounds> _boundsHandle;
        ComponentTypeHandle<MorrowindMovementState> _movementStateHandle;
        ComponentTypeHandle<ActorWeaponAnimationState> _weaponStateHandle;
        ComponentTypeHandle<ActorHitAftermathState> _aftermathHandle;
        BufferTypeHandle<ActorAnimationOverlayState> _overlayHandle;
        BufferTypeHandle<ActorActiveMagicEffect> _activeEffectHandle;
        BufferTypeHandle<ActorEquipmentSlot> _equipmentHandle;
        ComponentLookup<ActorDead> _deadLookup;
        ComponentLookup<PlacedRefRuntimeState> _placedRefStateLookup;
        ComponentLookup<PlacedRefIdentity> _placedRefLookup;
        ComponentLookup<LocalToWorld> _localToWorldLookup;
        ComponentLookup<ActorLocalBounds> _boundsLookup;
        ComponentLookup<PlayerCharacterComponent> _playerLookup;
        ComponentLookup<ActorHitAftermathState> _aftermathLookup;
        short _paralyzeEffectId;

        public void OnCreate(ref SystemState systemState)
        {
            _combatActorQuery = systemState.GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ActorSpawnSource>(),
                    ComponentType.ReadOnly<ActorDead>(),
                    ComponentType.ReadOnly<ActorActiveCombatTarget>(),
                    ComponentType.ReadWrite<ActorMeleeCombatAiState>(),
                    ComponentType.ReadWrite<LocalTransform>(),
                    ComponentType.ReadOnly<ActorLocalBounds>(),
                    ComponentType.ReadOnly<MorrowindMovementState>(),
                    ComponentType.ReadWrite<ActorWeaponAnimationState>(),
                    ComponentType.ReadWrite<ActorAnimationOverlayState>(),
                    ComponentType.ReadOnly<ActorHitAftermathState>(),
                    ComponentType.ReadOnly<ActorActiveMagicEffect>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<PlayerTag>(),
                    ComponentType.ReadOnly<LocalPlayerVisual>(),
                },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });

            _voiceRequests = new NativeList<MeleeCombatVoiceQueueRequest>(Allocator.Persistent);
            _entityHandle = systemState.GetEntityTypeHandle();
            _sourceHandle = systemState.GetComponentTypeHandle<ActorSpawnSource>(isReadOnly: true);
            _deadHandle = systemState.GetComponentTypeHandle<ActorDead>(isReadOnly: true);
            _activeCombatTargetHandle = systemState.GetComponentTypeHandle<ActorActiveCombatTarget>(isReadOnly: true);
            _meleeAiHandle = systemState.GetComponentTypeHandle<ActorMeleeCombatAiState>(isReadOnly: false);
            _transformHandle = systemState.GetComponentTypeHandle<LocalTransform>(isReadOnly: false);
            _boundsHandle = systemState.GetComponentTypeHandle<ActorLocalBounds>(isReadOnly: true);
            _movementStateHandle = systemState.GetComponentTypeHandle<MorrowindMovementState>(isReadOnly: true);
            _weaponStateHandle = systemState.GetComponentTypeHandle<ActorWeaponAnimationState>(isReadOnly: false);
            _aftermathHandle = systemState.GetComponentTypeHandle<ActorHitAftermathState>(isReadOnly: true);
            _overlayHandle = systemState.GetBufferTypeHandle<ActorAnimationOverlayState>(isReadOnly: true);
            _activeEffectHandle = systemState.GetBufferTypeHandle<ActorActiveMagicEffect>(isReadOnly: true);
            _equipmentHandle = systemState.GetBufferTypeHandle<ActorEquipmentSlot>(isReadOnly: true);
            _deadLookup = systemState.GetComponentLookup<ActorDead>(isReadOnly: true);
            _placedRefStateLookup = systemState.GetComponentLookup<PlacedRefRuntimeState>(isReadOnly: true);
            _placedRefLookup = systemState.GetComponentLookup<PlacedRefIdentity>(isReadOnly: true);
            _localToWorldLookup = systemState.GetComponentLookup<LocalToWorld>(isReadOnly: true);
            _boundsLookup = systemState.GetComponentLookup<ActorLocalBounds>(isReadOnly: true);
            _playerLookup = systemState.GetComponentLookup<PlayerCharacterComponent>(isReadOnly: true);
            _aftermathLookup = systemState.GetComponentLookup<ActorHitAftermathState>(isReadOnly: true);
            _paralyzeEffectId = RequireEffectId("sEffectParalyze");

            systemState.RequireForUpdate(_combatActorQuery);
            systemState.RequireForUpdate<MorrowindCombatRuntimeState>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<PhysicsWorldSingleton>();
        }

        public void OnDestroy(ref SystemState systemState)
        {
            if (_voiceRequests.IsCreated)
                _voiceRequests.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][ContentBlob] Actor melee combat animation sync requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;

            int voiceAttackOdds = RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref content, RuntimeContentKnownHashes.iVoiceAttackOdds);
            if (voiceAttackOdds < 0 || voiceAttackOdds > 100)
                throw new InvalidOperationException($"[VVardenfell][ActorMelee] GMST 'iVoiceAttackOdds' must be between 0 and 100, got {voiceAttackOdds}.");

            Entity scriptRuntimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            if (!systemState.EntityManager.HasBuffer<MorrowindCombatVoiceResolveRequest>(scriptRuntimeEntity))
                throw new InvalidOperationException("[VVardenfell][ActorMelee] Script runtime has no MorrowindCombatVoiceResolveRequest buffer.");
            if (!systemState.EntityManager.HasBuffer<MorrowindScriptActiveSay>(scriptRuntimeEntity))
                throw new InvalidOperationException("[VVardenfell][ActorMelee] Script runtime has no MorrowindScriptActiveSay buffer.");

            ref var combatState = ref SystemAPI.GetSingletonRW<MorrowindCombatRuntimeState>().ValueRW;
            var random = new Unity.Mathematics.Random(combatState.RandomState == 0u ? 0x6E624EB7u : combatState.RandomState);
            uint frameSeed = random.NextUInt();
            combatState.RandomState = random.state == 0u ? 0x6E624EB7u : random.state;

            int actorCount = _combatActorQuery.CalculateEntityCount();
            if (_voiceRequests.Capacity < actorCount)
                _voiceRequests.Capacity = actorCount;
            _voiceRequests.Clear();

            _entityHandle.Update(ref systemState);
            _sourceHandle.Update(ref systemState);
            _deadHandle.Update(ref systemState);
            _activeCombatTargetHandle.Update(ref systemState);
            _meleeAiHandle.Update(ref systemState);
            _transformHandle.Update(ref systemState);
            _boundsHandle.Update(ref systemState);
            _movementStateHandle.Update(ref systemState);
            _weaponStateHandle.Update(ref systemState);
            _aftermathHandle.Update(ref systemState);
            _overlayHandle.Update(ref systemState);
            _activeEffectHandle.Update(ref systemState);
            _equipmentHandle.Update(ref systemState);
            _deadLookup.Update(ref systemState);
            _placedRefStateLookup.Update(ref systemState);
            _placedRefLookup.Update(ref systemState);
            _localToWorldLookup.Update(ref systemState);
            _boundsLookup.Update(ref systemState);
            _playerLookup.Update(ref systemState);
            _aftermathLookup.Update(ref systemState);

            systemState.Dependency = new MeleeCombatAnimationJob
            {
                Content = contentBlobReference.Blob,
                CollisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld,
                DeltaTime = math.max(0f, SystemAPI.Time.DeltaTime),
                FrameSeed = frameSeed == 0u ? 1u : frameSeed,
                VoiceAttackOdds = voiceAttackOdds,
                ParalyzeEffectId = _paralyzeEffectId,
                EntityHandle = _entityHandle,
                SourceHandle = _sourceHandle,
                DeadHandle = _deadHandle,
                ActiveCombatTargetHandle = _activeCombatTargetHandle,
                MeleeAiHandle = _meleeAiHandle,
                TransformHandle = _transformHandle,
                BoundsHandle = _boundsHandle,
                MovementStateHandle = _movementStateHandle,
                WeaponStateHandle = _weaponStateHandle,
                AftermathHandle = _aftermathHandle,
                OverlayHandle = _overlayHandle,
                ActiveEffectHandle = _activeEffectHandle,
                EquipmentHandle = _equipmentHandle,
                DeadLookup = _deadLookup,
                PlacedRefStateLookup = _placedRefStateLookup,
                PlacedRefLookup = _placedRefLookup,
                LocalToWorldLookup = _localToWorldLookup,
                BoundsLookup = _boundsLookup,
                PlayerLookup = _playerLookup,
                AftermathLookup = _aftermathLookup,
                VoiceRequests = _voiceRequests.AsParallelWriter(),
            }.ScheduleParallel(_combatActorQuery, systemState.Dependency);

            systemState.Dependency.Complete();
            ApplyVoiceRequests(ref systemState, ref content, scriptRuntimeEntity);
        }

        void ApplyVoiceRequests(ref SystemState systemState, ref RuntimeContentBlob content, Entity scriptRuntimeEntity)
        {
            var combatVoiceRequests = systemState.EntityManager.GetBuffer<MorrowindCombatVoiceResolveRequest>(scriptRuntimeEntity);
            var activeSays = systemState.EntityManager.GetBuffer<MorrowindScriptActiveSay>(scriptRuntimeEntity, true);
            for (int i = 0; i < _voiceRequests.Length; i++)
            {
                var request = _voiceRequests[i];
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

        struct MeleeCombatVoiceQueueRequest
        {
            public Entity Actor;
            public uint PlacedRefId;
            public ActorDefHandle ActorDef;
            public uint RandomState;
        }

        [BurstCompile]
        struct MeleeCombatAnimationJob : IJobChunk
        {
            [ReadOnly] public BlobAssetReference<RuntimeContentBlob> Content;
            [ReadOnly] public CollisionWorld CollisionWorld;
            public float DeltaTime;
            public uint FrameSeed;
            public int VoiceAttackOdds;
            public short ParalyzeEffectId;
            [ReadOnly] public EntityTypeHandle EntityHandle;
            [ReadOnly] public ComponentTypeHandle<ActorSpawnSource> SourceHandle;
            [ReadOnly] public ComponentTypeHandle<ActorDead> DeadHandle;
            [ReadOnly] public ComponentTypeHandle<ActorActiveCombatTarget> ActiveCombatTargetHandle;
            public ComponentTypeHandle<ActorMeleeCombatAiState> MeleeAiHandle;
            public ComponentTypeHandle<LocalTransform> TransformHandle;
            [ReadOnly] public ComponentTypeHandle<ActorLocalBounds> BoundsHandle;
            [ReadOnly] public ComponentTypeHandle<MorrowindMovementState> MovementStateHandle;
            public ComponentTypeHandle<ActorWeaponAnimationState> WeaponStateHandle;
            [ReadOnly] public ComponentTypeHandle<ActorHitAftermathState> AftermathHandle;
            [ReadOnly] public BufferTypeHandle<ActorAnimationOverlayState> OverlayHandle;
            [ReadOnly] public BufferTypeHandle<ActorActiveMagicEffect> ActiveEffectHandle;
            [ReadOnly] public BufferTypeHandle<ActorEquipmentSlot> EquipmentHandle;
            [ReadOnly] public ComponentLookup<ActorDead> DeadLookup;
            [ReadOnly] public ComponentLookup<PlacedRefRuntimeState> PlacedRefStateLookup;
            [ReadOnly] public ComponentLookup<PlacedRefIdentity> PlacedRefLookup;
            [ReadOnly] public ComponentLookup<LocalToWorld> LocalToWorldLookup;
            [ReadOnly] public ComponentLookup<ActorLocalBounds> BoundsLookup;
            [ReadOnly] public ComponentLookup<PlayerCharacterComponent> PlayerLookup;
            [ReadOnly] public ComponentLookup<ActorHitAftermathState> AftermathLookup;
            public NativeList<MeleeCombatVoiceQueueRequest>.ParallelWriter VoiceRequests;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
            {
                var entities = chunk.GetNativeArray(EntityHandle);
                var sources = chunk.GetNativeArray(ref SourceHandle);
                var activeTargets = chunk.GetNativeArray(ref ActiveCombatTargetHandle);
                var aiStates = chunk.GetNativeArray(ref MeleeAiHandle);
                var transforms = chunk.GetNativeArray(ref TransformHandle);
                var actorBounds = chunk.GetNativeArray(ref BoundsHandle);
                var weaponStates = chunk.GetNativeArray(ref WeaponStateHandle);
                var aftermath = chunk.GetNativeArray(ref AftermathHandle);
                var overlays = chunk.GetBufferAccessor(ref OverlayHandle);
                var effects = chunk.GetBufferAccessor(ref ActiveEffectHandle);
                bool hasEquipment = chunk.Has(ref EquipmentHandle);
                BufferAccessor<ActorEquipmentSlot> equipment = default;
                if (hasEquipment)
                    equipment = chunk.GetBufferAccessor(ref EquipmentHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    Entity actor = entities[i];
                    var source = sources[i];
                    var aiState = aiStates[i];
                    var transform = transforms[i];
                    var weapon = weaponStates[i];
                    var random = new Unity.Mathematics.Random(BuildActorSeed(actor, source, i));

                    ResolveEquippedMeleeWeapon(actor, source, hasEquipment, hasEquipment ? equipment[i] : default, ref weapon);

                    if (!chunk.IsComponentEnabled(ref ActiveCombatTargetHandle, i)
                        || chunk.IsComponentEnabled(ref DeadHandle, i)
                        || IsDisabled(actor))
                    {
                        StopActorCombatAnimation(ref aiState, ref weapon);
                        WriteBack(i, aiState, transform, weapon);
                        continue;
                    }

                    Entity target = activeTargets[i].TargetEntity;
                    if (target == Entity.Null
                        || !LocalToWorldLookup.HasComponent(target)
                        || IsActorDisabledOrDead(target)
                        || IsActorUnableToAttack(aftermath[i], effects[i]))
                    {
                        StopActorCombatAnimation(ref aiState, ref weapon);
                        WriteBack(i, aiState, transform, weapon);
                        continue;
                    }

                    if (!ActorWeaponAnimationUtility.IsSupportedMelee(weapon.WeaponType))
                    {
                        StopActorCombatAnimation(ref aiState, ref weapon);
                        WriteBack(i, aiState, transform, weapon);
                        continue;
                    }

                    if (weapon.Drawn == 0 && !IsAttacking(weapon.Phase))
                    {
                        weapon.ReadyWeaponTogglePressed = 1;
                        WriteBack(i, aiState, transform, weapon);
                        continue;
                    }

                    if (aiState.CooldownSeconds > 0f)
                        aiState.CooldownSeconds = math.max(0f, aiState.CooldownSeconds - DeltaTime);

                    UpdateAttackRelease(ref aiState, ref weapon, overlays[i]);

                    if (aiState.AttackInProgress != 0 || aiState.CooldownSeconds > 0f)
                    {
                        WriteBack(i, aiState, transform, weapon);
                        continue;
                    }

                    if (weapon.Drawn == 0 || weapon.Phase != ActorWeaponAnimationPhase.Equipped)
                    {
                        WriteBack(i, aiState, transform, weapon);
                        continue;
                    }

                    if (!CanStartMeleeAttack(actor, actorBounds[i], transform, weapon, target, out var facedTransform))
                    {
                        WriteBack(i, aiState, transform, weapon);
                        continue;
                    }

                    transform = facedTransform;
                    aiState.DesiredAttackStrength = random.NextFloat();
                    aiState.DesiredAttackType = ChooseAttackType(weapon.WeaponContent, ref random);
                    aiState.AttackInProgress = 1;
                    aiState.CooldownSeconds = ResolveAttackCooldown(source, ref random);
                    weapon.AiAttackTypeOverride = 1;
                    weapon.AiAttackType = aiState.DesiredAttackType;
                    weapon.AttackPressed = 1;
                    if (random.NextInt(100) < VoiceAttackOdds)
                        TryQueueAttackVoice(actor, source, ref random);

                    WriteBack(i, aiState, transform, weapon);
                }

                void WriteBack(int index, ActorMeleeCombatAiState aiState, LocalTransform transform, ActorWeaponAnimationState weapon)
                {
                    aiStates[index] = aiState;
                    transforms[index] = transform;
                    weaponStates[index] = weapon;
                }
            }

            void ResolveEquippedMeleeWeapon(
                Entity actor,
                in ActorSpawnSource source,
                bool hasEquipment,
                DynamicBuffer<ActorEquipmentSlot> equipment,
                ref ActorWeaponAnimationState state)
            {
                state.WeaponType = ActorWeaponAnimationUtility.NoWeaponType;
                state.WeaponContent = default;

                if (!hasEquipment)
                {
                    if (ResolveActorKind(source) == ActorDefKind.Creature)
                        return;

                    throw new InvalidOperationException($"[VVardenfell][ActorMelee] NPC ref=0x{PlacedRefId(actor):X8} has no ActorEquipmentSlot buffer.");
                }

                ref RuntimeContentBlob content = ref Content.Value;
                state.WeaponType = ActorWeaponAnimationUtility.ResolveEquippedWeaponType(ref content, equipment, out var weaponContent);
                state.WeaponContent = weaponContent;
            }

            static void StopActorCombatAnimation(ref ActorMeleeCombatAiState aiState, ref ActorWeaponAnimationState weapon)
            {
                ClearAttackControls(ref aiState, ref weapon);
                aiState.CooldownSeconds = 0f;
                if (weapon.Phase == ActorWeaponAnimationPhase.AttackWindUp)
                    weapon.AttackReleased = 1;
                weapon.MeleeSwingPending = 0;
                weapon.MeleeSwingAttackStrength = 0f;
                weapon.MeleeSwingWeaponContent = default;
                weapon.MeleeHitPending = 0;
                weapon.MeleeHitAttackStrength = 0f;
                weapon.MeleeHitWeaponContent = default;
                if (weapon.Drawn != 0 && !IsAttacking(weapon.Phase))
                    weapon.ReadyWeaponTogglePressed = 1;
            }

            static void ClearAttackControls(ref ActorMeleeCombatAiState aiState, ref ActorWeaponAnimationState weapon)
            {
                aiState.AttackInProgress = 0;
                aiState.DesiredAttackStrength = 0f;
                weapon.AttackPressed = 0;
                weapon.AttackReleased = 0;
                weapon.AiAttackTypeOverride = 0;
            }

            static void UpdateAttackRelease(ref ActorMeleeCombatAiState aiState, ref ActorWeaponAnimationState weapon, DynamicBuffer<ActorAnimationOverlayState> overlays)
            {
                if (aiState.AttackInProgress == 0)
                {
                    if (weapon.Phase == ActorWeaponAnimationPhase.AttackWindUp)
                        weapon.AttackReleased = 1;
                    return;
                }

                if (!IsAttacking(weapon.Phase))
                {
                    aiState.AttackInProgress = 0;
                    weapon.AiAttackTypeOverride = 0;
                    return;
                }

                if (weapon.Phase != ActorWeaponAnimationPhase.AttackWindUp)
                    return;

                float attackStrength = weapon.AttackStrength;
                if (TryGetUpperBodyOverlay(overlays, out var overlay) && overlay.Playback.Playing != 0 && overlay.Playback.ClipIndex >= 0)
                {
                    float span = weapon.AttackMaxTime - weapon.AttackMinTime;
                    attackStrength = span > 0.0001f
                        ? math.saturate((overlay.Playback.Time - weapon.AttackMinTime) / span)
                        : 1f;
                }

                if (attackStrength >= math.saturate(aiState.DesiredAttackStrength))
                    weapon.AttackReleased = 1;
            }

            static bool TryGetUpperBodyOverlay(DynamicBuffer<ActorAnimationOverlayState> overlays, out ActorAnimationOverlayState overlay)
            {
                for (int i = 0; i < overlays.Length; i++)
                {
                    overlay = overlays[i];
                    if (overlay.Mask == ActorAnimationBlendMask.UpperBody)
                        return true;
                }

                overlay = default;
                return false;
            }

            static bool IsAttacking(ActorWeaponAnimationPhase phase)
                => phase == ActorWeaponAnimationPhase.AttackWindUp
                   || phase == ActorWeaponAnimationPhase.AttackRelease
                   || phase == ActorWeaponAnimationPhase.AttackFollow;

            bool IsActorUnableToAttack(in ActorHitAftermathState aftermath, DynamicBuffer<ActorActiveMagicEffect> effects)
            {
                if (aftermath.KnockedDown != 0 || aftermath.KnockedOut != 0 || aftermath.HitRecovery != 0)
                    return true;

                return MorrowindMeleeCombatMechanics.SumEffectMagnitude(effects, ParalyzeEffectId) > 0f;
            }

            bool IsActorDisabledOrDead(Entity actor)
            {
                if (IsDisabled(actor))
                    return true;

                return DeadLookup.HasComponent(actor) && DeadLookup.IsComponentEnabled(actor);
            }

            bool CanStartMeleeAttack(
                Entity actor,
                in ActorLocalBounds actorBounds,
                in LocalTransform actorTransform,
                in ActorWeaponAnimationState weapon,
                Entity target,
                out LocalTransform facedTransform)
            {
                facedTransform = actorTransform;
                if (!TryResolveTargetBounds(target, out float3 targetBase, out _, out float targetHeight, out float3 targetCenter))
                    return false;
                if (!IsTargetInsideMeleeReach(actorBounds, actorTransform, weapon, target))
                    return false;

                FaceTarget(ref facedTransform, target);
                if (!PassesCombatCone(facedTransform, targetBase, targetHeight))
                    return false;

                float3 source = math.transform(
                    float4x4.TRS(actorTransform.Position, facedTransform.Rotation, new float3(actorTransform.Scale)),
                    actorBounds.Center);
                var input = new RaycastInput
                {
                    Start = source,
                    End = targetCenter,
                    Filter = InteractionCollisionLayers.LineOfSightQueryFilter,
                };
                return !CollisionWorld.CastRay(input);
            }

            void FaceTarget(ref LocalTransform actorTransform, Entity target)
            {
                float3 delta = LocalToWorldLookup[target].Position - actorTransform.Position;
                delta.y = 0f;
                if (math.lengthsq(delta) > 0.000001f)
                    actorTransform.Rotation = quaternion.LookRotationSafe(math.normalize(delta), math.up());
            }

            bool IsTargetInsideMeleeReach(
                in ActorLocalBounds actorBounds,
                in LocalTransform actorTransform,
                in ActorWeaponAnimationState weaponState,
                Entity target)
            {
                if (!TryResolveTargetBounds(target, out float3 targetBase, out float targetRadius, out _, out _))
                    return false;

                ref RuntimeContentBlob content = ref Content.Value;
                MorrowindMeleeCombatMechanics.ResolveWeaponEquipment(
                    ref content,
                    weaponState.WeaponContent,
                    out bool hasWeapon,
                    out _,
                    out var weapon,
                    out _);
                float reach = MorrowindMeleeCombatMechanics.ComputeMeleeReach(ref content, hasWeapon, weapon);
                float actorRadius = math.max(actorBounds.Extents.x, actorBounds.Extents.z) * math.max(0.01f, actorTransform.Scale);
                float distanceToBounds = math.max(0f, math.distance(ToHorizontal(actorTransform.Position), ToHorizontal(targetBase)) - actorRadius - targetRadius);
                return distanceToBounds <= reach;
            }

            bool TryResolveTargetBounds(
                Entity target,
                out float3 targetBase,
                out float targetRadius,
                out float targetHeight,
                out float3 targetCenter)
            {
                targetBase = default;
                targetRadius = 0f;
                targetHeight = 0f;
                targetCenter = default;
                if (!LocalToWorldLookup.HasComponent(target))
                    throw new InvalidOperationException($"[VVardenfell][ActorMelee] Target entity={target.Index}:{target.Version} has no LocalTransform.");

                var localToWorld = LocalToWorldLookup[target];
                targetBase = localToWorld.Position;
                if (BoundsLookup.HasComponent(target))
                {
                    var bounds = BoundsLookup[target];
                    float scale = ResolveUniformScale(localToWorld);
                    targetRadius = math.max(bounds.Extents.x, bounds.Extents.z) * scale;
                    targetHeight = math.max(0.01f, bounds.Extents.y * 2f * scale);
                    targetCenter = math.transform(localToWorld.Value, bounds.Center);
                    return true;
                }

                if (PlayerLookup.HasComponent(target))
                {
                    var player = PlayerLookup[target];
                    targetRadius = math.max(0.01f, player.Radius);
                    targetHeight = math.max(0.01f, player.StandingHeight);
                    targetCenter = targetBase + new float3(0f, targetHeight * 0.5f, 0f);
                    return true;
                }

                throw new InvalidOperationException($"[VVardenfell][ActorMelee] Target entity={target.Index}:{target.Version} has no ActorLocalBounds or PlayerCharacterComponent.");
            }

            bool PassesCombatCone(in LocalTransform actorTransform, float3 targetBase, float targetHeight)
            {
                ref RuntimeContentBlob content = ref Content.Value;
                float combatAngleXY = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fCombatAngleXY) / 90f;
                float combatAngleZ = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fCombatAngleZ) / 90f;
                float3 forward = math.normalizesafe(math.rotate(actorTransform.Rotation, new float3(0f, 0f, 1f)), new float3(0f, 0f, 1f));
                float3 forwardXY = math.normalizesafe(new float3(forward.x, 0f, forward.z), new float3(0f, 0f, 1f));
                float3 toTargetXY = math.normalizesafe(ToHorizontal(targetBase - actorTransform.Position), float3.zero);
                if (math.lengthsq(toTargetXY) <= 0f)
                    return false;
                if (math.dot(toTargetXY, forwardXY) <= 0f)
                    return false;
                if (math.abs(toTargetXY.x * forwardXY.z - toTargetXY.z * forwardXY.x) > combatAngleXY)
                    return false;

                float actorVerticalAngle = forward.y;
                float3 toFeet = math.normalizesafe(targetBase - actorTransform.Position, float3.zero);
                float3 toHead = math.normalizesafe(targetBase + new float3(0f, targetHeight, 0f) - actorTransform.Position, float3.zero);
                return actorVerticalAngle - toHead.y <= combatAngleZ && actorVerticalAngle - toFeet.y >= -combatAngleZ;
            }

            ActorWeaponAttackType ChooseAttackType(in ContentReference weaponContent, ref Unity.Mathematics.Random random)
            {
                if (!weaponContent.IsValid)
                    return ChooseRandomAttackType(ref random);

                ref RuntimeContentBlob content = ref Content.Value;
                MorrowindMeleeCombatMechanics.ResolveWeaponEquipment(
                    ref content,
                    weaponContent,
                    out bool hasWeapon,
                    out _,
                    out var weapon,
                    out _);
                if (!hasWeapon)
                    return ChooseRandomAttackType(ref random);

                int slash = (weapon.SlashMin + weapon.SlashMax) / 2;
                int chop = (weapon.ChopMin + weapon.ChopMax) / 2;
                int thrust = (weapon.ThrustMin + weapon.ThrustMax) / 2;
                int total = slash + chop + thrust;
                if (total <= 0)
                    return ChooseRandomAttackType(ref random);

                float roll = random.NextFloat() * total;
                if (roll <= slash)
                    return ActorWeaponAttackType.Slash;
                if (roll <= slash + thrust)
                    return ActorWeaponAttackType.Thrust;
                return ActorWeaponAttackType.Chop;
            }

            static ActorWeaponAttackType ChooseRandomAttackType(ref Unity.Mathematics.Random random)
            {
                float roll = random.NextFloat();
                if (roll >= 2f / 3f)
                    return ActorWeaponAttackType.Thrust;
                if (roll >= 1f / 3f)
                    return ActorWeaponAttackType.Slash;
                return ActorWeaponAttackType.Chop;
            }

            float ResolveAttackCooldown(in ActorSpawnSource source, ref Unity.Mathematics.Random random)
            {
                ref RuntimeContentBlob content = ref Content.Value;
                ulong gmstHash = ResolveActorKind(source) == ActorDefKind.Creature
                    ? RuntimeContentKnownHashes.fCombatDelayCreature
                    : RuntimeContentKnownHashes.fCombatDelayNPC;
                float baseDelay = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, gmstHash);
                return math.min(baseDelay + 0.01f * random.NextInt(100), baseDelay + 0.9f);
            }

            void TryQueueAttackVoice(Entity actor, in ActorSpawnSource source, ref Unity.Mathematics.Random random)
            {
                if (ResolveActorKind(source) != ActorDefKind.Npc)
                    return;

                uint placedRefId = PlacedRefId(actor);
                uint seed = random.NextUInt();
                VoiceRequests.AddNoResize(new MeleeCombatVoiceQueueRequest
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
                    throw new InvalidOperationException("[VVardenfell][ActorMelee] Actor has invalid ActorSpawnSource.");
                if (!Content.IsCreated || (uint)source.Definition.Index >= (uint)Content.Value.Actors.Length)
                    throw new InvalidOperationException("[VVardenfell][ActorMelee] Actor has out-of-range ActorSpawnSource.");

                return Content.Value.Actors[source.Definition.Index].Kind;
            }

            bool IsDisabled(Entity actor)
                => PlacedRefStateLookup.HasComponent(actor) && PlacedRefStateLookup[actor].Disabled != 0;

            uint PlacedRefId(Entity entity)
                => PlacedRefLookup.HasComponent(entity) ? PlacedRefLookup[entity].Value : 0u;

            uint BuildActorSeed(Entity actor, in ActorSpawnSource source, int chunkIndex)
            {
                uint placedRefId = PlacedRefId(actor);
                uint seed = math.hash(new uint4(FrameSeed, placedRefId, (uint)(actor.Index + 1), (uint)(source.Definition.Index + chunkIndex + 1)));
                return seed == 0u ? 1u : seed;
            }

            static float3 ToHorizontal(float3 value)
                => new(value.x, 0f, value.z);

            static float ResolveUniformScale(in LocalToWorld localToWorld)
            {
                float3 x = localToWorld.Value.c0.xyz;
                float3 y = localToWorld.Value.c1.xyz;
                float3 z = localToWorld.Value.c2.xyz;
                return math.max(0.01f, math.cmax(new float3(math.length(x), math.length(y), math.length(z))));
            }
        }

        static short RequireEffectId(string gmstId)
        {
            if (!MorrowindMagicEffectTextUtility.TryResolveEffectId(gmstId, out short effectId))
                throw new InvalidOperationException($"[VVardenfell][ActorMelee] Required magic effect GMST '{gmstId}' is not mapped.");
            return effectId;
        }
    }
}
