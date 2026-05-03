#if !VVARDENFELL_OLD_ACTOR_ANIMATION
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateBefore(typeof(ActorAnimationControllerSystem))]
    public partial class ActorWeaponAnimationSystem : SystemBase
    {
        const int CombatOverlayPriority = 40;

        protected override void OnCreate()
        {
            RequireForUpdate<ActorAnimationBlobCatalog>();
            RequireForUpdate<ActorWeaponAnimationState>();
        }

        protected override void OnUpdate()
        {
            var catalogRef = SystemAPI.GetSingleton<ActorAnimationBlobCatalog>().Blob;
            if (!catalogRef.IsCreated)
                return;

            ref var catalog = ref catalogRef.Value;
            foreach (var (presentation, movementState, weaponState, overlays) in
                     SystemAPI.Query<RefRO<ActorPresentation>, RefRO<MorrowindMovementState>, RefRW<ActorWeaponAnimationState>, DynamicBuffer<ActorAnimationOverlayState>>())
            {
                UpdateWeaponAnimation(
                    ref catalog,
                    presentation.ValueRO,
                    movementState.ValueRO,
                    ref weaponState.ValueRW,
                    overlays);
            }
        }

        internal static void UpdateWeaponAnimation(
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            in MorrowindMovementState movementState,
            ref ActorWeaponAnimationState state,
            DynamicBuffer<ActorAnimationOverlayState> overlays)
        {
            bool supportedMelee = ActorWeaponAnimationUtility.IsSupportedMelee(state.WeaponType);
            if (state.ReadyWeaponTogglePressed != 0 && !IsAttacking(state.Phase))
                ToggleDrawState(ref catalog, presentation, ref state, overlays);

            switch (state.Phase)
            {
                case ActorWeaponAnimationPhase.Equipping:
                    if (state.Drawn != 0 && TryGetUpperBodyOverlay(overlays, out _, out var drawOverlay) && PlaybackReachedStop(drawOverlay.Playback))
                    {
                        state.Phase = ActorWeaponAnimationPhase.Equipped;
                    }
                    else if (!IsUpperBodyOverlayActive(overlays))
                    {
                        if (state.Drawn != 0)
                        {
                            state.Phase = ActorWeaponAnimationPhase.Equipped;
                            EnsureReadyHold(ref catalog, presentation, ref state, overlays);
                        }
                        else
                        {
                            state.Phase = ActorWeaponAnimationPhase.Hidden;
                            ClearUpperBodyOverlay(overlays);
                        }
                    }
                    break;
                case ActorWeaponAnimationPhase.Equipped:
                    if (state.Drawn != 0)
                    {
                        EnsureReadyHold(ref catalog, presentation, ref state, overlays);
                        if (supportedMelee && state.AttackPressed != 0)
                            StartWindUp(ref catalog, presentation, movementState, ref state, overlays);
                    }
                    break;
                case ActorWeaponAnimationPhase.AttackWindUp:
                    UpdateWindUp(ref catalog, presentation, ref state, overlays);
                    break;
                case ActorWeaponAnimationPhase.AttackRelease:
                    if (!IsUpperBodyOverlayActive(overlays))
                        StartFollow(ref catalog, presentation, ref state, overlays);
                    break;
                case ActorWeaponAnimationPhase.AttackFollow:
                    if (!IsUpperBodyOverlayActive(overlays))
                        FinishAttack(ref catalog, presentation, ref state, overlays);
                    break;
                case ActorWeaponAnimationPhase.Hidden:
                default:
                    if (state.Drawn != 0)
                        state.Phase = ActorWeaponAnimationPhase.Equipped;
                    break;
            }

            state.ReadyWeaponTogglePressed = 0;
            state.AttackPressed = 0;
            state.AttackReleased = 0;
        }

        static bool IsAttacking(ActorWeaponAnimationPhase phase)
            => phase == ActorWeaponAnimationPhase.AttackWindUp
               || phase == ActorWeaponAnimationPhase.AttackRelease
               || phase == ActorWeaponAnimationPhase.AttackFollow;

        static void ToggleDrawState(
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            ref ActorWeaponAnimationState state,
            DynamicBuffer<ActorAnimationOverlayState> overlays)
        {
            bool draw = state.Drawn == 0;
            state.Drawn = draw ? (byte)1 : (byte)0;
            state.ReleaseQueued = 0;
            state.AttackStrength = 0f;

            FixedString64Bytes start = draw ? Fixed64("equip start") : Fixed64("unequip start");
            FixedString64Bytes stop = draw ? Fixed64("equip stop") : Fixed64("unequip stop");
            if (TryResolveWeaponWindow(ref catalog, presentation, state.WeaponType, start, stop, out var group, out float startTime, out float stopTime))
            {
                StartUpperBodyOverlay(overlays, group, startTime, stopTime, holdAtStop: draw);
                state.Phase = ActorWeaponAnimationPhase.Equipping;
                return;
            }

            state.Phase = draw ? ActorWeaponAnimationPhase.Equipped : ActorWeaponAnimationPhase.Hidden;
            if (draw)
                EnsureReadyHold(ref catalog, presentation, ref state, overlays);
            else
                ClearUpperBodyOverlay(overlays);
        }

        static void StartWindUp(
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            in MorrowindMovementState movementState,
            ref ActorWeaponAnimationState state,
            DynamicBuffer<ActorAnimationOverlayState> overlays)
        {
            state.AttackType = ResolveAttackType(movementState.LocalMove);
            if (!TryResolveWeaponWindow(
                    ref catalog,
                    presentation,
                    state.WeaponType,
                    AttackMarker(state.AttackType, AttackMarkerKind.Start),
                    AttackMarker(state.AttackType, AttackMarkerKind.MaxAttack),
                    out var group,
                    out float startTime,
                    out float stopTime))
            {
                return;
            }

            state.AttackStrength = 0f;
            state.ReleaseQueued = state.AttackReleased;
            state.Phase = ActorWeaponAnimationPhase.AttackWindUp;
            StartUpperBodyOverlay(overlays, group, startTime, stopTime, holdAtStop: true);
        }

        static void UpdateWindUp(
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            ref ActorWeaponAnimationState state,
            DynamicBuffer<ActorAnimationOverlayState> overlays)
        {
            if (state.AttackReleased != 0)
                state.ReleaseQueued = 1;

            if (!TryGetUpperBodyOverlay(overlays, out var overlayIndex, out var overlay)
                || !ActorAnimationPlaybackUtility.IsActive(overlay.Playback))
            {
                FinishAttack(ref catalog, presentation, ref state, overlays);
                return;
            }

            float span = overlay.Playback.StopTime - overlay.Playback.StartTime;
            state.AttackStrength = span > 0.0001f
                ? math.saturate((overlay.Playback.Time - overlay.Playback.StartTime) / span)
                : 1f;

            if (state.ReleaseQueued == 0 || overlay.Playback.Time < overlay.Playback.StopTime)
                return;

            if (TryResolveWeaponWindow(
                    ref catalog,
                    presentation,
                    state.WeaponType,
                    AttackMarker(state.AttackType, AttackMarkerKind.MaxAttack),
                    AttackMarker(state.AttackType, AttackMarkerKind.Hit),
                    out var group,
                    out float startTime,
                    out float stopTime))
            {
                state.Phase = ActorWeaponAnimationPhase.AttackRelease;
                overlay = overlays[overlayIndex];
                ActorAnimationPlaybackUtility.StartWindow(ref overlay.Playback, group, startTime, stopTime, holdAtStop: false);
                overlay.Weight = 1f;
                overlay.Priority = CombatOverlayPriority;
                overlay.Mask = ActorAnimationBlendMask.UpperBody;
                overlays[overlayIndex] = overlay;
                return;
            }

            StartFollow(ref catalog, presentation, ref state, overlays);
        }

        static void StartFollow(
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            ref ActorWeaponAnimationState state,
            DynamicBuffer<ActorAnimationOverlayState> overlays)
        {
            ActorAttackFollowSize followSize = ResolveFollowSize(state.AttackStrength);
            if (TryResolveWeaponWindow(
                    ref catalog,
                    presentation,
                    state.WeaponType,
                    FollowMarker(state.AttackType, followSize, start: true),
                    FollowMarker(state.AttackType, followSize, start: false),
                    out var group,
                    out float startTime,
                    out float stopTime))
            {
                state.Phase = ActorWeaponAnimationPhase.AttackFollow;
                StartUpperBodyOverlay(overlays, group, startTime, stopTime, holdAtStop: false);
                return;
            }

            FinishAttack(ref catalog, presentation, ref state, overlays);
        }

        static void FinishAttack(
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            ref ActorWeaponAnimationState state,
            DynamicBuffer<ActorAnimationOverlayState> overlays)
        {
            state.ReleaseQueued = 0;
            state.AttackStrength = 0f;
            state.Phase = state.Drawn != 0
                ? ActorWeaponAnimationPhase.Equipped
                : ActorWeaponAnimationPhase.Hidden;
            if (state.Drawn != 0)
                EnsureReadyHold(ref catalog, presentation, ref state, overlays);
            else
                ClearUpperBodyOverlay(overlays);
        }

        static void EnsureReadyHold(
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            ref ActorWeaponAnimationState state,
            DynamicBuffer<ActorAnimationOverlayState> overlays)
        {
            if (state.Drawn == 0 || IsAttacking(state.Phase))
                return;

            if (TryGetUpperBodyOverlay(overlays, out _, out var overlay)
                && ActorAnimationPlaybackUtility.IsActive(overlay.Playback)
                && overlay.Playback.HoldAtStop != 0
                && PlaybackReachedStop(overlay.Playback))
            {
                return;
            }

            if (!TryResolveWeaponWindow(
                    ref catalog,
                    presentation,
                    state.WeaponType,
                    Fixed64("equip start"),
                    Fixed64("equip stop"),
                    out var group,
                    out _,
                    out float stopTime))
            {
                return;
            }

            StartUpperBodyOverlay(overlays, group, stopTime, stopTime, holdAtStop: true);
        }

        static ActorWeaponAttackType ResolveAttackType(float2 localMove)
        {
            float absX = math.abs(localMove.x);
            float absY = math.abs(localMove.y);
            if (absY > 0.0001f && absY >= absX)
                return ActorWeaponAttackType.Thrust;
            if (absX > 0.0001f)
                return ActorWeaponAttackType.Slash;
            return ActorWeaponAttackType.Chop;
        }

        static bool TryResolveWeaponWindow(
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            int weaponType,
            FixedString64Bytes startValue,
            FixedString64Bytes stopValue,
            out ActorAnimationGroupBlob group,
            out float start,
            out float stop)
        {
            group = default;
            start = 0f;
            stop = 0f;
            if (!ActorWeaponAnimationUtility.TryResolveGroupHashes(weaponType, out ulong primaryHash, out ulong fallbackHash))
                return false;

            if (TryResolveGroupWindow(ref catalog, presentation, primaryHash, startValue, stopValue, out group, out start, out stop))
                return true;

            return fallbackHash != 0UL
                   && TryResolveGroupWindow(ref catalog, presentation, fallbackHash, startValue, stopValue, out group, out start, out stop);
        }

        static bool TryResolveGroupWindow(
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            ulong groupHash,
            FixedString64Bytes startValue,
            FixedString64Bytes stopValue,
            out ActorAnimationGroupBlob group,
            out float start,
            out float stop)
        {
            if (!ActorAnimationGroupLookupUtility.TryResolveGroup(ref catalog, presentation, groupHash, out group))
            {
                start = 0f;
                stop = 0f;
                return false;
            }

            return ActorAnimationMarkerWindowUtility.TryResolveWindow(ref catalog, group, startValue, stopValue, out start, out stop);
        }

        static void StartUpperBodyOverlay(
            DynamicBuffer<ActorAnimationOverlayState> overlays,
            ActorAnimationGroupBlob group,
            float startTime,
            float stopTime,
            bool holdAtStop)
        {
            int index = FindUpperBodyOverlay(overlays);
            var overlay = index >= 0 ? overlays[index] : default;
            ActorAnimationPlaybackUtility.StartWindow(ref overlay.Playback, group, startTime, stopTime, holdAtStop);
            overlay.Weight = 1f;
            overlay.Priority = CombatOverlayPriority;
            overlay.Mask = ActorAnimationBlendMask.UpperBody;
            if (index >= 0)
                overlays[index] = overlay;
            else
                overlays.Add(overlay);
        }

        static void ClearUpperBodyOverlay(DynamicBuffer<ActorAnimationOverlayState> overlays)
        {
            int index = FindUpperBodyOverlay(overlays);
            if (index < 0)
                return;

            var overlay = overlays[index];
            ActorAnimationPlaybackUtility.Clear(ref overlay.Playback);
            overlay.Weight = 0f;
            overlays[index] = overlay;
        }

        static bool IsUpperBodyOverlayActive(DynamicBuffer<ActorAnimationOverlayState> overlays)
            => TryGetUpperBodyOverlay(overlays, out _, out var overlay)
               && ActorAnimationPlaybackUtility.IsActive(overlay.Playback);

        static bool PlaybackReachedStop(in ActorAnimationPlaybackState playback)
            => ActorAnimationPlaybackUtility.IsActive(playback)
               && playback.StopTime <= playback.Time + 0.0001f;

        static bool TryGetUpperBodyOverlay(
            DynamicBuffer<ActorAnimationOverlayState> overlays,
            out int index,
            out ActorAnimationOverlayState overlay)
        {
            index = FindUpperBodyOverlay(overlays);
            if (index < 0)
            {
                overlay = default;
                return false;
            }

            overlay = overlays[index];
            return true;
        }

        static int FindUpperBodyOverlay(DynamicBuffer<ActorAnimationOverlayState> overlays)
        {
            for (int i = 0; i < overlays.Length; i++)
                if (overlays[i].Mask == ActorAnimationBlendMask.UpperBody)
                    return i;
            return -1;
        }

        enum AttackMarkerKind : byte
        {
            Start,
            MaxAttack,
            Hit,
        }

        enum ActorAttackFollowSize : byte
        {
            Small,
            Medium,
            Large,
        }

        static ActorAttackFollowSize ResolveFollowSize(float strength)
        {
            if (strength < 0.33f)
                return ActorAttackFollowSize.Small;
            return strength < 0.66f ? ActorAttackFollowSize.Medium : ActorAttackFollowSize.Large;
        }

        static FixedString64Bytes AttackMarker(ActorWeaponAttackType attackType, AttackMarkerKind kind)
        {
            return attackType switch
            {
                ActorWeaponAttackType.Slash => kind switch
                {
                    AttackMarkerKind.Start => Fixed64("slash start"),
                    AttackMarkerKind.MaxAttack => Fixed64("slash max attack"),
                    _ => Fixed64("slash hit"),
                },
                ActorWeaponAttackType.Thrust => kind switch
                {
                    AttackMarkerKind.Start => Fixed64("thrust start"),
                    AttackMarkerKind.MaxAttack => Fixed64("thrust max attack"),
                    _ => Fixed64("thrust hit"),
                },
                _ => kind switch
                {
                    AttackMarkerKind.Start => Fixed64("chop start"),
                    AttackMarkerKind.MaxAttack => Fixed64("chop max attack"),
                    _ => Fixed64("chop hit"),
                },
            };
        }

        static FixedString64Bytes FollowMarker(ActorWeaponAttackType attackType, ActorAttackFollowSize size, bool start)
        {
            string prefix = attackType switch
            {
                ActorWeaponAttackType.Slash => "slash",
                ActorWeaponAttackType.Thrust => "thrust",
                _ => "chop",
            };
            string sizeText = size switch
            {
                ActorAttackFollowSize.Medium => "medium",
                ActorAttackFollowSize.Large => "large",
                _ => "small",
            };
            return Fixed64($"{prefix} {sizeText} follow {(start ? "start" : "stop")}");
        }

        static FixedString64Bytes Fixed64(string value) => new(value);
    }
}
#endif
