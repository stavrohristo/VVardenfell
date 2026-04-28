using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Interactions
{
    [UpdateInGroup(typeof(MorrowindInteractionPresentationSystemGroup))]
    public partial class InteractionPresentationStateSystem : SystemBase
    {
        const float NotificationLifetimeSeconds = 2.25f;

        protected override void OnCreate()
        {
            RequireForUpdate<PlayerInteractionFocus>();
            RequireForUpdate<InteractionActivationResult>();
            RequireForUpdate<InteractionPresentationState>();
        }

        protected override void OnUpdate()
        {
            ref var presentation = ref SystemAPI.GetSingletonRW<InteractionPresentationState>().ValueRW;
            presentation.ShowCrosshair = 1;
            presentation.ShowFocus = 0;
            presentation.FocusText = default;

            var focus = SystemAPI.GetSingleton<PlayerInteractionFocus>();
            if (focus.HasTarget != 0 && EntityManager.Exists(focus.TargetEntity))
            {
                string prompt = BuildFocusPrompt(RuntimeContentDatabase.Active, focus);
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

        string BuildFocusPrompt(RuntimeContentDatabase contentDb, PlayerInteractionFocus focus)
        {
            return InteractionMetadataResolver.BuildFocusPrompt(contentDb, EntityManager, focus);
        }

    }
}
