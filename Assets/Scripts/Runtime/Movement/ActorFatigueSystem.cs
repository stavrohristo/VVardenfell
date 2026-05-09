using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Movement
{
    [UpdateInGroup(typeof(MorrowindPhysicsPostQueryMutationSystemGroup))]
    [BurstCompile]
    public partial struct ActorFatigueSystem : ISystem
    {
        EntityQuery _actorQuery;
        EntityTypeHandle _entityHandle;
        ComponentTypeHandle<ActorSpawnSource> _sourceHandle;
        ComponentTypeHandle<ActorAttributeSet> _attributeHandle;
        ComponentTypeHandle<ActorSkillSet> _skillHandle;
        ComponentTypeHandle<ActorVitalSet> _vitalHandle;
        ComponentTypeHandle<ActorEffectStatModifiers> _effectModifierHandle;
        ComponentTypeHandle<ActorDerivedMovementStats> _derivedHandle;
        ComponentTypeHandle<MorrowindMovementSpeed> _movementSpeedHandle;
        ComponentTypeHandle<MorrowindMovementState> _movementStateHandle;
        ComponentLookup<ActorDead> _deadLookup;
        ComponentLookup<PlacedRefIdentity> _placedRefLookup;

        public void OnCreate(ref SystemState systemState)
        {
            _actorQuery = systemState.GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ActorSpawnSource>(),
                    ComponentType.ReadOnly<ActorDead>(),
                    ComponentType.ReadOnly<ActorAttributeSet>(),
                    ComponentType.ReadOnly<ActorSkillSet>(),
                    ComponentType.ReadWrite<ActorVitalSet>(),
                    ComponentType.ReadOnly<ActorEffectStatModifiers>(),
                    ComponentType.ReadWrite<ActorDerivedMovementStats>(),
                    ComponentType.ReadWrite<MorrowindMovementSpeed>(),
                    ComponentType.ReadOnly<MorrowindMovementState>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<PlayerTag>(),
                },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });

            _entityHandle = systemState.GetEntityTypeHandle();
            _sourceHandle = systemState.GetComponentTypeHandle<ActorSpawnSource>(isReadOnly: true);
            _attributeHandle = systemState.GetComponentTypeHandle<ActorAttributeSet>(isReadOnly: true);
            _skillHandle = systemState.GetComponentTypeHandle<ActorSkillSet>(isReadOnly: true);
            _vitalHandle = systemState.GetComponentTypeHandle<ActorVitalSet>(isReadOnly: false);
            _effectModifierHandle = systemState.GetComponentTypeHandle<ActorEffectStatModifiers>(isReadOnly: true);
            _derivedHandle = systemState.GetComponentTypeHandle<ActorDerivedMovementStats>(isReadOnly: false);
            _movementSpeedHandle = systemState.GetComponentTypeHandle<MorrowindMovementSpeed>(isReadOnly: false);
            _movementStateHandle = systemState.GetComponentTypeHandle<MorrowindMovementState>(isReadOnly: true);
            _deadLookup = systemState.GetComponentLookup<ActorDead>(isReadOnly: true);
            _placedRefLookup = systemState.GetComponentLookup<PlacedRefIdentity>(isReadOnly: true);

            systemState.RequireForUpdate(_actorQuery);
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            float dt = SystemAPI.Time.DeltaTime;
            if (dt <= 0f)
                return;

            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][ContentBlob] Actor fatigue requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;

            _entityHandle.Update(ref systemState);
            _sourceHandle.Update(ref systemState);
            _attributeHandle.Update(ref systemState);
            _skillHandle.Update(ref systemState);
            _vitalHandle.Update(ref systemState);
            _effectModifierHandle.Update(ref systemState);
            _derivedHandle.Update(ref systemState);
            _movementSpeedHandle.Update(ref systemState);
            _movementStateHandle.Update(ref systemState);
            _deadLookup.Update(ref systemState);
            _placedRefLookup.Update(ref systemState);

            systemState.Dependency = new ActorFatigueJob
            {
                Content = contentBlobReference.Blob,
                Gmsts = MorrowindActorMovementStats.LoadGmsts(ref content),
                DeltaTime = dt,
                EntityHandle = _entityHandle,
                SourceHandle = _sourceHandle,
                AttributeHandle = _attributeHandle,
                SkillHandle = _skillHandle,
                VitalHandle = _vitalHandle,
                EffectModifierHandle = _effectModifierHandle,
                DerivedHandle = _derivedHandle,
                MovementSpeedHandle = _movementSpeedHandle,
                MovementStateHandle = _movementStateHandle,
                DeadLookup = _deadLookup,
                PlacedRefLookup = _placedRefLookup,
            }.ScheduleParallel(_actorQuery, systemState.Dependency);
        }

        [BurstCompile]
        struct ActorFatigueJob : IJobChunk
        {
            [ReadOnly] public BlobAssetReference<RuntimeContentBlob> Content;
            public MorrowindActorMovementStats.MovementGmstSet Gmsts;
            public float DeltaTime;
            [ReadOnly] public EntityTypeHandle EntityHandle;
            [ReadOnly] public ComponentTypeHandle<ActorSpawnSource> SourceHandle;
            [ReadOnly] public ComponentTypeHandle<ActorAttributeSet> AttributeHandle;
            [ReadOnly] public ComponentTypeHandle<ActorSkillSet> SkillHandle;
            public ComponentTypeHandle<ActorVitalSet> VitalHandle;
            [ReadOnly] public ComponentTypeHandle<ActorEffectStatModifiers> EffectModifierHandle;
            public ComponentTypeHandle<ActorDerivedMovementStats> DerivedHandle;
            public ComponentTypeHandle<MorrowindMovementSpeed> MovementSpeedHandle;
            [ReadOnly] public ComponentTypeHandle<MorrowindMovementState> MovementStateHandle;
            [ReadOnly] public ComponentLookup<ActorDead> DeadLookup;
            [ReadOnly] public ComponentLookup<PlacedRefIdentity> PlacedRefLookup;

            [BurstCompile]
            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
            {
                var entities = chunk.GetNativeArray(EntityHandle);
                var sources = chunk.GetNativeArray(ref SourceHandle);
                var attributes = chunk.GetNativeArray(ref AttributeHandle);
                var skills = chunk.GetNativeArray(ref SkillHandle);
                var vitals = chunk.GetNativeArray(ref VitalHandle);
                var effectModifiers = chunk.GetNativeArray(ref EffectModifierHandle);
                var derivedStats = chunk.GetNativeArray(ref DerivedHandle);
                var movementSpeeds = chunk.GetNativeArray(ref MovementSpeedHandle);
                var movementStates = chunk.GetNativeArray(ref MovementStateHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    Entity entity = entities[i];
                    if (DeadLookup.IsComponentEnabled(entity))
                        continue;

                    ActorSpawnSource source = sources[i];
                    if (!source.Definition.IsValid)
                        throw new InvalidOperationException($"[VVardenfell][Fatigue] Actor ref={PlacedRefId(entity)} has invalid ActorSpawnSource.");
                    if (!Content.IsCreated || (uint)source.Definition.Index >= (uint)Content.Value.Actors.Length)
                        throw new InvalidOperationException($"[VVardenfell][Fatigue] Actor ref={PlacedRefId(entity)} has an out-of-range ActorSpawnSource.");

                    ActorAttributeSet attributeSet = attributes[i];
                    ActorSkillSet skillSet = skills[i];
                    ActorVitalSet vitalSet = vitals[i];
                    ActorEffectStatModifiers effectModifierSet = effectModifiers[i];
                    ActorDerivedMovementStats derived = derivedStats[i];
                    MorrowindMovementSpeed movementSpeed = movementSpeeds[i];
                    MorrowindMovementState movementState = movementStates[i];

                    var context = new MorrowindActorMovementStats.Context(
                        in Gmsts,
                        attributeSet,
                        skillSet,
                        vitalSet,
                        effectModifierSet,
                        derived,
                        movementSpeed);

                    float fatigue = vitalSet.CurrentFatigue;
                    fatigue -= context.GetMovementFatigueLossPerSecond(
                        movementState.RunHeld && !movementState.SneakHeld,
                        movementState.SneakHeld,
                        movementState.SpeedFactor) * DeltaTime;
                    if (movementState.JumpAccepted)
                        fatigue -= context.GetJumpFatigueLoss();

                    if (fatigue < vitalSet.ModifiedFatigueBase)
                    {
                        float restorePerSecond = Gmsts.FatigueReturnBase + Gmsts.FatigueReturnMult * attributeSet.Endurance;
                        fatigue = math.min(vitalSet.ModifiedFatigueBase, fatigue + restorePerSecond * DeltaTime);
                    }

                    vitalSet.CurrentFatigue = fatigue;
                    ApplyMovementDerived(in Gmsts, vitalSet, ref derived);
                    movementSpeed = BuildMovementSpeed(
                        in Gmsts,
                        Content.Value.Actors[source.Definition.Index].Kind,
                        attributeSet,
                        skillSet,
                        vitalSet,
                        effectModifierSet,
                        derived);

                    vitals[i] = vitalSet;
                    derivedStats[i] = derived;
                    movementSpeeds[i] = movementSpeed;
                }
            }

            uint PlacedRefId(Entity entity)
                => PlacedRefLookup.HasComponent(entity) ? PlacedRefLookup[entity].Value : 0u;
        }

        static void ApplyMovementDerived(
            in MorrowindActorMovementStats.MovementGmstSet gmsts,
            in ActorVitalSet vitals,
            ref ActorDerivedMovementStats derived)
        {
            float modifiedFatigueBase = math.max(0f, vitals.ModifiedFatigueBase);
            float normalizedFatigue = modifiedFatigueBase <= 0f
                ? 1f
                : math.max(0f, vitals.CurrentFatigue / modifiedFatigueBase);
            derived.FatigueTerm = gmsts.FatigueBase - gmsts.FatigueMult * (1f - normalizedFatigue);
        }

        static MorrowindMovementSpeed BuildMovementSpeed(
            in MorrowindActorMovementStats.MovementGmstSet gmsts,
            ActorDefKind actorKind,
            in ActorAttributeSet attributes,
            in ActorSkillSet skills,
            in ActorVitalSet vitals,
            in ActorEffectStatModifiers effectModifiers,
            in ActorDerivedMovementStats derived)
        {
            float jumpSpeed = ComputeJumpSpeed(in gmsts, skills, effectModifiers, derived);
            float jumpMoveFactor = ComputeJumpMoveFactor(in gmsts, skills);
            if (actorKind == ActorDefKind.Creature)
            {
                float walkSpeed = gmsts.MinWalkSpeedCreature
                    + 0.01f * attributes.Speed * (gmsts.MaxWalkSpeedCreature - gmsts.MinWalkSpeedCreature);

                return new MorrowindMovementSpeed
                {
                    WalkSpeed = math.max(0f, walkSpeed),
                    RunSpeed = math.max(0f, walkSpeed),
                    SneakWalkSpeed = math.max(0f, walkSpeed),
                    JumpSpeed = jumpSpeed,
                    JumpRunMultiplier = 1f,
                    JumpMoveFactor = jumpMoveFactor,
                };
            }

            float walkSpeedNpc = ComputeWalkSpeed(in gmsts, attributes, derived);
            float runSpeedNpc = walkSpeedNpc * (0.01f * skills.Athletics * gmsts.AthleticsRunBonus + gmsts.BaseRunMultiplier);
            float sneakWalkSpeedNpc = walkSpeedNpc * gmsts.SneakSpeedMultiplier;

            return new MorrowindMovementSpeed
            {
                WalkSpeed = walkSpeedNpc,
                RunSpeed = math.max(0f, runSpeedNpc),
                SneakWalkSpeed = math.max(0f, sneakWalkSpeedNpc),
                JumpSpeed = jumpSpeed,
                JumpRunMultiplier = gmsts.JumpRunMultiplier,
                JumpMoveFactor = jumpMoveFactor,
            };
        }

        static float ComputeWalkSpeed(
            in MorrowindActorMovementStats.MovementGmstSet gmsts,
            in ActorAttributeSet attributes,
            in ActorDerivedMovementStats derived)
        {
            float walkSpeed = gmsts.MinWalkSpeed + 0.01f * attributes.Speed * (gmsts.MaxWalkSpeed - gmsts.MinWalkSpeed);
            walkSpeed *= 1f - gmsts.EncumberedMoveEffect * derived.NormalizedEncumbrance;
            return math.max(0f, walkSpeed);
        }

        static float ComputeJumpSpeed(
            in MorrowindActorMovementStats.MovementGmstSet gmsts,
            in ActorSkillSet skills,
            in ActorEffectStatModifiers effectModifiers,
            in ActorDerivedMovementStats derived)
        {
            float a = skills.Acrobatics;
            float b = 0f;
            if (a > 50f)
            {
                b = a - 50f;
                a = 50f;
            }

            float jump = gmsts.JumpAcrobaticsBase + math.pow(a / 15f, gmsts.JumpAcroMultiplier);
            jump += 3f * b * gmsts.JumpAcroMultiplier;
            jump += effectModifiers.JumpMagnitude * 64f;
            jump *= gmsts.JumpEncumbranceBase + gmsts.JumpEncumbranceMultiplier * (1f - derived.NormalizedEncumbrance);
            jump *= derived.FatigueTerm;
            jump += 8.96f * (1f / VVardenfell.Core.WorldScale.MwUnitsToMeters);
            jump /= 3f;
            return math.max(0f, jump);
        }

        static float ComputeJumpMoveFactor(
            in MorrowindActorMovementStats.MovementGmstSet gmsts,
            in ActorSkillSet skills)
            => math.min(1f, gmsts.JumpMoveBase + gmsts.JumpMoveMult * skills.Acrobatics / 100f);
    }
}
