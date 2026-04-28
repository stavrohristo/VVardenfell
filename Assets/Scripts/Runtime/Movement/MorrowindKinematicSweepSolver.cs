using Unity.Mathematics;
using Unity.Physics;
using VVardenfell.Core;

namespace VVardenfell.Runtime.Movement
{
    public static partial class MorrowindActorMovementSolver
    {
        static void MoveKinematic(
            in CollisionWorld world,
            in PhysicsCollider collider,
            in MorrowindMovementSettings tuning,
            ref float3 position,
            ref float3 velocity,
            float time,
            bool startedGrounded,
            ref MovementSolveScratch scratch)
        {
            float remainingTime = time;
            float3 originalVelocity = velocity;
            float3 lastSlideNormal = math.up();
            float3 lastSlideNormalFallback = math.up();
            int slideCount = 0;

            for (int iteration = 0; iteration < tuning.MaxIterations && remainingTime > 0.0001f; iteration++)
            {
                float3 move = velocity * remainingTime;
                if (math.lengthsq(move) <= MinMoveEpsilon)
                    break;

                var castInput = new ColliderCastInput(collider.Value, position, position + move, quaternion.identity);
                if (!world.CastCollider(castInput, out ColliderCastHit hit))
                {
                    position += move;
                    break;
                }

                bool seenGround = startedGrounded || IsWalkableSlope(hit.SurfaceNormal, tuning.MaxSlopeCosine);
                float3 oldPosition = position;
                bool stepped = false;
                if (hit.SurfaceNormal.y < tuning.MaxSlopeCosine)
                {
                    stepped = TryStep(world, collider, tuning, ref position, ref velocity, ref remainingTime, startedGrounded, iteration == 0);
                }

                if (stepped)
                {
                    scratch.StepSucceeded = true;
                    continue;
                }

                position = oldPosition;
                remainingTime *= 1f - hit.Fraction;

                float3 planeNormal = hit.SurfaceNormal;
                float3 originalPlaneNormal = planeNormal;
                if (seenGround && !IsWalkableSlope(planeNormal, tuning.MaxSlopeCosine) && math.abs(planeNormal.y) > 0.0001f)
                {
                    planeNormal = math.normalizesafe(new float3(planeNormal.x, 0f, planeNormal.z), planeNormal);
                }

                float3 direction = math.normalizesafe(velocity);
                position += move * hit.Fraction;
                position -= direction * tuning.CollisionMargin;

                float3 newVelocity = math.dot(velocity, planeNormal) <= 0f
                    ? Reject(velocity, planeNormal)
                    : velocity;

                bool usedSeamLogic = false;
                if (slideCount > 0)
                {
                    float dotA = math.dot(lastSlideNormal, originalPlaneNormal);
                    float dotB = slideCount <= 1 ? 1f : math.dot(lastSlideNormalFallback, originalPlaneNormal);
                    if (dotA <= 0f || dotB <= 0f)
                    {
                        float3 bestNormal = dotB < dotA ? lastSlideNormalFallback : lastSlideNormal;
                        float3 constraint = math.cross(bestNormal, originalPlaneNormal);
                        if (math.lengthsq(constraint) > 0f)
                        {
                            constraint = math.normalize(constraint);
                            newVelocity = Project(velocity, constraint);
                            usedSeamLogic = true;
                        }
                    }
                }

                if (!usedSeamLogic)
                    position += planeNormal * (tuning.CollisionMargin * 2f);

                if (seenGround && math.dot(newVelocity, originalVelocity) <= 0f)
                {
                    float3 perpendicular = math.cross(newVelocity, originalVelocity);
                    if (math.lengthsq(perpendicular) > 0f && math.abs(math.normalize(perpendicular).y) > 0.7071f)
                        break;
                }

                if (!IsWalkableSlope(planeNormal, tuning.MaxSlopeCosine) && !usedSeamLogic)
                    newVelocity.y = math.min(newVelocity.y, velocity.y);

                slideCount++;
                lastSlideNormalFallback = lastSlideNormal;
                lastSlideNormal = originalPlaneNormal;
                velocity = newVelocity;
            }
        }

