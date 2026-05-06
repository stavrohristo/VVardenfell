using Unity.Entities;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Interactions
{
    [UpdateInGroup(typeof(MorrowindPhysicsPostQueryMutationSystemGroup))]
    [UpdateAfter(typeof(NpcInteractionDeferredSystem))]
    [UpdateBefore(typeof(ActivatorInteractionDeferredSystem))]
    public partial struct BedInteractionDeferredSystem : ISystem
    {
        const string BedStandardScriptId = "bed_standard";
        const string CharGenBedScriptId = "chargenbed";

        EntityQuery _requestQuery;
        EntityQuery _focusQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _requestQuery = systemState.GetEntityQuery(ComponentType.ReadWrite<InteractionActivationRequest>());
            _focusQuery = systemState.GetEntityQuery(ComponentType.ReadWrite<PlayerInteractionFocus>());

            systemState.RequireForUpdate(_requestQuery);
            systemState.RequireForUpdate(_focusQuery);
            systemState.RequireForUpdate<InteractionActivationResult>();
            systemState.RequireForUpdate<RuntimeShellState>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            var requestRef = _requestQuery.GetSingletonRW<InteractionActivationRequest>();
            ref var request = ref requestRef.ValueRW;
            if (request.Pending == 0 || request.Kind != (byte)InteractableKind.Activator)
                return;

            Entity target = request.TargetEntity;
            if (!systemState.EntityManager.Exists(target) || !IsBedActivator(ref systemState, target))
                return;

            uint placedRefId = request.TargetPlacedRefId;
            uint sequence = request.Sequence;
            request.Pending = 0;
            request.TargetEntity = Entity.Null;

            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            RuntimeShellStateUtility.OpenRestMenu(ref shell, target, placedRefId, canSleep: true);
            RuntimeShellStateUtility.SyncGameplayGateAndCursor(ref shell);

            ClearFocus();

            ref var result = ref SystemAPI.GetSingletonRW<InteractionActivationResult>().ValueRW;
            result.Sequence = sequence;
            result.Kind = (byte)InteractableKind.Activator;
            result.Success = 1;
            result.PendingNotification = 0;
            result.NotificationText = default;
        }

        bool IsBedActivator(ref SystemState systemState, Entity target)
        {
            if (!systemState.EntityManager.HasComponent<ActivatorAuthoring>(target))
                return false;

            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;

            ActivatorDefHandle handle = systemState.EntityManager.GetComponentData<ActivatorAuthoring>(target).Definition;
            if (!handle.IsValid || handle.Index < 0 || handle.Index >= contentBlob.Activators.Length)
                return false;

            ref RuntimeBaseDefBlob activator = ref RuntimeContentBlobUtility.Get(ref contentBlob, handle);
            string scriptId = ContentId.NormalizeId(activator.ScriptId.ToString());
            return scriptId == BedStandardScriptId || scriptId == CharGenBedScriptId;
        }

        void ClearFocus()
        {
            var focusRef = _focusQuery.GetSingletonRW<PlayerInteractionFocus>();
            focusRef.ValueRW = new PlayerInteractionFocus
            {
                TargetEntity = Entity.Null,
            };
        }
    }
}
