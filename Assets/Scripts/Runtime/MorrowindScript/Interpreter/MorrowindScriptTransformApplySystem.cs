using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindWorldMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptRefStateApplySystem))]
    public partial class MorrowindScriptTransformApplySystem : SystemBase
    {
        EntityQuery _runtimeQuery;

        protected override void OnCreate()
        {
            _runtimeQuery = GetEntityQuery(
                ComponentType.ReadOnly<MorrowindScriptRuntimeState>(),
                ComponentType.ReadWrite<MorrowindScriptTransformRequest>());

            RequireForUpdate(_runtimeQuery);
            RequireForUpdate<LogicalRefLookup>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = _runtimeQuery.GetSingletonEntity();
            var requests = EntityManager.GetBuffer<MorrowindScriptTransformRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            var logicalRefLookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            for (int i = 0; i < requests.Length; i++)
            {
                var request = requests[i];
                Entity target = ResolveLiveTarget(request, logicalRefLookup);
                if (target == Entity.Null || !EntityManager.Exists(target))
                    continue;

                if (request.Operation == 1)
                {
                    LogicalRefRotationUtility.SetAngle(EntityManager, target, request.Axis, request.Radians);
                    continue;
                }

                quaternion delta = quaternion.AxisAngle(
                    LogicalRefRotationUtility.ResolveAxis(request.Axis),
                    request.Radians);
                LogicalRefRotationUtility.ApplyDelta(EntityManager, target, delta);
            }

            requests.Clear();
        }

        Entity ResolveLiveTarget(in MorrowindScriptTransformRequest request, in LogicalRefLookup lookup)
        {
            if (request.TargetEntity != Entity.Null && EntityManager.Exists(request.TargetEntity))
                return request.TargetEntity;

            if (lookup.Map.IsCreated && lookup.Map.TryGetValue(request.TargetPlacedRefId, out Entity target))
                return target;

            return Entity.Null;
        }
    }
}
