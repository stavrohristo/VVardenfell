using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Player;

namespace VVardenfell.Runtime.Movement
{
    public static partial class MorrowindActorMovementSolver
    {
        readonly struct GroundSupportResult
        {
            public readonly MorrowindSupportKind Kind;
            public readonly float3 Normal;
            public readonly float3 HitPosition;
            public readonly float3 SupportedPosition;
            public readonly Entity StandingOn;
            public readonly float Fraction;
            public readonly float ProbeDistance;
            public readonly bool RejectedSteep;

            GroundSupportResult(
                MorrowindSupportKind kind,
                float3 normal,
                float3 hitPosition,
                float3 supportedPosition,
                Entity standingOn,
                float fraction,
                float probeDistance,
                bool rejectedSteep)
            {
                Kind = kind;
                Normal = normal;
                HitPosition = hitPosition;
                SupportedPosition = supportedPosition;
                StandingOn = standingOn;
                Fraction = fraction;
                ProbeDistance = probeDistance;
                RejectedSteep = rejectedSteep;
            }

            public static GroundSupportResult None(float3 position, bool rejectedSteep = false)
                => new(MorrowindSupportKind.None, math.up(), position, position, Entity.Null, 1f, 0f, rejectedSteep);

            public static GroundSupportResult Create(
                MorrowindSupportKind kind,
                float3 normal,
                float3 hitPosition,
                float3 supportedPosition,
                Entity standingOn,
                float fraction,
                float probeDistance,
                bool rejectedSteep = false)
                => new(kind, normal, hitPosition, supportedPosition, standingOn, fraction, probeDistance, rejectedSteep);

            public bool HasSolidSupport =>
                Kind == MorrowindSupportKind.FlatGround
                || Kind == MorrowindSupportKind.WalkableSlope
                || Kind == MorrowindSupportKind.RecoveryFlat;
        }

        static MorrowindSupportKind ResolvePreviousSupportKind(
            in MorrowindMovementFrameTrace previousTrace,
            in MorrowindActorKinematicState kinematic,
            EntityManager entityManager)
        {
            if (previousTrace.SupportKind != 0)
                return (MorrowindSupportKind)previousTrace.SupportKind;

            if (kinematic.WalkingOnWater)
                return MorrowindSupportKind.WaterSurfaceCandidate;

            if (!kinematic.Grounded)
                return MorrowindSupportKind.None;

            if (IsActorSupport(entityManager, kinematic.StandingOn))
                return MorrowindSupportKind.ActorTop;

            return MorrowindSupportKind.FlatGround;
        }

        static GroundSupportResult FindGroundSupport(
            EntityManager entityManager,
            in CollisionWorld world,
            in PhysicsCollider collider,
            float3 position,
            in MorrowindMovementTuning tuning,
            bool wasGrounded,
            bool allowRecoveryFallback)
        {
            if (allowRecoveryFallback)
            {
                return GroundSupportResult.Create(
                    MorrowindSupportKind.RecoveryFlat,
                    math.up(),
                    position,
                    position,
                    Entity.Null,
                    0f,
                    0f);
            }

            float dropDistance = 2f * tuning.GroundOffset + (wasGrounded ? tuning.StepSizeDown : 0f);
            var castInput = new ColliderCastInput(
                collider.Value,
                position,
                position - new float3(0f, dropDistance, 0f),
                quaternion.identity);

            if (!world.CastCollider(castInput, out ColliderCastHit hit))
                return GroundSupportResult.None(position);

            float3 hitPosition = position - new float3(0f, dropDistance * hit.Fraction, 0f);
            float3 supportedPosition = hitPosition + new float3(0f, tuning.GroundOffset, 0f);
            bool walkable = IsWalkableSlope(hit.SurfaceNormal, tuning.MaxSlopeCosine);
            bool actorTop = IsActorSupport(entityManager, hit.Entity);

            if (actorTop)
            {
                return walkable
                    ? GroundSupportResult.Create(
                        MorrowindSupportKind.ActorTop,
                        hit.SurfaceNormal,
                        hitPosition,
                        supportedPosition,
                        hit.Entity,
                        hit.Fraction,
                        dropDistance)
                    : GroundSupportResult.None(position, rejectedSteep: true);
            }

            if (!walkable)
                return GroundSupportResult.None(position, rejectedSteep: true);

            MorrowindSupportKind kind = hit.SurfaceNormal.y >= SupportSlopeReportingThreshold
                ? MorrowindSupportKind.FlatGround
                : MorrowindSupportKind.WalkableSlope;

            return GroundSupportResult.Create(
                kind,
                hit.SurfaceNormal,
                hitPosition,
                supportedPosition,
                hit.Entity,
                hit.Fraction,
                dropDistance);
        }

