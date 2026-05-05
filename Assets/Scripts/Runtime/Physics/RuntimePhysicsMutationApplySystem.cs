using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Physics
{
    [UpdateInGroup(typeof(MorrowindPhysicsPreBuildSystemGroup), OrderLast = true)]
    public partial class RuntimePhysicsMutationApplySystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<RuntimePhysicsMutationQueueTag>();
        }

        protected override void OnUpdate()
        {
            CompleteDependency();

            Entity queueEntity = SystemAPI.GetSingletonEntity<RuntimePhysicsMutationQueueTag>();
            var mutations = EntityManager.GetBuffer<RuntimePhysicsMutationRequest>(queueEntity);
            var flush = EntityManager.GetComponentData<PhysicsFlushRequested>(queueEntity);
            if (mutations.Length == 0 && flush.Pending == 0)
                return;

            using var snapshot = mutations.ToNativeArray(Allocator.Temp);
            mutations.Clear();
            EntityManager.SetComponentData(queueEntity, new PhysicsFlushRequested());

            for (int i = 0; i < snapshot.Length; i++)
                Apply(snapshot[i]);

            LogPhysicsBodyDiagnosticsIfChanged();
        }

        void Apply(in RuntimePhysicsMutationRequest request)
        {
            switch (request.Kind)
            {
                case RuntimePhysicsMutationKind.Enable:
                    RuntimeColliderPhysicsUtility.EnablePhysics(EntityManager, request.Entity);
                    break;
                case RuntimePhysicsMutationKind.Disable:
                    RuntimeColliderPhysicsUtility.DisablePhysics(EntityManager, request.Entity);
                    break;
                case RuntimePhysicsMutationKind.AttachSource:
                    RuntimeColliderPhysicsUtility.AttachSource(
                        EntityManager,
                        request.Entity,
                        request.Collider,
                        request.ColliderKind,
                        request.Active != 0,
                        request.Temporary != 0);
                    break;
                case RuntimePhysicsMutationKind.SetPhysicsCollider:
                    ApplyPhysicsColliderSwap(request.Entity, request.Collider);
                    break;
                default:
                    throw new InvalidOperationException($"[VVardenfell][Physics] Unsupported runtime physics mutation kind {request.Kind}.");
            }
        }

        void ApplyPhysicsColliderSwap(Entity entity, BlobAssetReference<Unity.Physics.Collider> collider)
        {
            if (entity == Entity.Null || !EntityManager.Exists(entity))
                return;
            if (!collider.IsCreated)
                throw new InvalidOperationException("[VVardenfell][Physics] Cannot apply an empty PhysicsCollider swap.");

            var physicsCollider = new PhysicsCollider { Value = collider };
            if (EntityManager.HasComponent<PhysicsCollider>(entity))
            {
                EntityManager.SetComponentData(entity, physicsCollider);
                RuntimeColliderPhysicsUtility.EnsureTemporalCoherence(EntityManager, entity, resetInfo: true);
            }
            else
            {
                EntityManager.AddComponentData(entity, physicsCollider);
                RuntimeColliderPhysicsUtility.EnsureTemporalCoherence(EntityManager, entity, resetInfo: true);
            }

            if (!EntityManager.HasComponent<PhysicsWorldIndex>(entity))
                EntityManager.AddSharedComponent(entity, new PhysicsWorldIndex { Value = 0 });
            RuntimeColliderPhysicsUtility.EnsureTemporalCoherence(EntityManager, entity, resetInfo: false);
            if (!EntityManager.HasComponent<PhysicsVelocity>(entity))
                EntityManager.AddComponentData(entity, new PhysicsVelocity());
        }

        RuntimePhysicsBodyCounts _lastBodyCounts;
        bool _hasBodyCounts;

        void LogPhysicsBodyDiagnosticsIfChanged()
        {
            var counts = new RuntimePhysicsBodyCounts();
            foreach (var (_, entity) in SystemAPI.Query<RefRO<PhysicsCollider>>().WithEntityAccess())
            {
                RuntimeColliderKind kind = RuntimeColliderKind.None;
                if (EntityManager.HasComponent<RuntimeColliderSource>(entity))
                {
                    kind = EntityManager.GetComponentData<RuntimeColliderSource>(entity).Kind;
                }
                else if (EntityManager.HasComponent<PlayerTag>(entity))
                {
                    kind = RuntimeColliderKind.Player;
                }
                else
                {
                    counts.MissingRuntimeColliderSource++;
                }

                bool hasVelocity = EntityManager.HasComponent<PhysicsVelocity>(entity);
                bool hasMass = EntityManager.HasComponent<PhysicsMass>(entity);
                bool kinematicInteractionPick =
                    EntityManager.HasComponent<InteractionActorPickSurfaceTag>(entity)
                    || EntityManager.HasComponent<InteractionActivationProxyTag>(entity);
                counts.Add(kind, hasVelocity, hasMass, kinematicInteractionPick);
            }

            if (_hasBodyCounts && counts.Equals(_lastBodyCounts))
                return;

            _lastBodyCounts = counts;
            _hasBodyCounts = true;

            UnityEngine.Debug.Log(
                "[VVardenfell][PhysicsDiagnostics] Active PhysicsCollider bodies: "
                + $"total={counts.Total}, static={counts.Static}, kinematic={counts.Kinematic}, dynamic={counts.Dynamic}, "
                + $"missingRuntimeColliderSource={counts.MissingRuntimeColliderSource}; "
                + $"kinds none={counts.None}, terrain={counts.TerrainCell}, staticCell={counts.StaticCell}, placedRef={counts.PlacedRef}, "
                + $"activationProxy={counts.ActivationProxy}, runtimeSpawn={counts.RuntimeSpawn}, player={counts.Player}, actor={counts.Actor}, "
                + $"interactionPick={counts.InteractionPick}, projectile={counts.Projectile}; "
                + $"movingPlacedRef={counts.MovingPlacedRef}, movingStaticGeometry={counts.MovingStaticGeometry}, "
                + $"kinematicInteractionPick={counts.KinematicInteractionPick}");

            if (counts.MovingPlacedRef > 0 || counts.MovingStaticGeometry > 0)
            {
                UnityEngine.Debug.LogWarning(
                    "[VVardenfell][PhysicsDiagnostics] Static geometry has PhysicsVelocity/PhysicsMass classification: "
                    + $"movingPlacedRef={counts.MovingPlacedRef}, movingStaticGeometry={counts.MovingStaticGeometry}.");
            }
        }

        struct RuntimePhysicsBodyCounts
        {
            public int Total;
            public int Static;
            public int Kinematic;
            public int Dynamic;
            public int MissingRuntimeColliderSource;
            public int None;
            public int TerrainCell;
            public int StaticCell;
            public int PlacedRef;
            public int ActivationProxy;
            public int RuntimeSpawn;
            public int Player;
            public int Actor;
            public int InteractionPick;
            public int Projectile;
            public int MovingPlacedRef;
            public int MovingStaticGeometry;
            public int KinematicInteractionPick;

            public void Add(RuntimeColliderKind kind, bool hasVelocity, bool hasMass, bool kinematicInteractionPick)
            {
                Total++;
                if (hasMass)
                    Dynamic++;
                else if (hasVelocity)
                    Kinematic++;
                else
                    Static++;

                switch (kind)
                {
                    case RuntimeColliderKind.TerrainCell:
                        TerrainCell++;
                        if (hasVelocity || hasMass)
                            MovingStaticGeometry++;
                        break;
                    case RuntimeColliderKind.StaticCell:
                        StaticCell++;
                        if (hasVelocity || hasMass)
                            MovingStaticGeometry++;
                        break;
                    case RuntimeColliderKind.PlacedRef:
                        PlacedRef++;
                        if (hasVelocity || hasMass)
                            MovingPlacedRef++;
                        break;
                    case RuntimeColliderKind.ActivationProxy:
                        ActivationProxy++;
                        break;
                    case RuntimeColliderKind.RuntimeSpawn:
                        RuntimeSpawn++;
                        break;
                    case RuntimeColliderKind.Player:
                        Player++;
                        break;
                    case RuntimeColliderKind.Actor:
                        Actor++;
                        break;
                    case RuntimeColliderKind.InteractionPick:
                        InteractionPick++;
                        if (hasVelocity || hasMass)
                        {
                            if (kinematicInteractionPick)
                                KinematicInteractionPick++;
                            else
                            MovingStaticGeometry++;
                        }
                        break;
                    case RuntimeColliderKind.Projectile:
                        Projectile++;
                        break;
                    case RuntimeColliderKind.None:
                    default:
                        None++;
                        break;
                }
            }

            public bool Equals(RuntimePhysicsBodyCounts other)
            {
                return Total == other.Total
                       && Static == other.Static
                       && Kinematic == other.Kinematic
                       && Dynamic == other.Dynamic
                       && MissingRuntimeColliderSource == other.MissingRuntimeColliderSource
                       && None == other.None
                       && TerrainCell == other.TerrainCell
                       && StaticCell == other.StaticCell
                       && PlacedRef == other.PlacedRef
                       && ActivationProxy == other.ActivationProxy
                       && RuntimeSpawn == other.RuntimeSpawn
                       && Player == other.Player
                       && Actor == other.Actor
                       && InteractionPick == other.InteractionPick
                       && Projectile == other.Projectile
                       && MovingPlacedRef == other.MovingPlacedRef
                       && MovingStaticGeometry == other.MovingStaticGeometry
                       && KinematicInteractionPick == other.KinematicInteractionPick;
            }
        }
    }
}
