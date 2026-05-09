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
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Pathfinding;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindPhysicsQuerySystemGroup))]
    [UpdateAfter(typeof(PathGridTraversalSteeringSystem))]
    [UpdateBefore(typeof(MorrowindActorMovementSystem))]
    [BurstCompile]
    public partial struct MorrowindCombatMovementSystem : ISystem
    {
        const float FacingSharpness = 12f;
        const float SidestepAngleRadians = math.PI / 4f;
        const float SidestepDecisionCooldownMin = 0.45f;
        const float SidestepDecisionCooldownRange = 0.35f;

        EntityQuery _query;
        EntityTypeHandle _entityHandle;
        ComponentTypeHandle<ActorSpawnSource> _sourceHandle;
        ComponentTypeHandle<ActorDead> _deadHandle;
        ComponentTypeHandle<ActorActiveCombatTarget> _activeCombatTargetHandle;
        ComponentTypeHandle<ActorCombatMovementState> _combatMovementHandle;
        ComponentTypeHandle<ActorAiState> _aiStateHandle;
        ComponentTypeHandle<LocalTransform> _transformHandle;
        ComponentTypeHandle<ActorLocalBounds> _boundsHandle;
        ComponentTypeHandle<MorrowindMovementInput> _movementInputHandle;
        ComponentTypeHandle<PathGridTraversalState> _traversalStateHandle;
        BufferTypeHandle<ActorAiPackageRuntime> _packageHandle;
        ComponentLookup<ActorActiveCombatTarget> _activeCombatTargetLookup;
        ComponentLookup<ActorSpawnSource> _sourceLookup;
        ComponentLookup<ActorDead> _deadLookup;
        ComponentLookup<PlacedRefRuntimeState> _placedRefStateLookup;
        ComponentLookup<PlacedRefIdentity> _placedRefLookup;
        ComponentLookup<LocalToWorld> _localToWorldLookup;
        ComponentLookup<ActorLocalBounds> _boundsLookup;
        ComponentLookup<PlayerCharacterComponent> _playerLookup;
        ComponentLookup<PathGridTraversalPendingRequest> _traversalPendingLookup;
        ComponentLookup<PathGridTraversalAwaitingResult> _traversalAwaitingLookup;
        BufferLookup<ActorAiPackageRuntime> _packageLookup;
        BufferLookup<ActorEquipmentSlot> _equipmentLookup;

        public void OnCreate(ref SystemState systemState)
        {
            _query = systemState.GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ActorSpawnSource>(),
                    ComponentType.ReadOnly<ActorDead>(),
                    ComponentType.ReadOnly<ActorActiveCombatTarget>(),
                    ComponentType.ReadWrite<ActorCombatMovementState>(),
                    ComponentType.ReadWrite<ActorAiState>(),
                    ComponentType.ReadOnly<ActorAiPackageRuntime>(),
                    ComponentType.ReadWrite<LocalTransform>(),
                    ComponentType.ReadOnly<ActorLocalBounds>(),
                    ComponentType.ReadWrite<MorrowindMovementInput>(),
                    ComponentType.ReadOnly<PathGridTraversalState>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<PlayerTag>(),
                    ComponentType.ReadOnly<LocalPlayerVisual>(),
                },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });

            systemState.RequireForUpdate(_query);
            systemState.RequireForUpdate<RuntimeContentBlobReference>();

            _entityHandle = systemState.GetEntityTypeHandle();
            _sourceHandle = systemState.GetComponentTypeHandle<ActorSpawnSource>(isReadOnly: true);
            _deadHandle = systemState.GetComponentTypeHandle<ActorDead>(isReadOnly: true);
            _activeCombatTargetHandle = systemState.GetComponentTypeHandle<ActorActiveCombatTarget>(isReadOnly: true);
            _combatMovementHandle = systemState.GetComponentTypeHandle<ActorCombatMovementState>(isReadOnly: false);
            _aiStateHandle = systemState.GetComponentTypeHandle<ActorAiState>(isReadOnly: false);
            _transformHandle = systemState.GetComponentTypeHandle<LocalTransform>(isReadOnly: false);
            _boundsHandle = systemState.GetComponentTypeHandle<ActorLocalBounds>(isReadOnly: true);
            _movementInputHandle = systemState.GetComponentTypeHandle<MorrowindMovementInput>(isReadOnly: false);
            _traversalStateHandle = systemState.GetComponentTypeHandle<PathGridTraversalState>(isReadOnly: true);
            _packageHandle = systemState.GetBufferTypeHandle<ActorAiPackageRuntime>(isReadOnly: true);
            _activeCombatTargetLookup = systemState.GetComponentLookup<ActorActiveCombatTarget>(isReadOnly: true);
            _sourceLookup = systemState.GetComponentLookup<ActorSpawnSource>(isReadOnly: true);
            _deadLookup = systemState.GetComponentLookup<ActorDead>(isReadOnly: true);
            _placedRefStateLookup = systemState.GetComponentLookup<PlacedRefRuntimeState>(isReadOnly: true);
            _placedRefLookup = systemState.GetComponentLookup<PlacedRefIdentity>(isReadOnly: true);
            _localToWorldLookup = systemState.GetComponentLookup<LocalToWorld>(isReadOnly: true);
            _boundsLookup = systemState.GetComponentLookup<ActorLocalBounds>(isReadOnly: true);
            _playerLookup = systemState.GetComponentLookup<PlayerCharacterComponent>(isReadOnly: true);
            _traversalPendingLookup = systemState.GetComponentLookup<PathGridTraversalPendingRequest>(isReadOnly: true);
            _traversalAwaitingLookup = systemState.GetComponentLookup<PathGridTraversalAwaitingResult>(isReadOnly: true);
            _packageLookup = systemState.GetBufferLookup<ActorAiPackageRuntime>(isReadOnly: true);
            _equipmentLookup = systemState.GetBufferLookup<ActorEquipmentSlot>(isReadOnly: true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            float deltaTime = math.max(0f, SystemAPI.Time.DeltaTime);
            if (deltaTime <= 0f)
                return;

            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][CombatMove] requires runtime content blob.");

            float elapsedTime = (float)SystemAPI.Time.ElapsedTime;

            _entityHandle.Update(ref systemState);
            _sourceHandle.Update(ref systemState);
            _deadHandle.Update(ref systemState);
            _activeCombatTargetHandle.Update(ref systemState);
            _combatMovementHandle.Update(ref systemState);
            _aiStateHandle.Update(ref systemState);
            _transformHandle.Update(ref systemState);
            _boundsHandle.Update(ref systemState);
            _movementInputHandle.Update(ref systemState);
            _traversalStateHandle.Update(ref systemState);
            _packageHandle.Update(ref systemState);
            _activeCombatTargetLookup.Update(ref systemState);
            _sourceLookup.Update(ref systemState);
            _deadLookup.Update(ref systemState);
            _placedRefStateLookup.Update(ref systemState);
            _placedRefLookup.Update(ref systemState);
            _localToWorldLookup.Update(ref systemState);
            _boundsLookup.Update(ref systemState);
            _playerLookup.Update(ref systemState);
            _traversalPendingLookup.Update(ref systemState);
            _traversalAwaitingLookup.Update(ref systemState);
            _packageLookup.Update(ref systemState);
            _equipmentLookup.Update(ref systemState);

            systemState.Dependency = new CombatMovementJob
            {
                Content = contentBlobReference.Blob,
                DeltaTime = deltaTime,
                ElapsedTime = elapsedTime,
                EntityHandle = _entityHandle,
                SourceHandle = _sourceHandle,
                DeadHandle = _deadHandle,
                ActiveCombatTargetHandle = _activeCombatTargetHandle,
                CombatMovementHandle = _combatMovementHandle,
                AiStateHandle = _aiStateHandle,
                TransformHandle = _transformHandle,
                BoundsHandle = _boundsHandle,
                MovementInputHandle = _movementInputHandle,
                TraversalStateHandle = _traversalStateHandle,
                PackageHandle = _packageHandle,
                ActiveCombatTargetLookup = _activeCombatTargetLookup,
                SourceLookup = _sourceLookup,
                DeadLookup = _deadLookup,
                PlacedRefStateLookup = _placedRefStateLookup,
                PlacedRefLookup = _placedRefLookup,
                LocalToWorldLookup = _localToWorldLookup,
                BoundsLookup = _boundsLookup,
                PlayerLookup = _playerLookup,
                TraversalPendingLookup = _traversalPendingLookup,
                TraversalAwaitingLookup = _traversalAwaitingLookup,
                PackageLookup = _packageLookup,
                EquipmentLookup = _equipmentLookup,
            }.ScheduleParallel(_query, systemState.Dependency);
        }

        [BurstCompile]
        struct CombatMovementJob : IJobChunk
        {
            [ReadOnly] public BlobAssetReference<RuntimeContentBlob> Content;
            public float DeltaTime;
            public float ElapsedTime;
            [ReadOnly] public EntityTypeHandle EntityHandle;
            [ReadOnly] public ComponentTypeHandle<ActorSpawnSource> SourceHandle;
            [ReadOnly] public ComponentTypeHandle<ActorDead> DeadHandle;
            [ReadOnly] public ComponentTypeHandle<ActorActiveCombatTarget> ActiveCombatTargetHandle;
            public ComponentTypeHandle<ActorCombatMovementState> CombatMovementHandle;
            public ComponentTypeHandle<ActorAiState> AiStateHandle;
            public ComponentTypeHandle<LocalTransform> TransformHandle;
            [ReadOnly] public ComponentTypeHandle<ActorLocalBounds> BoundsHandle;
            public ComponentTypeHandle<MorrowindMovementInput> MovementInputHandle;
            [ReadOnly] public ComponentTypeHandle<PathGridTraversalState> TraversalStateHandle;
            [ReadOnly] public BufferTypeHandle<ActorAiPackageRuntime> PackageHandle;
            [ReadOnly] public ComponentLookup<ActorActiveCombatTarget> ActiveCombatTargetLookup;
            [ReadOnly] public ComponentLookup<ActorSpawnSource> SourceLookup;
            [ReadOnly] public ComponentLookup<ActorDead> DeadLookup;
            [ReadOnly] public ComponentLookup<PlacedRefRuntimeState> PlacedRefStateLookup;
            [ReadOnly] public ComponentLookup<PlacedRefIdentity> PlacedRefLookup;
            [ReadOnly] public ComponentLookup<LocalToWorld> LocalToWorldLookup;
            [ReadOnly] public ComponentLookup<ActorLocalBounds> BoundsLookup;
            [ReadOnly] public ComponentLookup<PlayerCharacterComponent> PlayerLookup;
            [ReadOnly] public ComponentLookup<PathGridTraversalPendingRequest> TraversalPendingLookup;
            [ReadOnly] public ComponentLookup<PathGridTraversalAwaitingResult> TraversalAwaitingLookup;
            [ReadOnly] public BufferLookup<ActorAiPackageRuntime> PackageLookup;
            [ReadOnly] public BufferLookup<ActorEquipmentSlot> EquipmentLookup;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
            {
                var entities = chunk.GetNativeArray(EntityHandle);
                var sources = chunk.GetNativeArray(ref SourceHandle);
                var activeTargets = chunk.GetNativeArray(ref ActiveCombatTargetHandle);
                var combatMoves = chunk.GetNativeArray(ref CombatMovementHandle);
                var aiStates = chunk.GetNativeArray(ref AiStateHandle);
                var transforms = chunk.GetNativeArray(ref TransformHandle);
                var bounds = chunk.GetNativeArray(ref BoundsHandle);
                var inputs = chunk.GetNativeArray(ref MovementInputHandle);
                var traversalStates = chunk.GetNativeArray(ref TraversalStateHandle);
                var packages = chunk.GetBufferAccessor(ref PackageHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    Entity actor = entities[i];
                    var combatMove = combatMoves[i];
                    var aiState = aiStates[i];

                    if (!chunk.IsComponentEnabled(ref ActiveCombatTargetHandle, i)
                        || chunk.IsComponentEnabled(ref DeadHandle, i))
                    {
                        ResetCombatMoveState(ref combatMove);
                        combatMoves[i] = combatMove;
                        continue;
                    }

                    if (IsDisabled(actor) || HasActiveTraversal(actor, traversalStates[i]))
                    {
                        ResetCombatMoveState(ref combatMove);
                        combatMoves[i] = combatMove;
                        continue;
                    }

                    var active = activeTargets[i];
                    Entity target = active.TargetEntity;
                    if (target == Entity.Null
                        || !LocalToWorldLookup.HasComponent(target)
                        || IsDead(target)
                        || IsDisabled(target))
                    {
                        ResetCombatMoveState(ref combatMove);
                        combatMoves[i] = combatMove;
                        continue;
                    }

                    if (!IsCurrentCombatPackage(packages[i], aiState, target, active.TargetPlacedRefId))
                    {
                        ResetCombatMoveState(ref combatMove);
                        combatMoves[i] = combatMove;
                        continue;
                    }

                    LocalTransform transform = transforms[i];
                    float3 flatDelta = LocalToWorldLookup[target].Position - transform.Position;
                    flatDelta.y = 0f;
                    float distanceSq = math.lengthsq(flatDelta);
                    if (distanceSq <= 1e-6f)
                    {
                        ResetCombatMoveState(ref combatMove);
                        combatMoves[i] = combatMove;
                        continue;
                    }

                    float3 targetDirection = flatDelta * math.rsqrt(distanceSq);
                    float angleToTarget = SignedYawToDirection(transform.Rotation, targetDirection);
                    float turn = 1f - math.exp(-FacingSharpness * DeltaTime);
                    transform.Rotation = math.slerp(
                        transform.Rotation,
                        quaternion.LookRotationSafe(targetDirection, math.up()),
                        math.saturate(turn));

                    var input = inputs[i];
                    input.LocalMove = float2.zero;
                    input.RunHeld = false;
                    input.SneakHeld = false;
                    input.JumpPressed = false;

                    if (combatMove.LateralMoveUntilTime <= ElapsedTime && combatMove.NextLateralMoveTime <= ElapsedTime)
                        TryStartLateralMove(actor, sources[i], bounds[i], transform, target, angleToTarget, ref aiState, ref combatMove);

                    if (combatMove.LateralMoveUntilTime > ElapsedTime && combatMove.LateralDirection != 0)
                    {
                        input.LocalMove = new float2(combatMove.LateralDirection, 0f);
                        input.RunHeld = true;
                    }

                    transforms[i] = transform;
                    inputs[i] = input;
                    combatMoves[i] = combatMove;
                    aiStates[i] = aiState;
                }
            }

            void TryStartLateralMove(
                Entity actor,
                in ActorSpawnSource source,
                in ActorLocalBounds actorBounds,
                in LocalTransform actorTransform,
                Entity target,
                float angleToTarget,
                ref ActorAiState aiState,
                ref ActorCombatMovementState combatMove)
            {
                combatMove.LateralMoveUntilTime = 0f;
                combatMove.LateralDirection = 0;

                if (!IsNpc(source))
                    return;

                var random = new Unity.Mathematics.Random(aiState.RandomSeed == 0u ? BuildSeed(actor) : aiState.RandomSeed);

                float moveDuration = 0f;
                float absAngleToTarget = math.abs(angleToTarget);
                if (absAngleToTarget > SidestepAngleRadians)
                {
                    moveDuration = 0.2f;
                }
                else
                {
                    float distanceToBounds = ResolveDistanceToBounds(actorBounds, actorTransform, target);
                    float targetReach = ResolveCombatReach(target);
                    if (distanceToBounds <= targetReach && random.NextFloat() < 0.25f)
                        moveDuration = 0.1f + 0.1f * random.NextFloat();
                }

                if (moveDuration > 0f)
                {
                    combatMove.LateralDirection = absAngleToTarget > SidestepAngleRadians
                        ? (angleToTarget > 0f ? (sbyte)1 : (sbyte)(-1))
                        : (random.NextFloat() < 0.5f ? (sbyte)1 : (sbyte)(-1));
                    combatMove.LateralMoveUntilTime = ElapsedTime + moveDuration;
                    combatMove.NextLateralMoveTime = combatMove.LateralMoveUntilTime
                                                       + SidestepDecisionCooldownMin
                                                       + SidestepDecisionCooldownRange * random.NextFloat();
                }

                aiState.RandomSeed = random.state == 0u ? 1u : random.state;
            }

            static bool IsCurrentCombatPackage(DynamicBuffer<ActorAiPackageRuntime> packages, in ActorAiState aiState, Entity target, uint targetPlacedRefId)
            {
                if ((uint)aiState.CurrentPackageIndex >= (uint)packages.Length)
                    return false;

                ActorAiPackageRuntime package = packages[aiState.CurrentPackageIndex];
                return package.Type == (byte)ActorAiRuntimePackageType.Combat
                       && package.FollowTargetEntity == target
                       && package.FollowTargetPlacedRefId == targetPlacedRefId;
            }

            bool HasActiveTraversal(Entity actor, in PathGridTraversalState traversal)
            {
                if (traversal.Status == (byte)PathGridTraversalStatus.Traversing
                    || traversal.Status == (byte)PathGridTraversalStatus.RequestingPath
                    || traversal.ActivePathRequestId > 0)
                {
                    return true;
                }

                bool hasPendingRequest = TraversalPendingLookup.HasComponent(actor)
                                         && TraversalPendingLookup.IsComponentEnabled(actor);
                bool awaitingResult = TraversalAwaitingLookup.HasComponent(actor)
                                      && TraversalAwaitingLookup.IsComponentEnabled(actor);
                return hasPendingRequest || awaitingResult;
            }

            static void ResetCombatMoveState(ref ActorCombatMovementState combatMove)
            {
                combatMove.LateralMoveUntilTime = 0f;
                combatMove.NextLateralMoveTime = 0f;
                combatMove.LateralDirection = 0;
            }

            static float SignedYawToDirection(quaternion rotation, float3 targetDirection)
            {
                float3 forward = math.normalizesafe(math.rotate(rotation, new float3(0f, 0f, 1f)), new float3(0f, 0f, 1f));
                forward.y = 0f;
                forward = math.normalizesafe(forward, new float3(0f, 0f, 1f));
                float dot = math.clamp(math.dot(forward, targetDirection), -1f, 1f);
                float cross = forward.x * targetDirection.z - forward.z * targetDirection.x;
                return math.atan2(cross, dot);
            }

            float ResolveDistanceToBounds(in ActorLocalBounds actorBounds, in LocalTransform actorTransform, Entity target)
            {
                float actorRadius = math.max(actorBounds.Extents.x, actorBounds.Extents.z) * math.max(0.01f, actorTransform.Scale);
                float targetRadius = ResolveTargetRadius(target);
                float3 delta = LocalToWorldLookup[target].Position - actorTransform.Position;
                delta.y = 0f;
                return math.max(0f, math.length(delta) - actorRadius - targetRadius);
            }

            float ResolveTargetRadius(Entity target)
            {
                if (BoundsLookup.HasComponent(target))
                {
                    ActorLocalBounds bounds = BoundsLookup[target];
                    return math.max(bounds.Extents.x, bounds.Extents.z) * ResolveUniformScale(LocalToWorldLookup[target]);
                }

                if (PlayerLookup.HasComponent(target))
                    return math.max(0.01f, PlayerLookup[target].Radius);

                throw new InvalidOperationException($"[VVardenfell][CombatMove] Target entity={target.Index}:{target.Version} has no ActorLocalBounds or PlayerCharacterComponent.");
            }

            static float ResolveUniformScale(in LocalToWorld localToWorld)
            {
                float3 x = localToWorld.Value.c0.xyz;
                float3 y = localToWorld.Value.c1.xyz;
                float3 z = localToWorld.Value.c2.xyz;
                return math.max(0.01f, math.cmax(new float3(math.length(x), math.length(y), math.length(z))));
            }

            float ResolveCombatReach(Entity actor)
            {
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
                    return MorrowindMeleeCombatMechanics.ComputeMeleeReach(ref content, hasWeapon, weapon);
                }

                if (IsCreature(actor))
                {
                    ref RuntimeContentBlob content = ref Content.Value;
                    return MorrowindMeleeCombatMechanics.ComputeMeleeReach(ref content, false, default);
                }

                throw new InvalidOperationException($"[VVardenfell][CombatMove] NPC ref=0x{PlacedRefId(actor):X8} has no ActorEquipmentSlot buffer.");
            }

            bool IsNpc(in ActorSpawnSource source)
                => ResolveActorKind(source) == ActorDefKind.Npc;

            bool IsCreature(Entity actor)
            {
                if (!SourceLookup.HasComponent(actor))
                    return false;

                return ResolveActorKind(SourceLookup[actor]) == ActorDefKind.Creature;
            }

            ActorDefKind ResolveActorKind(in ActorSpawnSource source)
            {
                if (!source.Definition.IsValid)
                    throw new InvalidOperationException("[VVardenfell][CombatMove] Actor has invalid ActorSpawnSource.");
                if (!Content.IsCreated || (uint)source.Definition.Index >= (uint)Content.Value.Actors.Length)
                    throw new InvalidOperationException("[VVardenfell][CombatMove] Actor has out-of-range ActorSpawnSource.");
                return Content.Value.Actors[source.Definition.Index].Kind;
            }

            bool IsDead(Entity actor)
                => DeadLookup.HasComponent(actor) && DeadLookup.IsComponentEnabled(actor);

            bool IsDisabled(Entity actor)
                => PlacedRefStateLookup.HasComponent(actor) && PlacedRefStateLookup[actor].Disabled != 0;

            uint BuildSeed(Entity actor)
            {
                uint seed = PlacedRefId(actor);
                if (seed == 0u)
                    seed = (uint)(actor.Index + 1) * 16777619u ^ (uint)(actor.Version + 1);
                return seed == 0u ? 1u : seed;
            }

            uint PlacedRefId(Entity entity)
                => PlacedRefLookup.HasComponent(entity) ? PlacedRefLookup[entity].Value : 0u;
        }
    }
}
