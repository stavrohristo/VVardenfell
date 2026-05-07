using System;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Player;
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
            systemState.RequireForUpdate<CharacterGenerationState>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = systemState.EntityManager.GetBuffer<MorrowindScriptShellRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            ref var charGen = ref SystemAPI.GetSingletonRW<CharacterGenerationState>().ValueRW;
            Entity charGenEntity = SystemAPI.GetSingletonEntity<CharacterGenerationState>();
            bool hasLogicalRefLookup = SystemAPI.TryGetSingleton(out LogicalRefLookup logicalRefLookup);
            for (int i = 0; i < requests.Length; i++)
                ApplyRequest(systemState.EntityManager, charGenEntity, hasLogicalRefLookup, logicalRefLookup, ref shell, ref charGen, requests[i]);

            requests.Clear();
        }

        static void ApplyRequest(
            EntityManager entityManager,
            Entity charGenEntity,
            bool hasLogicalRefLookup,
            in LogicalRefLookup logicalRefLookup,
            ref RuntimeShellState shell,
            ref CharacterGenerationState charGen,
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

            if (request.Operation == (byte)MorrowindScriptShellRequestOperation.PlayerLooking)
            {
                shell.PlayerLookingDisabled = request.Enabled != 0 ? (byte)0 : (byte)1;
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
                        ApplyCharacterGenerationMenu(entityManager, charGenEntity, ref shell, ref charGen, CharacterGenerationMenu.Name, request.Enabled != 0, disabled);
                        return;
                    case 6:
                        shell.RaceMenuDisabled = disabled;
                        ApplyCharacterGenerationMenu(entityManager, charGenEntity, ref shell, ref charGen, CharacterGenerationMenu.Race, request.Enabled != 0, disabled);
                        return;
                    case 7:
                        shell.ClassMenuDisabled = disabled;
                        ApplyCharacterGenerationMenu(entityManager, charGenEntity, ref shell, ref charGen, CharacterGenerationMenu.ClassChoice, request.Enabled != 0, disabled);
                        return;
                    case 8:
                        shell.BirthMenuDisabled = disabled;
                        ApplyCharacterGenerationMenu(entityManager, charGenEntity, ref shell, ref charGen, CharacterGenerationMenu.Birth, request.Enabled != 0, disabled);
                        return;
                    case 9:
                        shell.StatReviewMenuDisabled = disabled;
                        ApplyCharacterGenerationMenu(entityManager, charGenEntity, ref shell, ref charGen, CharacterGenerationMenu.Review, request.Enabled != 0, disabled);
                        return;
                    default:
                        throw new InvalidOperationException($"[VVardenfell][MWScript] Unsupported shell menu kind {request.MenuKind}.");
                }
            }

            throw new InvalidOperationException($"[VVardenfell][MWScript] Unsupported shell request operation {request.Operation}.");
        }

        static void ApplyCharacterGenerationMenu(
            EntityManager entityManager,
            Entity charGenEntity,
            ref RuntimeShellState shell,
            ref CharacterGenerationState charGen,
            CharacterGenerationMenu menu,
            bool enabled,
            byte disabled)
        {
            if (!enabled)
            {
                if ((CharacterGenerationMenu)charGen.CurrentMenu == menu)
                {
                    CharacterGenerationUtility.Close(ref charGen);
                    RuntimeShellStateUtility.CloseCharacterGeneration(ref shell);
                    EnsureCharGenStage(entityManager, charGenEntity, in charGen);
                }
                return;
            }

            if (disabled != 0)
                throw new InvalidOperationException($"[VVardenfell][MWScript] Cannot open disabled character generation menu {menu}.");

            CharacterGenerationUtility.OpenMenu(ref charGen, menu);
            EnsureCharGenStage(entityManager, charGenEntity, in charGen);
            RuntimeShellStateUtility.OpenCharacterGeneration(ref shell);
        }

        static void EnsureCharGenStage(EntityManager entityManager, Entity charGenEntity, in CharacterGenerationState charGen)
        {
            var stage = new CharGenStage
            {
                Stage = charGen.Stage,
                Menu = charGen.CurrentMenu,
                Finalized = charGen.Finalized,
            };

            if (entityManager.HasComponent<CharGenStage>(charGenEntity))
                entityManager.SetComponentData(charGenEntity, stage);
            else
                entityManager.AddComponentData(charGenEntity, stage);
        }
    }
}
