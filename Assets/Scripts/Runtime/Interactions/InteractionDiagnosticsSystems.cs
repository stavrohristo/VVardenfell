using System.Text;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Interactions
{
    [UpdateInGroup(typeof(MorrowindInteractionPresentationSystemGroup))]
    [UpdateAfter(typeof(InteractionPresentationStateSystem))]
    public partial class InteractionDiagnosticsSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<InteractionDiagnosticsState>();
            RequireForUpdate<InteractionDiagnosticsSnapshot>();
            RequireForUpdate<PlayerInteractionRaycastHit>();
            RequireForUpdate<PlayerInteractionFocus>();
            RequireForUpdate<InteractionActivationRequest>();
            RequireForUpdate<InteractionActivationResult>();
            RequireForUpdate<InteractionPresentationState>();
        }

        protected override void OnUpdate()
        {
            var raycast = SystemAPI.GetSingleton<PlayerInteractionRaycastHit>();
            var focus = SystemAPI.GetSingleton<PlayerInteractionFocus>();
            var request = SystemAPI.GetSingleton<InteractionActivationRequest>();
            var result = SystemAPI.GetSingleton<InteractionActivationResult>();
            var presentation = SystemAPI.GetSingleton<InteractionPresentationState>();
            uint fixedTick = SystemAPI.HasSingleton<MorrowindPhysicsFrameState>()
                ? SystemAPI.GetSingleton<MorrowindPhysicsFrameState>().SnapshotTick
                : 0u;

            ref var state = ref SystemAPI.GetSingletonRW<InteractionDiagnosticsState>().ValueRW;
            state.SnapshotSequence++;
            state.LastFixedTick = fixedTick;
            state.LastRaycastSequence = raycast.Sequence;
            state.LastActivationRequestSequence = request.Sequence;
            state.LastActivationResultSequence = result.Sequence;
            state.LastHadRaycastHit = raycast.HasHit;
            state.LastHadFocus = focus.HasTarget;
            state.LastRequestPending = request.Pending;
            state.LastResultSuccess = result.Success;

            ref var snapshot = ref SystemAPI.GetSingletonRW<InteractionDiagnosticsSnapshot>().ValueRW;
            snapshot = new InteractionDiagnosticsSnapshot
            {
                SnapshotSequence = state.SnapshotSequence,
                FixedTick = fixedTick,
                RaycastSequence = raycast.Sequence,
                HasPrimaryHit = raycast.HasHit,
                PrimaryHitEntity = raycast.HitEntity,
                PrimaryHitDistance = raycast.HitDistance,
                HasProxyHit = raycast.HasProxyHit,
                ProxyHitEntity = raycast.ProxyHitEntity,
                ProxyHitDistance = raycast.ProxyHitDistance,
                HasSolidHit = raycast.HasSolidHit,
                SolidHitEntity = raycast.SolidHitEntity,
                SolidHitDistance = raycast.SolidHitDistance,

                HasFocus = focus.HasTarget,
                FocusEntity = focus.TargetEntity,
                FocusPlacedRefId = focus.PlacedRefId,
                FocusKind = focus.InteractKind,
                FocusDistance = focus.HitDistance,
                FocusLabel = ResolveFocusLabel(focus),

                RequestPending = request.Pending,
                RequestSequence = request.Sequence,
                RequestTargetEntity = request.TargetEntity,
                RequestPlacedRefId = request.TargetPlacedRefId,
                RequestKind = request.Kind,

                ResultSequence = result.Sequence,
                ResultKind = result.Kind,
                ResultSuccess = result.Success,
                ResultPendingNotification = result.PendingNotification,
                ResultText = result.NotificationText,

                PresentationFocusText = presentation.FocusText,
                PresentationNotificationText = presentation.NotificationText,
                PresentationShowFocus = presentation.ShowFocus,
                PresentationShowNotification = presentation.ShowNotification,
            };
        }

        FixedString128Bytes ResolveFocusLabel(in PlayerInteractionFocus focus)
        {
            if (focus.HasTarget == 0 || !EntityManager.Exists(focus.TargetEntity))
                return default;

            string label = InteractionMetadataResolver.ResolveDisplayName(
                RuntimeContentDatabase.Active,
                EntityManager,
                focus.TargetEntity,
                (InteractableKind)focus.InteractKind);

            return ToFixedString(label);
        }

        static FixedString128Bytes ToFixedString(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return default;
            if (value.Length > 127)
                value = value.Substring(0, 127);
            return new FixedString128Bytes(value);
        }
    }

    public static class InteractionDiagnosticsBridge
    {
        public static bool TryGetSnapshot(out InteractionDiagnosticsSnapshot snapshot, out string error)
        {
            snapshot = default;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                error = "Default ECS world is not ready.";
                return false;
            }

            var entityManager = world.EntityManager;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<InteractionDiagnosticsSnapshot>());
            if (query.IsEmptyIgnoreFilter)
            {
                error = "Interaction diagnostics snapshot is not ready.";
                return false;
            }

            snapshot = query.GetSingleton<InteractionDiagnosticsSnapshot>();
            error = null;
            return true;
        }

        public static string DescribeLatest()
        {
            if (!TryGetSnapshot(out var snapshot, out string error))
                return $"Interaction diagnostics unavailable: {error}";

            var builder = new StringBuilder(512);
            builder.Append("[VVardenfell][InteractionDiagnostics]");
            builder.Append(" snapshot=");
            builder.Append(snapshot.SnapshotSequence);
            builder.Append(" raycast=");
            builder.Append(snapshot.RaycastSequence);
            builder.Append(" tick=");
            builder.Append(snapshot.FixedTick);
            builder.Append(" primary=");
            AppendHit(builder, snapshot.HasPrimaryHit, snapshot.PrimaryHitEntity, snapshot.PrimaryHitDistance);
            builder.Append(" proxy=");
            AppendHit(builder, snapshot.HasProxyHit, snapshot.ProxyHitEntity, snapshot.ProxyHitDistance);
            builder.Append(" solid=");
            AppendHit(builder, snapshot.HasSolidHit, snapshot.SolidHitEntity, snapshot.SolidHitDistance);
            builder.AppendLine();

            builder.Append("  focus=");
            if (snapshot.HasFocus != 0)
            {
                builder.Append((InteractableKind)snapshot.FocusKind);
                builder.Append(" placedRef=0x");
                builder.Append(snapshot.FocusPlacedRefId.ToString("X8"));
                builder.Append(" entity=");
                builder.Append(snapshot.FocusEntity);
                builder.Append(" distance=");
                builder.Append(snapshot.FocusDistance.ToString("F2"));
                if (snapshot.FocusLabel.Length > 0)
                {
                    builder.Append(" label='");
                    builder.Append(snapshot.FocusLabel.ToString());
                    builder.Append("'");
                }
            }
            else
            {
                builder.Append("none");
            }
            builder.AppendLine();

            builder.Append("  request=");
            builder.Append(snapshot.RequestPending != 0 ? "pending" : "idle");
            builder.Append(" seq=");
            builder.Append(snapshot.RequestSequence);
            builder.Append(" kind=");
            builder.Append((InteractableKind)snapshot.RequestKind);
            builder.Append(" placedRef=0x");
            builder.Append(snapshot.RequestPlacedRefId.ToString("X8"));
            builder.Append(" entity=");
            builder.Append(snapshot.RequestTargetEntity);
            builder.AppendLine();

            builder.Append("  result seq=");
            builder.Append(snapshot.ResultSequence);
            builder.Append(" kind=");
            builder.Append((InteractableKind)snapshot.ResultKind);
            builder.Append(" success=");
            builder.Append(snapshot.ResultSuccess != 0 ? "yes" : "no");
            if (snapshot.ResultText.Length > 0)
            {
                builder.Append(" text='");
                builder.Append(snapshot.ResultText.ToString());
                builder.Append("'");
            }
            builder.AppendLine();

            builder.Append("  presentation focus=");
            builder.Append(snapshot.PresentationShowFocus != 0 ? "show" : "hide");
            if (snapshot.PresentationFocusText.Length > 0)
            {
                builder.Append(" '");
                builder.Append(snapshot.PresentationFocusText.ToString());
                builder.Append("'");
            }
            builder.Append(" notification=");
            builder.Append(snapshot.PresentationShowNotification != 0 ? "show" : "hide");
            if (snapshot.PresentationNotificationText.Length > 0)
            {
                builder.Append(" '");
                builder.Append(snapshot.PresentationNotificationText.ToString());
                builder.Append("'");
            }

            return builder.ToString();
        }

        static void AppendHit(StringBuilder builder, byte hasHit, Entity entity, float distance)
        {
            if (hasHit == 0)
            {
                builder.Append("none");
                return;
            }

            builder.Append(entity);
            builder.Append("@");
            builder.Append(distance.ToString("F2"));
        }
    }
}
