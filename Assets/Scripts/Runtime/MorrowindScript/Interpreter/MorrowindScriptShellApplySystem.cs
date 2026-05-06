using System;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindGameplayMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial struct MorrowindScriptShellApplySystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<MorrowindScriptShellRequest>();
            systemState.RequireForUpdate<RuntimeShellState>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = systemState.EntityManager.GetBuffer<MorrowindScriptShellRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            bool hasLogicalRefLookup = SystemAPI.TryGetSingleton(out LogicalRefLookup logicalRefLookup);
            for (int i = 0; i < requests.Length; i++)
                ApplyRequest(systemState.EntityManager, hasLogicalRefLookup, logicalRefLookup, ref shell, requests[i]);

            RuntimeShellStateUtility.SyncGameplayGateAndCursor(ref shell);
            requests.Clear();
        }

        static void ApplyRequest(
            EntityManager entityManager,
            bool hasLogicalRefLookup,
            in LogicalRefLookup logicalRefLookup,
            ref RuntimeShellState shell,
            in MorrowindScriptShellRequest request)
        {
            if (request.Operation == (byte)MorrowindScriptShellRequestOperation.WakeUpPlayer)
            {
                RuntimeShellStateUtility.CloseRestMenu(ref shell);
                shell.PlayerSleeping = 0;
                return;
            }

            if (request.Operation == (byte)MorrowindScriptShellRequestOperation.ShowRestMenu)
            {
                Entity bedEntity = request.TargetEntity;
                if (bedEntity == Entity.Null || !entityManager.Exists(bedEntity))
                {
                    if (!hasLogicalRefLookup)
                        throw new InvalidOperationException($"ShowRestMenu target ref={request.TargetPlacedRefId} cannot resolve without logical ref lookup.");

                    bedEntity = MorrowindRuntimeTargetResolver.ResolveLiveTarget(
                        entityManager,
                        request.TargetEntity,
                        request.TargetPlacedRefId,
                        logicalRefLookup);
                }

                if (bedEntity == Entity.Null || !entityManager.Exists(bedEntity))
                    throw new InvalidOperationException($"ShowRestMenu target ref={request.TargetPlacedRefId} is not loaded.");

                if (!entityManager.HasComponent<PlacedRefIdentity>(bedEntity))
                    throw new InvalidOperationException($"ShowRestMenu target ref={request.TargetPlacedRefId} has no placed ref identity.");

                uint bedPlacedRefId = entityManager.GetComponentData<PlacedRefIdentity>(bedEntity).Value;
                if (request.TargetPlacedRefId != 0u && bedPlacedRefId != request.TargetPlacedRefId)
                    throw new InvalidOperationException($"ShowRestMenu target mismatch requested={request.TargetPlacedRefId} actual={bedPlacedRefId}.");

                RuntimeShellStateUtility.OpenRestMenu(ref shell, bedEntity, bedPlacedRefId, canSleep: true);
                return;
            }

            if (request.Operation == (byte)MorrowindScriptShellRequestOperation.PlayBink)
            {
                if (request.MovieName.IsEmpty)
                    throw new InvalidOperationException("[VVardenfell][MWScript] PlayBink requires a non-empty movie name.");

                RuntimeShellStateUtility.OpenMovie(ref shell, request.MovieName, request.AllowSkipping != 0);
                return;
            }

            if (request.Operation == (byte)MorrowindScriptShellRequestOperation.ScreenFade)
            {
                shell.ScreenFadeStartAlpha = shell.ScreenFadeAlpha;
                shell.ScreenFadeTargetAlpha = request.FadeOut != 0 ? 1f : 0f;
                shell.ScreenFadeDuration = request.Duration < 0f ? 0f : request.Duration;
                shell.ScreenFadeElapsed = 0f;
                if (shell.ScreenFadeDuration <= 0f)
                {
                    shell.ScreenFadeAlpha = shell.ScreenFadeTargetAlpha;
                    shell.ScreenFadeElapsed = shell.ScreenFadeDuration;
                }
                return;
            }

            if (request.Operation == (byte)MorrowindScriptShellRequestOperation.PlayerControls)
            {
                shell.PlayerControlsDisabled = request.Enabled != 0 ? (byte)0 : (byte)1;
                return;
            }

            if (request.Operation == (byte)MorrowindScriptShellRequestOperation.PlayerFighting)
            {
                shell.PlayerFightingDisabled = request.Enabled != 0 ? (byte)0 : (byte)1;
                return;
            }

            if (request.Operation == (byte)MorrowindScriptShellRequestOperation.PlayerJumping)
            {
                shell.PlayerJumpingDisabled = request.Enabled != 0 ? (byte)0 : (byte)1;
                return;
            }

            if (request.Operation == (byte)MorrowindScriptShellRequestOperation.PlayerMagic)
            {
                shell.PlayerMagicDisabled = request.Enabled != 0 ? (byte)0 : (byte)1;
                return;
            }

            if (request.Operation == (byte)MorrowindScriptShellRequestOperation.PlayerViewSwitch)
            {
                shell.PlayerViewSwitchDisabled = request.Enabled != 0 ? (byte)0 : (byte)1;
                return;
            }

            if (request.Operation == (byte)MorrowindScriptShellRequestOperation.VanityMode)
            {
                shell.VanityModeDisabled = request.Enabled != 0 ? (byte)0 : (byte)1;
                return;
            }

            if (request.Operation == (byte)MorrowindScriptShellRequestOperation.Rest)
            {
                shell.RestDisabled = request.Enabled != 0 ? (byte)0 : (byte)1;
                return;
            }

            if (request.Operation == (byte)MorrowindScriptShellRequestOperation.Teleporting)
            {
                shell.TeleportingDisabled = request.Enabled != 0 ? (byte)0 : (byte)1;
                return;
            }

            if (request.Operation == (byte)MorrowindScriptShellRequestOperation.MenuEnabled)
            {
                byte disabled = request.Enabled != 0 ? (byte)0 : (byte)1;
                switch (request.MenuKind)
                {
                    case 1:
                        shell.InventoryMenuDisabled = disabled;
                        if (disabled != 0)
                            shell.InventoryOpen = 0;
                        return;
                    case 2:
                        shell.StatsMenuDisabled = disabled;
                        return;
                    case 3:
                        shell.MagicMenuDisabled = disabled;
                        return;
                    case 4:
                        shell.MapMenuDisabled = disabled;
                        return;
                    case 5:
                        shell.NameMenuDisabled = disabled;
                        return;
                    case 6:
                        shell.RaceMenuDisabled = disabled;
                        return;
                    case 7:
                        shell.ClassMenuDisabled = disabled;
                        return;
                    case 8:
                        shell.BirthMenuDisabled = disabled;
                        return;
                    case 9:
                        shell.StatReviewMenuDisabled = disabled;
                        return;
                    default:
                        throw new InvalidOperationException($"[VVardenfell][MWScript] Unsupported shell menu kind {request.MenuKind}.");
                }
            }

            throw new InvalidOperationException($"[VVardenfell][MWScript] Unsupported shell request operation {request.Operation}.");
        }
    }
}
