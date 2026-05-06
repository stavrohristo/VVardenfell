using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldState;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindGameplayMutationSystemGroup))]
    [UpdateBefore(typeof(MorrowindScriptInterpreterSystem))]
    public partial struct MorrowindScriptPlaceAtApplySystem : ISystem
    {
        const float ActorSafetyLiftMw = 30f;
        const float ActorSafetyTargetLiftMw = 20f;

        EntityQuery _playerQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _playerQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<MorrowindScriptPlaceAtRequest>();
            systemState.RequireForUpdate<RuntimeSpawnState>();
            systemState.RequireForUpdate<RuntimeSpawnRequest>();
            systemState.RequireForUpdate(_playerQuery);
            systemState.RequireForUpdate<MorrowindMovementSettings>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
            systemState.RequireForUpdate<RuntimeWorldCellBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = systemState.EntityManager.GetBuffer<MorrowindScriptPlaceAtRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][ContentBlob] PlaceAtPC requested before runtime content blob was ready.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;

            Entity player = ResolvePlayer();
            var playerTransform = systemState.EntityManager.GetComponentData<LocalTransform>(player);
            bool isInterior = TryResolveInteriorContext(ref systemState, out FixedString128Bytes interiorCellId, out ulong interiorCellHash);
            int2 playerExteriorCell = isInterior ? default : WorldBootstrap.WorldPositionToCell(playerTransform.Position);
            float groundOffset = SystemAPI.GetSingleton<MorrowindMovementSettings>().GroundOffset;

            Entity spawnEntity = SystemAPI.GetSingletonEntity<RuntimeSpawnState>();
            var spawnState = systemState.EntityManager.GetComponentData<RuntimeSpawnState>(spawnEntity);
            var spawnRequests = systemState.EntityManager.GetBuffer<RuntimeSpawnRequest>(spawnEntity);

            for (int requestIndex = 0; requestIndex < requests.Length; requestIndex++)
            {
                var request = requests[requestIndex];
                ValidateRequest(ref content, request, out bool actorSpawn);
                if (request.Count == 0)
                    continue;

                if (!TryResolvePlacementPosition(
                        playerTransform,
                        request.Distance,
                        request.Direction,
                        actorSpawn,
                        isInterior,
                        interiorCellHash,
                        groundOffset,
                        out float3 position))
                {
                    throw new InvalidOperationException("[VVardenfell][MWScript] PlaceAtPC could not find a grounded unblocked placement point.");
                }

                for (int i = 0; i < request.Count; i++)
                {
                    spawnState.NextRequestSequence += 1u;
                    spawnRequests.Add(new RuntimeSpawnRequest
                    {
                        Sequence = spawnState.NextRequestSequence,
                        Content = request.Content,
                        Position = position,
                        Rotation = playerTransform.Rotation,
                        Scale = math.max(0.0001f, playerTransform.Scale),
                        ExteriorCell = playerExteriorCell,
                        InteriorCellId = interiorCellId,
                        InteriorCellHash = interiorCellHash,
                        IsInterior = (byte)(isInterior ? 1 : 0),
                        PersistencePolicy = (byte)RuntimeSpawnPersistencePolicy.CellOwnedSession,
                    });
                }
            }

            systemState.EntityManager.SetComponentData(spawnEntity, spawnState);
            requests.Clear();
        }

        Entity ResolvePlayer()
            => _playerQuery.GetSingletonEntity();

        bool TryResolveInteriorContext(ref SystemState systemState, out FixedString128Bytes interiorCellId, out ulong interiorCellHash)
        {
            interiorCellId = default;
            interiorCellHash = 0UL;
            if (!SystemAPI.TryGetSingleton<InteriorTransitionState>(out var transition) || transition.InteriorActive == 0)
                return false;

            if (transition.ActiveInteriorCellHash == 0UL || transition.ActiveInteriorCellId.IsEmpty)
                throw new InvalidOperationException("[VVardenfell][MWScript] PlaceAtPC active interior context is incomplete.");

            var worldCellReference = SystemAPI.GetSingleton<RuntimeWorldCellBlobReference>();
            if (!worldCellReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][WorldCellBlob] PlaceAtPC requires runtime world cell blob.");
            ref RuntimeWorldCellBlob worldCells = ref worldCellReference.Blob.Value;
            if (!RuntimeWorldCellBlobUtility.TryGetInteriorCellIndex(ref worldCells, transition.ActiveInteriorCellHash, out _))
                throw new InvalidOperationException($"[VVardenfell][MWScript] PlaceAtPC active interior 0x{transition.ActiveInteriorCellHash:X16} is missing from the world cell blob.");

            interiorCellId = transition.ActiveInteriorCellId;
            interiorCellHash = transition.ActiveInteriorCellHash;
            return true;
        }

        static void ValidateRequest(ref RuntimeContentBlob content, in MorrowindScriptPlaceAtRequest request, out bool actorSpawn)
        {
            actorSpawn = false;
            if (request.Count < 0)
                throw new InvalidOperationException("[VVardenfell][MWScript] PlaceAtPC count must be non-negative.");

            if (request.Direction > 3)
                throw new InvalidOperationException("[VVardenfell][MWScript] PlaceAtPC direction must be 0, 1, 2, or 3.");

            if (!request.Content.IsValid || !RuntimeContentBlobUtility.IsValid(ref content, request.Content))
                throw new InvalidOperationException("[VVardenfell][MWScript] PlaceAtPC content reference is invalid.");

            if (request.Content.Kind == ContentReferenceKind.Actor)
            {
                actorSpawn = true;
            }
            else if (request.Content.Kind != ContentReferenceKind.Item && request.Content.Kind != ContentReferenceKind.Light)
            {
                throw new InvalidOperationException("[VVardenfell][MWScript] PlaceAtPC currently supports actors, items, and lights only.");
            }

            if (request.Content.Kind != ContentReferenceKind.Actor && !WorldResources.TryGetRuntimeSpawnPrefab(request.Content, out _))
                throw new InvalidOperationException("[VVardenfell][MWScript] PlaceAtPC content has no runtime spawn prefab.");
        }

        static bool TryResolvePlacementPosition(
            in LocalTransform playerTransform,
            float distance,
            byte requestedDirection,
            bool actorSpawn,
            bool isInterior,
            ulong interiorCellHash,
            float groundOffset,
            out float3 position)
        {
            int[] directions =
            {
                requestedDirection,
                (requestedDirection + 3) % 4,
                (requestedDirection + 2) % 4,
                (requestedDirection + 1) % 4,
            };

            for (int i = 0; i < directions.Length; i++)
            {
                float3 candidate = ComputeDirectionalPosition(playerTransform, distance, directions[i]);
                if (actorSpawn && IsBlockedFromPlayer(candidate, playerTransform.Position, isInterior, interiorCellHash))
                    continue;

                if (TrySnapToGround(candidate, isInterior, interiorCellHash, groundOffset, out position))
                    return true;
            }

            position = default;
            return false;
        }

        static float3 ComputeDirectionalPosition(in LocalTransform playerTransform, float distance, int direction)
        {
            float3 forward = math.rotate(playerTransform.Rotation, new float3(0f, 0f, 1f));
            forward.y = 0f;
            forward = math.normalizesafe(forward, new float3(0f, 0f, 1f));

            float3 right = math.rotate(playerTransform.Rotation, new float3(1f, 0f, 0f));
            right.y = 0f;
            right = math.normalizesafe(right, new float3(1f, 0f, 0f));

            return direction switch
            {
                0 => playerTransform.Position + forward * distance,
                1 => playerTransform.Position - forward * distance,
                2 => playerTransform.Position - right * distance,
                3 => playerTransform.Position + right * distance,
                _ => playerTransform.Position,
            };
        }

        static bool IsBlockedFromPlayer(float3 candidate, float3 playerPosition, bool isInterior, ulong interiorCellHash)
        {
            float3 start = candidate + new float3(0f, ActorSafetyLiftMw * WorldScale.MwUnitsToMeters, 0f);
            float3 end = playerPosition + new float3(0f, ActorSafetyTargetLiftMw * WorldScale.MwUnitsToMeters, 0f);
            var input = new RaycastInput { Start = start, End = end, Filter = CollisionFilter.Default };
            return RaycastWorld(input, isInterior, interiorCellHash, out _);
        }

        static bool TrySnapToGround(float3 candidate, bool isInterior, ulong interiorCellHash, float groundOffset, out float3 grounded)
        {
            float probeLift = ActorSafetyLiftMw * WorldScale.MwUnitsToMeters;
            float probeDistance = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            var input = new RaycastInput
            {
                Start = candidate + new float3(0f, probeLift, 0f),
                End = candidate - new float3(0f, probeDistance, 0f),
                Filter = CollisionFilter.Default,
            };

            if (!RaycastWorld(input, isInterior, interiorCellHash, out var hit))
            {
                grounded = default;
                return false;
            }

            grounded = hit.Position + new float3(0f, groundOffset, 0f);
            return true;
        }

        static bool RaycastWorld(RaycastInput input, bool isInterior, ulong interiorCellHash, out Unity.Physics.RaycastHit bestHit)
        {
            bool found = false;
            bestHit = default;

            if (isInterior)
            {
                if (!WorldSpawner.TryGetInteriorStaticCollider(interiorCellHash, out var staticCollider))
                {
                    return false;
                }

                TryRaycastBody(BuildBody(staticCollider, Entity.Null, float3.zero), input, ref found, ref bestHit);
                return found;
            }

            float cellM = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            var minCell = new int2((int)math.floor(math.min(input.Start.x, input.End.x) / cellM), (int)math.floor(math.min(input.Start.z, input.End.z) / cellM));
            var maxCell = new int2((int)math.floor(math.max(input.Start.x, input.End.x) / cellM), (int)math.floor(math.max(input.Start.z, input.End.z) / cellM));
            for (int x = minCell.x; x <= maxCell.x; x++)
            {
                for (int y = minCell.y; y <= maxCell.y; y++)
                {
                    var cell = new int2(x, y);
                    float3 origin = new float3(cell.x * cellM, 0f, cell.y * cellM);
                    if (WorldResources.TryGetTerrainCollider(cell, out var terrain))
                        TryRaycastBody(BuildBody(terrain, Entity.Null, origin), input, ref found, ref bestHit);
                    if (WorldResources.TryGetStaticCellCollider(cell, out var statics))
                        TryRaycastBody(BuildBody(statics, Entity.Null, origin), input, ref found, ref bestHit);
                }
            }

            return found;
        }

        static RigidBody BuildBody(BlobAssetReference<Collider> collider, Entity entity, float3 position)
        {
            return new RigidBody
            {
                Collider = collider,
                Entity = entity,
                WorldFromBody = new RigidTransform(quaternion.identity, position),
                Scale = 1f,
            };
        }

        static void TryRaycastBody(RigidBody body, RaycastInput input, ref bool found, ref Unity.Physics.RaycastHit bestHit)
        {
            if (!body.CastRay(input, out var hit))
                return;

            if (found && hit.Fraction >= bestHit.Fraction)
                return;

            found = true;
            bestHit = hit;
        }
    }
}
