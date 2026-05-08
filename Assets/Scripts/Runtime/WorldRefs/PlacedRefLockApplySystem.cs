using System;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldState;

namespace VVardenfell.Runtime.WorldRefs
{
    [UpdateInGroup(typeof(MorrowindGameplayMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptRefStateApplySystem))]
    public partial struct PlacedRefLockApplySystem : ISystem
    {
        public const byte LockOperation = 1;
        public const byte UnlockOperation = 2;
        public const int DefaultLockLevelSentinel = int.MinValue;

        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<PlacedRefLockRequest>();
            systemState.RequireForUpdate<LogicalRefLookup>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = systemState.EntityManager.GetBuffer<PlacedRefLockRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            var lookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            for (int i = 0; i < requests.Length; i++)
            {
                var request = requests[i];
                Entity target = MorrowindRuntimeTargetResolver.ResolveLiveTarget(systemState.EntityManager, request.TargetEntity, request.TargetPlacedRefId, lookup);
                if (target == Entity.Null)
                    throw new InvalidOperationException("Lock state request target is not live.");

                if (request.Operation == LockOperation)
                {
                    ApplyLock(ref systemState, target, request);
                    ScriptVisibleSaveStateUtility.UpsertLock(systemState.EntityManager, ResolvePlacedRefId(ref systemState, target, request.TargetPlacedRefId), systemState.EntityManager.GetComponentData<PlacedRefLockState>(target));
                    continue;
                }

                if (request.Operation == UnlockOperation)
                {
                    ApplyUnlock(ref systemState, target);
                    if (systemState.EntityManager.HasComponent<PlacedRefLockState>(target))
                        ScriptVisibleSaveStateUtility.UpsertLock(systemState.EntityManager, ResolvePlacedRefId(ref systemState, target, request.TargetPlacedRefId), systemState.EntityManager.GetComponentData<PlacedRefLockState>(target));
                    continue;
                }

                throw new InvalidOperationException("Unsupported lock state operation.");
            }

            requests.Clear();
        }

        void ApplyLock(ref SystemState systemState, Entity target, in PlacedRefLockRequest request)
        {
            var state = systemState.EntityManager.HasComponent<PlacedRefLockState>(target)
                ? systemState.EntityManager.GetComponentData<PlacedRefLockState>(target)
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

            if (systemState.EntityManager.HasComponent<PlacedRefLockState>(target))
                systemState.EntityManager.SetComponentData(target, state);
            else
                systemState.EntityManager.AddComponentData(target, state);
        }

        void ApplyUnlock(ref SystemState systemState, Entity target)
        {
            if (!systemState.EntityManager.HasComponent<PlacedRefLockState>(target))
                return;

            var state = systemState.EntityManager.GetComponentData<PlacedRefLockState>(target);
            if (state.Locked != 0)
                state.LockLevel = -math.abs(state.LockLevel);

            state.Locked = 0;
            systemState.EntityManager.SetComponentData(target, state);
        }

        static uint ResolvePlacedRefId(ref SystemState systemState, Entity target, uint requestPlacedRefId)
        {
            if (requestPlacedRefId != 0u)
                return requestPlacedRefId;
            if (target != Entity.Null
                && systemState.EntityManager.Exists(target)
                && systemState.EntityManager.HasComponent<PlacedRefIdentity>(target))
                return systemState.EntityManager.GetComponentData<PlacedRefIdentity>(target).Value;
            throw new InvalidOperationException("[VVardenfell][Save] Lock state mutation has no placed ref id.");
        }
    }
}
