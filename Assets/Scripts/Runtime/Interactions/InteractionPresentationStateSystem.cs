using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Interactions
{
    [UpdateInGroup(typeof(MorrowindInteractionPresentationSystemGroup))]
    public partial struct InteractionPresentationStateSystem : ISystem
    {
        const float NotificationLifetimeSeconds = 2.25f;

        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<PlayerInteractionFocus>();
            systemState.RequireForUpdate<InteractionActivationResult>();
            systemState.RequireForUpdate<InteractionPresentationState>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
            systemState.RequireForUpdate<RuntimeWorldCellBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            ref var presentation = ref SystemAPI.GetSingletonRW<InteractionPresentationState>().ValueRW;
            presentation.ShowCrosshair = 1;
            presentation.ShowFocus = 0;
            presentation.FocusText = default;

            var focus = SystemAPI.GetSingleton<PlayerInteractionFocus>();
            if (focus.HasTarget != 0 && systemState.EntityManager.Exists(focus.TargetEntity))
            {
                ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
                var worldCellReference = SystemAPI.GetSingleton<RuntimeWorldCellBlobReference>();
                if (!worldCellReference.Blob.IsCreated)
                    throw new System.InvalidOperationException("[VVardenfell][WorldCellBlob] interaction presentation requires runtime world cell blob.");
                ref RuntimeWorldCellBlob worldCells = ref worldCellReference.Blob.Value;
                string prompt = BuildFocusPrompt(ref systemState, ref contentBlob, ref worldCells, focus);
                if (!string.IsNullOrWhiteSpace(prompt))
                {
                    presentation.ShowFocus = 1;
                    presentation.FocusText = RuntimeFixedStringUtility.ToFixed128OrDefault(prompt);
                }
            }

            ref var result = ref SystemAPI.GetSingletonRW<InteractionActivationResult>().ValueRW;
            if (result.PendingNotification != 0 && result.Success != 0 && result.NotificationText.Length > 0)
            {
                presentation.NotificationText = result.NotificationText;
                presentation.NotificationSecondsRemaining = NotificationLifetimeSeconds;
                presentation.ShowNotification = 1;
                result.PendingNotification = 0;
            }
            else if (presentation.ShowNotification != 0)
            {
                presentation.NotificationSecondsRemaining -= SystemAPI.Time.DeltaTime;
                if (presentation.NotificationSecondsRemaining <= 0f)
                {
                    presentation.NotificationSecondsRemaining = 0f;
                    presentation.NotificationText = default;
                    presentation.ShowNotification = 0;
                }
            }
        }

        string BuildFocusPrompt(ref SystemState systemState, ref RuntimeContentBlob contentBlob, ref RuntimeWorldCellBlob worldCells, PlayerInteractionFocus focus)
        {
            return InteractionMetadataResolver.BuildFocusPrompt(ref contentBlob, ref worldCells, systemState.EntityManager, focus);
        }

    }
}
