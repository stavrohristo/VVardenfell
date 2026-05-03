using System;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.WorldRefs
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptRefStateApplySystem))]
    public partial class PlacedRefLockApplySystem : SystemBase
    {
        const byte LockOperation = 1;
        const byte UnlockOperation = 2;
        const int DefaultLockLevelSentinel = int.MinValue;

        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindScriptRuntimeState>();
            RequireForUpdate<PlacedRefLockRequest>();
            RequireForUpdate<LogicalRefLookup>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = EntityManager.GetBuffer<PlacedRefLockRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            var lookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            for (int i = 0; i < requests.Length; i++)
            {
                var request = requests[i];
                Entity target = MorrowindRuntimeTargetResolver.ResolveLiveTarget(EntityManager, request.TargetEntity, request.TargetPlacedRefId, lookup);
                if (target == Entity.Null)
                    throw new InvalidOperationException($"Lock state request target ref={request.TargetPlacedRefId} is not live.");

                if (request.Operation == LockOperation)
                {
                    ApplyLock(target, request);
                    continue;
                }

                if (request.Operation == UnlockOperation)
                {
                    ApplyUnlock(target);
                    continue;
                }

                throw new InvalidOperationException($"Unsupported lock state operation {request.Operation} for ref={request.TargetPlacedRefId}.");
            }

            requests.Clear();
        }

        void ApplyLock(Entity target, in PlacedRefLockRequest request)
        {
            var state = EntityManager.HasComponent<PlacedRefLockState>(target)
                ? EntityManager.GetComponentData<PlacedRefLockState>(target)
                : default;

            int level = request.LockLevel;
            if (level == DefaultLockLevelSentinel)
            {
                level = state.LockLevel;
                if (level == 0)
                    level = 100;
            }

            state.LockLevel = level;
            state.Locked = 1;

            if (EntityManager.HasComponent<PlacedRefLockState>(target))
                EntityManager.SetComponentData(target, state);
            else
                EntityManager.AddComponentData(target, state);
        }

        void ApplyUnlock(Entity target)
        {
            if (!EntityManager.HasComponent<PlacedRefLockState>(target))
                return;

            var state = EntityManager.GetComponentData<PlacedRefLockState>(target);
            if (state.Locked != 0)
                state.LockLevel = -math.abs(state.LockLevel);

            state.Locked = 0;
            EntityManager.SetComponentData(target, state);
        }
    }
}
