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
    public partial struct ActorWeaponAnimationSystem : ISystem
    {
        const int CombatOverlayPriority = 40;
        const ulong SelfStartMarkerHash = 10179819465993188655UL;
        const ulong SelfReleaseMarkerHash = 18149065862640264910UL;
        const ulong SelfStopMarkerHash = 6163052633529419029UL;
        const ulong TouchStartMarkerHash = 9750376835238723124UL;
        const ulong TouchReleaseMarkerHash = 16570883444969621989UL;
        const ulong TouchStopMarkerHash = 10699017043350398000UL;
        const ulong TargetStartMarkerHash = 989263509605477796UL;
        const ulong TargetReleaseMarkerHash = 8348009748216247445UL;
        const ulong TargetStopMarkerHash = 10328737979966807904UL;

        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<ActorAnimationBlobCatalog>();
            systemState.RequireForUpdate<ActorWeaponAnimationState>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            var catalogRef = SystemAPI.GetSingleton<ActorAnimationBlobCatalog>().Blob;
            if (!catalogRef.IsCreated)
                return;

            ref var catalog = ref catalogRef.Value;
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            bool markedRenderOwnersDirty = false;
            foreach (var (presentation, movementState, weaponState, overlays, entity) in
                     SystemAPI.Query<RefRO<ActorPresentation>, RefRO<MorrowindMovementState>, RefRW<ActorWeaponAnimationState>, DynamicBuffer<ActorAnimationOverlayState>>()
                         .WithEntityAccess())
            {
                bool hadRenderOwner = HasRigidEquipmentRenderOwner(weaponState.ValueRO);
                UpdateWeaponAnimation(
                    ref catalog,
                    presentation.ValueRO,
                    movementState.ValueRO,
                    ref weaponState.ValueRW,
                    overlays);
                if (hadRenderOwner != HasRigidEquipmentRenderOwner(weaponState.ValueRO))
                    markedRenderOwnersDirty |= MarkRigidEquipmentRenderOwnersDirty(ref systemState, ref ecb, entity);
            }

            if (markedRenderOwnersDirty)
                ecb.Playback(systemState.EntityManager);
            ecb.Dispose();
        }

        bool MarkRigidEquipmentRenderOwnersDirty(ref SystemState systemState, ref EntityCommandBuffer ecb, Entity actor)
        {
            bool marked = false;
            foreach (var (attachment, entity) in
                     SystemAPI.Query<RefRO<ActorRigidEquipmentAttachment>>()
                         .WithEntityAccess())
            {
                if (attachment.ValueRO.Actor != actor)
                    continue;

                if (systemState.EntityManager.HasComponent<ActorRigidEquipmentRenderOwnerDirty>(entity))
                    ecb.SetComponentEnabled<ActorRigidEquipmentRenderOwnerDirty>(entity, true);
                else
                    ecb.AddComponent<ActorRigidEquipmentRenderOwnerDirty>(entity);
                marked = true;
            }

            return marked;
        }

        static bool HasRigidEquipmentRenderOwner(in ActorWeaponAnimationState state)
            => state.Drawn != 0 || state.Phase == ActorWeaponAnimationPhase.Equipping;

        internal static void UpdateWeaponAnimation(
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            in MorrowindMovementState movementState,
            ref ActorWeaponAnimationState state,
            DynamicBuffer<ActorAnimationOverlayState> overlays)
        {
            bool supportedMelee = ActorWeaponAnimationUtility.IsSupportedMelee(state.WeaponType);
            bool spellWeapon = state.WeaponType == ActorWeaponAnimationUtility.SpellWeaponType;
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
                        if (spellWeapon && state.SpellCastPressed != 0)
                            StartSpellCast(ref catalog, presentation, ref state, overlays);
                        else if (supportedMelee && state.AttackPressed != 0)
                            StartWindUp(ref catalog, presentation, movementState, ref state, overlays);
                    }
                    break;
                case ActorWeaponAnimationPhase.SpellCasting:
                    UpdateSpellCast(ref catalog, presentation, ref state, overlays);
                    break;
                case ActorWeaponAnimationPhase.AttackWindUp:
                    UpdateWindUp(ref catalog, presentation, ref state, overlays);
                    break;
                case ActorWeaponAnimationPhase.AttackRelease:
                    if (!IsUpperBodyOverlayActive(overlays))
                    {
                        QueueMeleeHit(ref state);
                        StartFollow(ref catalog, presentation, ref state, overlays);
                    }
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
            state.SpellCastPressed = 0;
        }

        static bool IsAttacking(ActorWeaponAnimationPhase phase)
            => phase == ActorWeaponAnimationPhase.AttackWindUp
               || phase == ActorWeaponAnimationPhase.AttackRelease
               || phase == ActorWeaponAnimationPhase.AttackFollow
               || phase == ActorWeaponAnimationPhase.SpellCasting;

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
            state.AttackMinTime = 0f;
            state.AttackMaxTime = 0f;

            ulong start = draw
                ? ActorWeaponAnimationUtility.EquipStartMarkerHash
                : ActorWeaponAnimationUtility.UnequipStartMarkerHash;
            ulong stop = draw
                ? ActorWeaponAnimationUtility.EquipStopMarkerHash
                : ActorWeaponAnimationUtility.UnequipStopMarkerHash;
            if (TryResolveWeaponWindow(ref catalog, presentation, state.WeaponType, start, stop, out var group, out float startTime, out float stopTime))
            {
                StartUpperBodyOverlay(overlays, group, startTime, stopTime, holdAtStop: draw);
                state.Phase = ActorWeaponAnimationPhase.Equipping;
                return;
            }

            if (state.WeaponType == ActorWeaponAnimationUtility.SpellWeaponType)
                throw new System.InvalidOperationException($"[VVardenfell][Magic] Spell ready animation is missing spellcast {(draw ? "equip" : "unequip")} start/stop markers.");

            state.Phase = draw ? ActorWeaponAnimationPhase.Equipped : ActorWeaponAnimationPhase.Hidden;
            if (draw)
                EnsureReadyHold(ref catalog, presentation, ref state, overlays);
            else
                ClearUpperBodyOverlay(overlays);
        }

        static void StartSpellCast(
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            ref ActorWeaponAnimationState state,
            DynamicBuffer<ActorAnimationOverlayState> overlays)
        {
            ResolveSpellCastMarkers(state.SpellCastRange, out ulong startHash, out ulong releaseHash, out ulong stopHash);
            if (!TryResolveWeaponWindow(
                    ref catalog,
                    presentation,
                    ActorWeaponAnimationUtility.SpellWeaponType,
                    startHash,
                    stopHash,
                    out var group,
                    out float startTime,
                    out float stopTime))
            {
                throw new System.InvalidOperationException($"[VVardenfell][Magic] Spellcast animation has no valid range window for range {state.SpellCastRange}.");
            }

            if (!TryResolveWeaponMarker(ref catalog, presentation, ActorWeaponAnimationUtility.SpellWeaponType, releaseHash, out float releaseTime)
                || releaseTime < startTime
                || releaseTime > stopTime)
            {
                throw new System.InvalidOperationException($"[VVardenfell][Magic] Spellcast animation has no valid release marker for range {state.SpellCastRange}.");
            }

            state.Phase = ActorWeaponAnimationPhase.SpellCasting;
            StartUpperBodyOverlay(overlays, group, startTime, stopTime, holdAtStop: false);
        }

        static void UpdateSpellCast(
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            ref ActorWeaponAnimationState state,
            DynamicBuffer<ActorAnimationOverlayState> overlays)
        {
            ResolveSpellCastMarkers(state.SpellCastRange, out _, out ulong releaseHash, out _);
            if (!TryResolveWeaponMarker(ref catalog, presentation, ActorWeaponAnimationUtility.SpellWeaponType, releaseHash, out float releaseTime))
                throw new System.InvalidOperationException($"[VVardenfell][Magic] Spellcast animation has no release marker for range {state.SpellCastRange}.");

            if (!TryGetUpperBodyOverlay(overlays, out _, out var overlay)
                || !ActorAnimationPlaybackUtility.CanSample(overlay.Playback))
            {
                FinishSpellCast(ref catalog, presentation, ref state, overlays);
                return;
            }

            if (state.SpellCastReleasePending == 0
                && overlay.Playback.PreviousTime < releaseTime
                && overlay.Playback.Time >= releaseTime)
            {
                state.SpellCastReleasePending = 1;
                state.SpellCastReleaseSourceKind = state.SpellCastSourceKind;
                state.SpellCastReleaseSpell = state.SpellCastSpell;
                state.SpellCastReleaseEnchantment = state.SpellCastEnchantment;
                state.SpellCastReleaseItemContent = state.SpellCastItemContent;
                state.SpellCastReleaseInventoryIndex = state.SpellCastInventoryIndex;
            }

            if (!ActorAnimationPlaybackUtility.IsActive(overlay.Playback) || PlaybackReachedStop(overlay.Playback))
                FinishSpellCast(ref catalog, presentation, ref state, overlays);
        }

        static void FinishSpellCast(
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            ref ActorWeaponAnimationState state,
            DynamicBuffer<ActorAnimationOverlayState> overlays)
        {
            state.Phase = state.Drawn != 0
                ? ActorWeaponAnimationPhase.Equipped
                : ActorWeaponAnimationPhase.Hidden;
            state.SpellCastPressed = 0;
            state.SpellCastRange = 0;
            state.SpellCastSpell = default;
            if (state.Drawn != 0)
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
            state.AttackType = state.AiAttackTypeOverride != 0
                ? state.AiAttackType
                : ResolveAttackType(movementState.LocalMove);
            state.AiAttackTypeOverride = 0;
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

            if (!TryResolveWeaponMarker(
                    ref catalog,
                    presentation,
                    state.WeaponType,
                    AttackMarker(state.AttackType, AttackMarkerKind.MinAttack),
                    out float minAttackTime)
                || minAttackTime >= stopTime)
            {
                throw new System.InvalidOperationException(
                    "[VVardenfell][Combat] Weapon animation has no valid min-to-max attack marker window.");
            }

            state.AttackStrength = 0f;
            state.AttackMinTime = minAttackTime;
            state.AttackMaxTime = stopTime;
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

            float span = state.AttackMaxTime - state.AttackMinTime;
            state.AttackStrength = span > 0.0001f
                ? math.saturate((overlay.Playback.Time - state.AttackMinTime) / span)
                : 1f;

            if (state.ReleaseQueued == 0 || overlay.Playback.Time < state.AttackMinTime)
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
                QueueMeleeSwing(ref state);
                state.Phase = ActorWeaponAnimationPhase.AttackRelease;
                float releaseStartTime = ResolveReleaseStartTime(
                    ref catalog,
                    presentation,
                    state,
                    startTime,
                    stopTime);
                overlay = overlays[overlayIndex];
                ActorAnimationPlaybackUtility.StartWindow(ref overlay.Playback, group, startTime, stopTime, holdAtStop: false);
                overlay.Playback.PreviousTime = releaseStartTime;
                overlay.Playback.Time = releaseStartTime;
                overlay.Weight = 1f;
                overlay.Priority = CombatOverlayPriority;
                overlay.Mask = ActorAnimationBlendMask.UpperBody;
                overlays[overlayIndex] = overlay;
                return;
            }

            throw new System.InvalidOperationException(
                "[VVardenfell][Combat] Weapon animation has no release-to-hit marker window.");
        }

        static void QueueMeleeSwing(ref ActorWeaponAnimationState state)
        {
            if (state.MeleeSwingPending != 0)
                return;

            state.MeleeSwingPending = 1;
            state.MeleeSwingAttackStrength = state.AttackStrength;
            state.MeleeSwingWeaponContent = state.WeaponContent;
        }

        static void QueueMeleeHit(ref ActorWeaponAnimationState state)
        {
            if (state.MeleeHitPending != 0)
                return;

            state.MeleeHitPending = 1;
            state.MeleeHitAttackType = state.AttackType;
            state.MeleeHitAttackStrength = state.AttackStrength;
            state.MeleeHitWeaponContent = state.WeaponContent;
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
            state.AttackMinTime = 0f;
            state.AttackMaxTime = 0f;
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
                    ActorWeaponAnimationUtility.EquipStartMarkerHash,
                    ActorWeaponAnimationUtility.EquipStopMarkerHash,
                    out var group,
                    out _,
                    out float stopTime))
            {
                if (state.WeaponType == ActorWeaponAnimationUtility.SpellWeaponType)
                    throw new System.InvalidOperationException("[VVardenfell][Magic] Spell ready hold requires spellcast equip start/stop markers.");
                return;
            }

            StartUpperBodyOverlay(overlays, group, stopTime, stopTime, holdAtStop: true);
        }

        static void ResolveSpellCastMarkers(byte range, out ulong startHash, out ulong releaseHash, out ulong stopHash)
        {
            switch (range)
            {
                case 0:
                    startHash = SelfStartMarkerHash;
                    releaseHash = SelfReleaseMarkerHash;
                    stopHash = SelfStopMarkerHash;
                    return;
                case 1:
                    startHash = TouchStartMarkerHash;
                    releaseHash = TouchReleaseMarkerHash;
                    stopHash = TouchStopMarkerHash;
                    return;
                case 2:
                    startHash = TargetStartMarkerHash;
                    releaseHash = TargetReleaseMarkerHash;
                    stopHash = TargetStopMarkerHash;
                    return;
                default:
                    throw new System.InvalidOperationException($"[VVardenfell][Magic] Unsupported spellcast animation range {range}.");
            }
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
            ulong startValueHash,
            ulong stopValueHash,
            out ActorAnimationGroupBlob group,
            out float start,
            out float stop)
        {
            group = default;
            start = 0f;
            stop = 0f;
            if (!ActorWeaponAnimationUtility.TryResolveGroupHashes(weaponType, out ulong primaryHash, out ulong fallbackHash))
                return false;

            if (TryResolveGroupWindow(ref catalog, presentation, primaryHash, startValueHash, stopValueHash, out group, out start, out stop))
                return true;

            return fallbackHash != 0UL
                   && TryResolveGroupWindow(ref catalog, presentation, fallbackHash, startValueHash, stopValueHash, out group, out start, out stop);
        }

        static bool TryResolveGroupWindow(
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            ulong groupHash,
            ulong startValueHash,
            ulong stopValueHash,
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

            return ActorAnimationMarkerWindowUtility.TryResolveWindow(ref catalog, group, startValueHash, stopValueHash, out start, out stop);
        }

        static bool TryResolveWeaponMarker(
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            int weaponType,
            ulong valueHash,
            out float time)
        {
            time = 0f;
            if (!ActorWeaponAnimationUtility.TryResolveGroupHashes(weaponType, out ulong primaryHash, out ulong fallbackHash))
                return false;

            if (TryResolveGroupMarker(ref catalog, presentation, primaryHash, valueHash, out time))
                return true;

            return fallbackHash != 0UL
                   && TryResolveGroupMarker(ref catalog, presentation, fallbackHash, valueHash, out time);
        }

        static bool TryResolveGroupMarker(
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            ulong groupHash,
            ulong valueHash,
            out float time)
        {
            if (!ActorAnimationGroupLookupUtility.TryResolveGroup(ref catalog, presentation, groupHash, out var group))
            {
                time = 0f;
                return false;
            }

            return ActorAnimationMarkerWindowUtility.TryResolveMarker(ref catalog, group, valueHash, out time);
        }

        static float ResolveReleaseStartTime(
            ref ActorAnimationCatalogBlob catalog,
            in ActorPresentation presentation,
            in ActorWeaponAnimationState state,
            float releaseStartTime,
            float hitTime)
        {
            float strength = math.saturate(state.AttackStrength);
            float startPoint = 1f - strength;
            if (TryResolveWeaponMarker(
                    ref catalog,
                    presentation,
                    state.WeaponType,
                    AttackMarker(state.AttackType, AttackMarkerKind.MinHit),
                    out float minHitTime)
                && releaseStartTime <= minHitTime
                && minHitTime < hitTime)
            {
                startPoint *= (minHitTime - releaseStartTime) / (hitTime - releaseStartTime);
            }

            return math.lerp(releaseStartTime, hitTime, math.saturate(startPoint));
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
            MinAttack,
            MaxAttack,
            MinHit,
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

        static ulong AttackMarker(ActorWeaponAttackType attackType, AttackMarkerKind kind)
        {
            return attackType switch
            {
                ActorWeaponAttackType.Slash => kind switch
                {
                    AttackMarkerKind.Start => ActorWeaponAnimationUtility.SlashStartMarkerHash,
                    AttackMarkerKind.MinAttack => ActorWeaponAnimationUtility.SlashMinAttackMarkerHash,
                    AttackMarkerKind.MaxAttack => ActorWeaponAnimationUtility.SlashMaxAttackMarkerHash,
                    AttackMarkerKind.MinHit => ActorWeaponAnimationUtility.SlashMinHitMarkerHash,
                    _ => ActorWeaponAnimationUtility.SlashHitMarkerHash,
                },
                ActorWeaponAttackType.Thrust => kind switch
                {
                    AttackMarkerKind.Start => ActorWeaponAnimationUtility.ThrustStartMarkerHash,
                    AttackMarkerKind.MinAttack => ActorWeaponAnimationUtility.ThrustMinAttackMarkerHash,
                    AttackMarkerKind.MaxAttack => ActorWeaponAnimationUtility.ThrustMaxAttackMarkerHash,
                    AttackMarkerKind.MinHit => ActorWeaponAnimationUtility.ThrustMinHitMarkerHash,
                    _ => ActorWeaponAnimationUtility.ThrustHitMarkerHash,
                },
                _ => kind switch
                {
                    AttackMarkerKind.Start => ActorWeaponAnimationUtility.ChopStartMarkerHash,
                    AttackMarkerKind.MinAttack => ActorWeaponAnimationUtility.ChopMinAttackMarkerHash,
                    AttackMarkerKind.MaxAttack => ActorWeaponAnimationUtility.ChopMaxAttackMarkerHash,
                    AttackMarkerKind.MinHit => ActorWeaponAnimationUtility.ChopMinHitMarkerHash,
                    _ => ActorWeaponAnimationUtility.ChopHitMarkerHash,
                },
            };
        }

        static ulong FollowMarker(ActorWeaponAttackType attackType, ActorAttackFollowSize size, bool start)
        {
            return attackType switch
            {
                ActorWeaponAttackType.Slash => size switch
                {
                    ActorAttackFollowSize.Medium => start ? ActorWeaponAnimationUtility.SlashMediumFollowStartMarkerHash : ActorWeaponAnimationUtility.SlashMediumFollowStopMarkerHash,
                    ActorAttackFollowSize.Large => start ? ActorWeaponAnimationUtility.SlashLargeFollowStartMarkerHash : ActorWeaponAnimationUtility.SlashLargeFollowStopMarkerHash,
                    _ => start ? ActorWeaponAnimationUtility.SlashSmallFollowStartMarkerHash : ActorWeaponAnimationUtility.SlashSmallFollowStopMarkerHash,
                },
                ActorWeaponAttackType.Thrust => size switch
                {
                    ActorAttackFollowSize.Medium => start ? ActorWeaponAnimationUtility.ThrustMediumFollowStartMarkerHash : ActorWeaponAnimationUtility.ThrustMediumFollowStopMarkerHash,
                    ActorAttackFollowSize.Large => start ? ActorWeaponAnimationUtility.ThrustLargeFollowStartMarkerHash : ActorWeaponAnimationUtility.ThrustLargeFollowStopMarkerHash,
                    _ => start ? ActorWeaponAnimationUtility.ThrustSmallFollowStartMarkerHash : ActorWeaponAnimationUtility.ThrustSmallFollowStopMarkerHash,
                },
                _ => size switch
                {
                    ActorAttackFollowSize.Medium => start ? ActorWeaponAnimationUtility.ChopMediumFollowStartMarkerHash : ActorWeaponAnimationUtility.ChopMediumFollowStopMarkerHash,
                    ActorAttackFollowSize.Large => start ? ActorWeaponAnimationUtility.ChopLargeFollowStartMarkerHash : ActorWeaponAnimationUtility.ChopLargeFollowStopMarkerHash,
                    _ => start ? ActorWeaponAnimationUtility.ChopSmallFollowStartMarkerHash : ActorWeaponAnimationUtility.ChopSmallFollowStopMarkerHash,
                },
            };
        }
    }
}
#endif
