using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.AI
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateBefore(typeof(ActorAiPlannerSystem))]
    public partial struct ActorAiPackageTargetResolveSystem : ISystem
    {
        EntityQuery _playerQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _playerQuery = systemState.GetEntityQuery(ComponentType.ReadOnly<PlayerTag>());
            systemState.RequireForUpdate<ActorAiPackageRuntime>();
            systemState.RequireForUpdate<ActiveExplicitRefLookup>();
            systemState.RequireForUpdate<LogicalRefLookup>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new System.InvalidOperationException("[VVardenfell][ContentBlob] AI package target resolve requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;

            var activeExplicitRefs = SystemAPI.GetSingleton<ActiveExplicitRefLookup>();
            var logicalRefs = SystemAPI.GetSingleton<LogicalRefLookup>();
            Entity player = _playerQuery.CalculateEntityCount() == 1 ? _playerQuery.GetSingletonEntity() : Entity.Null;

            foreach (var packages in SystemAPI.Query<DynamicBuffer<ActorAiPackageRuntime>>())
            {
                var writablePackages = packages;
                for (int i = 0; i < writablePackages.Length; i++)
                {
                    var package = writablePackages[i];
                    if (!RequiresTarget(package.Type) || package.FollowTargetEntity != Entity.Null || package.TargetId.IsEmpty)
                        continue;

                    if (!TryResolveTarget(ref systemState, ref content, activeExplicitRefs, logicalRefs, player, package.TargetId, out Entity target, out uint placedRefId))
                        continue;

                    package.FollowTargetEntity = target;
                    package.FollowTargetPlacedRefId = placedRefId;
                    writablePackages[i] = package;
                }
            }
        }

        static bool RequiresTarget(byte packageType)
        {
            return packageType == (byte)ActorAiRuntimePackageType.Follow
                   || packageType == (byte)ActorAiRuntimePackageType.Escort
                   || packageType == (byte)ActorAiRuntimePackageType.Activate;
        }

        bool TryResolveTarget(ref SystemState systemState, 
            ref RuntimeContentBlob contentBlob,
            in ActiveExplicitRefLookup activeExplicitRefs,
            in LogicalRefLookup logicalRefs,
            Entity player,
            FixedString128Bytes targetId,
            out Entity target,
            out uint placedRefId)
        {
            target = Entity.Null;
            placedRefId = 0u;
            if (targetId.Length == 0)
                return false;

            if (IsPlayerTarget(targetId))
            {
                if (player == Entity.Null || !systemState.EntityManager.Exists(player))
                    return false;

                target = player;
                placedRefId = systemState.EntityManager.HasComponent<PlacedRefIdentity>(player)
                    ? systemState.EntityManager.GetComponentData<PlacedRefIdentity>(player).Value
                    : 0u;
                return true;
            }

            ulong targetHash = RuntimeContentStableHash.HashId(targetId);
            if (targetHash == 0UL)
                return false;

            if (RuntimeContentBlobUtility.TryGetExplicitRefTargetByIdHash(ref contentBlob, targetHash, out placedRefId) && placedRefId != 0u)
            {
                if (!logicalRefs.Map.IsCreated || !logicalRefs.Map.TryGetValue(placedRefId, out target))
                    return false;

                return target != Entity.Null && systemState.EntityManager.Exists(target);
            }

            if (!RuntimeContentBlobUtility.TryResolvePlaceableByIdHash(ref contentBlob, targetHash, out ContentReference content) || !RuntimeContentBlobUtility.IsValid(ref contentBlob, content))
                return false;

            if (!activeExplicitRefs.ByContentKey.IsCreated)
                return false;

            int key = ActiveExplicitRefLookupUtility.Pack(content);
            if (!activeExplicitRefs.ByContentKey.TryGetValue(key, out var activeTarget) || activeTarget.Ambiguous != 0)
                return false;

            if (activeTarget.Entity == Entity.Null || activeTarget.PlacedRefId == 0u || !systemState.EntityManager.Exists(activeTarget.Entity))
                return false;

            target = activeTarget.Entity;
            placedRefId = activeTarget.PlacedRefId;
            return true;
        }

        static bool IsPlayerTarget(FixedString128Bytes targetId)
        {
            int start = 0;
            int end = targetId.Length - 1;
            while (start <= end && IsTrimByte(targetId[start]))
                start++;
            while (end >= start && IsTrimByte(targetId[end]))
                end--;
            while (start <= end && targetId[start] == (byte)'"')
                start++;
            while (end >= start && targetId[end] == (byte)'"')
                end--;

            if (end - start + 1 != 6)
                return false;

            return ToLowerAscii(targetId[start]) == (byte)'p'
                   && ToLowerAscii(targetId[start + 1]) == (byte)'l'
                   && ToLowerAscii(targetId[start + 2]) == (byte)'a'
                   && ToLowerAscii(targetId[start + 3]) == (byte)'y'
                   && ToLowerAscii(targetId[start + 4]) == (byte)'e'
                   && ToLowerAscii(targetId[start + 5]) == (byte)'r';
        }

        static bool IsTrimByte(byte value)
            => value == (byte)' '
               || value == (byte)'\t'
               || value == (byte)'\n'
               || value == (byte)'\r'
               || value == (byte)'\f'
               || value == (byte)'\v'
               || value == (byte)',';

        static byte ToLowerAscii(byte value)
            => value >= (byte)'A' && value <= (byte)'Z'
                ? (byte)(value + 32)
                : value;
    }
}
