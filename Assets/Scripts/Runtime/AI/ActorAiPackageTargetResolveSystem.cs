using Unity.Entities;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.AI
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateBefore(typeof(ActorAiPlannerSystem))]
    public partial class ActorAiPackageTargetResolveSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<ActorAiPackageRuntime>();
            RequireForUpdate<ActiveExplicitRefLookup>();
            RequireForUpdate<LogicalRefLookup>();
        }

        protected override void OnUpdate()
        {
            var contentDb = RuntimeContentDatabase.Active;
            if (contentDb == null)
                return;

            var activeExplicitRefs = SystemAPI.GetSingleton<ActiveExplicitRefLookup>();
            var logicalRefs = SystemAPI.GetSingleton<LogicalRefLookup>();
            Entity player = MorrowindRuntimeTargetResolver.ResolvePlayerEntity(EntityManager);

            foreach (var packages in SystemAPI.Query<DynamicBuffer<ActorAiPackageRuntime>>())
            {
                var writablePackages = packages;
                for (int i = 0; i < writablePackages.Length; i++)
                {
                    var package = writablePackages[i];
                    if (!RequiresTarget(package.Type) || package.FollowTargetEntity != Entity.Null || package.TargetId.IsEmpty)
                        continue;

                    if (!TryResolveTarget(contentDb, activeExplicitRefs, logicalRefs, player, package.TargetId.ToString(), out Entity target, out uint placedRefId))
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

        bool TryResolveTarget(
            RuntimeContentDatabase contentDb,
            in ActiveExplicitRefLookup activeExplicitRefs,
            in LogicalRefLookup logicalRefs,
            Entity player,
            string targetId,
            out Entity target,
            out uint placedRefId)
        {
            target = Entity.Null;
            placedRefId = 0u;
            if (string.IsNullOrWhiteSpace(targetId))
                return false;

            if (MorrowindCommandTextUtility.IsPlayerTarget(targetId))
            {
                if (player == Entity.Null || !EntityManager.Exists(player))
                    return false;

                target = player;
                placedRefId = EntityManager.HasComponent<PlacedRefIdentity>(player)
                    ? EntityManager.GetComponentData<PlacedRefIdentity>(player).Value
                    : 0u;
                return true;
            }

            if (contentDb.TryGetExplicitRefTarget(targetId, out placedRefId) && placedRefId != 0u)
            {
                if (!logicalRefs.Map.IsCreated || !logicalRefs.Map.TryGetValue(placedRefId, out target))
                    return false;

                return target != Entity.Null && EntityManager.Exists(target);
            }

            if (!contentDb.TryResolvePlaceable(targetId, out ContentReference content) || !contentDb.IsValid(content))
                return false;

            if (!activeExplicitRefs.ByContentKey.IsCreated)
                return false;

            int key = ActiveExplicitRefLookupUtility.Pack(content);
            if (!activeExplicitRefs.ByContentKey.TryGetValue(key, out var activeTarget) || activeTarget.Ambiguous != 0)
                return false;

            if (activeTarget.Entity == Entity.Null || activeTarget.PlacedRefId == 0u || !EntityManager.Exists(activeTarget.Entity))
                return false;

            target = activeTarget.Entity;
            placedRefId = activeTarget.PlacedRefId;
            return true;
        }
    }
}