        static void ApplySupportResult(
            in CollisionWorld world,
            in PhysicsCollider collider,
            in MorrowindMovementTuning tuning,
            ref float3 position,
            ref MorrowindActorKinematicState kinematic,
            ref MorrowindMovementFrameTrace trace,
            in GroundSupportResult support,
            bool hadSolidGroundBeforeMove)
        {
            trace.SupportKind = (byte)support.Kind;
            trace.SupportRejectedSteep = ToByte(support.RejectedSteep);
            trace.GroundNormal = support.Normal;
            trace.StandingOn = support.StandingOn;
            kinematic.WalkingOnWater = support.Kind == MorrowindSupportKind.WaterSurfaceCandidate;

            if (support.Kind == MorrowindSupportKind.ActorTop)
            {
                if (support.SupportedPosition.y <= position.y)
                    position.y = support.SupportedPosition.y;

                kinematic.Grounded = false;
                kinematic.OnSlope = false;
                kinematic.StandingOn = support.StandingOn;
                return;
            }

            if (support.Kind != MorrowindSupportKind.RecoveryFlat)
            {
                kinematic.StuckFrames = 0;
                kinematic.LastStuckPosition = position;
            }

            if (!support.HasSolidSupport)
            {
                kinematic.Grounded = false;
                kinematic.OnSlope = false;
                kinematic.StandingOn = Entity.Null;
                return;
            }

            kinematic.Grounded = true;
            kinematic.OnSlope = false;
            kinematic.StandingOn = support.StandingOn;

            if (!hadSolidGroundBeforeMove && kinematic.Inertia.y <= 0f)
                trace.LandingConsumedInertia = 1;

            ApplyLandingSnap(world, collider, tuning, ref position, ref trace, support, hadSolidGroundBeforeMove);
            kinematic.Inertia = float3.zero;
        }

        static void ApplyLandingSnap(
            in CollisionWorld world,
            in PhysicsCollider collider,
            in MorrowindMovementTuning tuning,
            ref float3 position,
            ref MorrowindMovementFrameTrace trace,
            in GroundSupportResult support,
            bool hadSolidGroundBeforeMove)
        {
            float hitDistance = support.Fraction * support.ProbeDistance;
            float flatGroundSnapTolerance = math.max(tuning.CollisionMargin, tuning.GroundOffset * FlatGroundSnapToleranceScale);
            bool withinFlatGroundTolerance = hitDistance <= tuning.GroundOffset + flatGroundSnapTolerance;
            bool routineSupportedSurface = hadSolidGroundBeforeMove
                && (support.Kind == MorrowindSupportKind.FlatGround || support.Kind == MorrowindSupportKind.WalkableSlope);

            if (support.Kind == MorrowindSupportKind.RecoveryFlat || hitDistance > tuning.GroundOffset)
            {
                if (routineSupportedSurface
                    && withinFlatGroundTolerance)
                {
                    trace.GroundProbeSnapped = 0;
                    trace.SupportSnapMode = (byte)MorrowindSupportSnapMode.None;
                    return;
                }

                trace.GroundProbeSnapped = 1;
                position = support.SupportedPosition;
                trace.SupportSnapMode = (byte)MorrowindSupportSnapMode.Offset;
                return;
            }

            if (routineSupportedSurface)
            {
                trace.GroundProbeSnapped = 0;
                trace.SupportSnapMode = (byte)MorrowindSupportSnapMode.None;
                return;
            }

            float3 start = support.HitPosition;
            float3 settleEnd = start + new float3(0f, 2f * tuning.GroundOffset, 0f);
            var settleCast = new ColliderCastInput(collider.Value, start, settleEnd, quaternion.identity);
            float3 settleHitPosition = settleEnd;
            if (world.CastCollider(settleCast, out ColliderCastHit settleHit))
                settleHitPosition = start + new float3(0f, 2f * tuning.GroundOffset * settleHit.Fraction, 0f);

            trace.GroundProbeSnapped = 1;
            position = (start + settleHitPosition) * 0.5f;
            trace.SupportSnapMode = (byte)MorrowindSupportSnapMode.Settle;
        }

        static bool IsWalkableSlope(float3 normal, float maxSlopeCosine) => normal.y > maxSlopeCosine;

        static bool IsActorSupport(EntityManager entityManager, Entity entity)
        {
            return entity != Entity.Null
                && entityManager.Exists(entity)
                && (entityManager.HasComponent<PlayerTag>(entity) || entityManager.HasComponent<PassiveActorPresence>(entity));
        }

        static byte ToByte(bool value) => value ? (byte)1 : (byte)0;
    }
}