        static bool TryStep(
            in CollisionWorld world,
            in PhysicsCollider collider,
            in MorrowindMovementSettings tuning,
            ref float3 position,
            ref float3 velocity,
            ref float remainingTime,
            bool onGround,
            bool firstIteration)
        {
            if (math.lengthsq(new float2(velocity.x, velocity.z)) <= MinMoveEpsilon)
                return false;

            float3 up = new float3(0f, tuning.StepSizeUp, 0f);
            var upCast = new ColliderCastInput(collider.Value, position, position + up, quaternion.identity);
            float upDistance;
            if (!world.CastCollider(upCast, out ColliderCastHit upHit))
                upDistance = tuning.StepSizeUp;
            else if (upHit.Fraction * tuning.StepSizeUp > tuning.CollisionMargin)
                upDistance = upHit.Fraction * tuning.StepSizeUp - tuning.CollisionMargin;
            else
                return false;

            float3 toMove = velocity * remainingTime;
            float3 horizontal = new float3(toMove.x, 0f, toMove.z);
            float moveDistance = math.length(horizontal);
            if (moveDistance <= MinMoveEpsilon)
                return false;
            float3 direction = horizontal / moveDistance;

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                if (attempt > 1 && !firstIteration)
                    return false;

                float attemptDistance = attempt == 1
                    ? moveDistance
                    : (attempt == 2
                        ? ExtraStairHackStep * WorldScale.MwUnitsToMeters
                        : ExtraStairHackStep2 * WorldScale.MwUnitsToMeters);
                if (attempt == 3)
                {
                    upDistance = math.min(upDistance, tuning.StepSizeUp);
                }

                float3 start = position + new float3(0f, upDistance, 0f);
                float3 dest = start + direction * attemptDistance;
                var forwardCast = new ColliderCastInput(collider.Value, start, dest, quaternion.identity);
                float3 forwardDest = dest;
                float effectiveDistance = attemptDistance;
                if (world.CastCollider(forwardCast, out ColliderCastHit forwardHit))
                {
                    effectiveDistance *= forwardHit.Fraction;
                    if (effectiveDistance <= tuning.CollisionMargin)
                        return false;

                    effectiveDistance -= tuning.CollisionMargin;
                    forwardDest = start + direction * effectiveDistance;
                    forwardDest += forwardHit.SurfaceNormal * tuning.CollisionMargin;
                }

                float downStepSize = attempt > 2
                    ? upDistance
                    : effectiveDistance + upDistance + tuning.StepSizeDown;
                var downCast = new ColliderCastInput(
                    collider.Value,
                    forwardDest,
                    forwardDest - new float3(0f, downStepSize, 0f),
                    quaternion.identity);

                if (!world.CastCollider(downCast, out ColliderCastHit downHit) ||
                    !IsWalkableSlope(downHit.SurfaceNormal, tuning.MaxSlopeCosine))
                    continue;

                float downDistance = downHit.Fraction * downStepSize > tuning.CollisionMargin
                    ? downHit.Fraction * downStepSize - tuning.CollisionMargin
                    : 0f;

                if (downDistance - tuning.CollisionMargin - tuning.GroundOffset > upDistance && !onGround)
                    return false;

                float3 newPosition = forwardDest - new float3(0f, downDistance, 0f);
                if (math.lengthsq(position - newPosition) < tuning.CollisionMargin * tuning.CollisionMargin)
                    return false;

                velocity = Reject(velocity, downHit.SurfaceNormal);
                position = newPosition;
                remainingTime *= 1f - math.saturate(effectiveDistance / math.max(moveDistance, MinMoveEpsilon));
                return true;
            }

            return false;
        }

        static float3 Reject(float3 direction, float3 planeNormal)
        {
            float3 normal = math.normalizesafe(planeNormal, math.up());
            return direction - normal * math.dot(direction, normal);
        }

        static float3 Project(float3 direction, float3 axis)
        {
            float3 normal = math.normalizesafe(axis);
            return normal * math.dot(direction, normal);
        }

    }
}
