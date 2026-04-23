using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.WorldState
{
    public static class RuntimeSpawnRequestBridge
    {
        public static bool TryRequestExteriorSpawn(
            ContentReference content,
            Vector3 position,
            Quaternion rotation,
            float scale,
            int exteriorCellX,
            int exteriorCellY,
            out uint sequence,
            out string error)
        {
            return TryRequestSpawn(
                content,
                position,
                rotation,
                scale,
                new int2(exteriorCellX, exteriorCellY),
                default,
                isInterior: false,
                out sequence,
                out error);
        }

        public static bool TryRequestExteriorSpawn(
            string contentId,
            Vector3 position,
            Quaternion rotation,
            float scale,
            int exteriorCellX,
            int exteriorCellY,
            out uint sequence,
            out string error)
        {
            sequence = 0u;
            if (!TryResolveContent(contentId, out var content, out error))
                return false;

            return TryRequestSpawn(
                content,
                position,
                rotation,
                scale,
                new int2(exteriorCellX, exteriorCellY),
                default,
                isInterior: false,
                out sequence,
                out error);
        }

        public static bool TryRequestInteriorSpawn(
            ContentReference content,
            Vector3 position,
            Quaternion rotation,
            float scale,
            string interiorCellId,
            out uint sequence,
            out string error)
        {
            return TryRequestSpawn(
                content,
                position,
                rotation,
                scale,
                default,
                string.IsNullOrEmpty(interiorCellId) ? default : new FixedString128Bytes(interiorCellId),
                isInterior: true,
                out sequence,
                out error);
        }

        public static bool TryRequestInteriorSpawn(
            string contentId,
            Vector3 position,
            Quaternion rotation,
            float scale,
            string interiorCellId,
            out uint sequence,
            out string error)
        {
            sequence = 0u;
            if (!TryResolveContent(contentId, out var content, out error))
                return false;

            return TryRequestSpawn(
                content,
                position,
                rotation,
                scale,
                default,
                string.IsNullOrEmpty(interiorCellId) ? default : new FixedString128Bytes(interiorCellId),
                isInterior: true,
                out sequence,
                out error);
        }

        public static bool TryGetLastResult(out RuntimeSpawnResult result, out string error)
        {
            result = default;
            if (!TryGetRuntimeSpawnEntity(out var entityManager, out Entity spawnEntity, out error))
                return false;

            result = entityManager.GetComponentData<RuntimeSpawnResult>(spawnEntity);
            error = null;
            return true;
        }

        static bool TryRequestSpawn(
            ContentReference content,
            Vector3 position,
            Quaternion rotation,
            float scale,
            int2 exteriorCell,
            FixedString128Bytes interiorCellId,
            bool isInterior,
            out uint sequence,
            out string error)
        {
            sequence = 0u;
            if (!content.IsValid)
            {
                error = "Spawn content reference is invalid.";
                return false;
            }

            if (!TryGetRuntimeSpawnEntity(out var entityManager, out Entity spawnEntity, out error))
                return false;

            var state = entityManager.GetComponentData<RuntimeSpawnState>(spawnEntity);
            sequence = state.NextRequestSequence + 1u;
            state.NextRequestSequence = sequence;
            entityManager.SetComponentData(spawnEntity, state);

            var requests = entityManager.GetBuffer<RuntimeSpawnRequest>(spawnEntity);
            requests.Add(new RuntimeSpawnRequest
            {
                Sequence = sequence,
                Content = content,
                Position = new float3(position.x, position.y, position.z),
                Rotation = new quaternion(rotation.x, rotation.y, rotation.z, rotation.w),
                Scale = Mathf.Max(0.0001f, scale),
                ExteriorCell = exteriorCell,
                InteriorCellId = interiorCellId,
                IsInterior = (byte)(isInterior ? 1 : 0),
                PersistencePolicy = (byte)RuntimeSpawnPersistencePolicy.CellOwnedSession,
            });

            error = null;
            return true;
        }

        static bool TryResolveContent(string contentId, out ContentReference content, out string error)
        {
            content = default;
            var contentDb = RuntimeContentDatabase.Active;
            if (contentDb == null)
            {
                error = "Runtime content database is not ready.";
                return false;
            }

            if (!contentDb.TryResolvePlaceable(contentId, out content))
            {
                error = $"Unknown placeable content id '{contentId}'.";
                return false;
            }

            error = null;
            return true;
        }

        static bool TryGetRuntimeSpawnEntity(out EntityManager entityManager, out Entity spawnEntity, out string error)
        {
            spawnEntity = Entity.Null;
            entityManager = default;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                error = "Default ECS world is not ready.";
                return false;
            }

            entityManager = world.EntityManager;
            using var query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<RuntimeSpawnState>(),
                ComponentType.ReadOnly<RuntimeSpawnResult>(),
                ComponentType.ReadWrite<RuntimeSpawnRequest>());
            if (query.IsEmptyIgnoreFilter)
            {
                error = "Runtime spawn state is not ready.";
                return false;
            }

            spawnEntity = query.GetSingletonEntity();
            error = null;
            return true;
        }
    }
}
