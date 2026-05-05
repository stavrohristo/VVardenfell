using System;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Shell
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(RuntimeShellActionSystem))]
    public partial class RuntimeRestMenuActionSystem : SystemBase
    {
        EntityQuery _playerQuery;

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<ActorAttributeSet>(),
                ComponentType.ReadOnly<ActorSkillSet>(),
                ComponentType.ReadWrite<ActorVitalSet>(),
                ComponentType.ReadOnly<ActorEffectStatModifiers>(),
                ComponentType.ReadOnly<ActorDerivedMovementStats>(),
                ComponentType.ReadOnly<MorrowindMovementSpeed>(),
                ComponentType.ReadWrite<ActorActiveMagicEffect>(),
                ComponentType.ReadOnly<ActorActiveMagicEffectDirty>());

            RequireForUpdate<RuntimeShellState>();
            RequireForUpdate<RuntimeShellActionRequest>();
            RequireForUpdate<MorrowindTimeState>();
            RequireForUpdate<MorrowindTimeAdvanceRequest>();
            RequireForUpdate<RuntimeContentBlobReference>();
            RequireForUpdate(_playerQuery);
        }

        protected override void OnUpdate()
        {
            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            ref var request = ref SystemAPI.GetSingletonRW<RuntimeShellActionRequest>().ValueRW;

            var action = (RuntimeShellRestMenuActionId)request.RestMenuAction;
            if (action != RuntimeShellRestMenuActionId.None)
            {
                HandleAction(ref shell, ref request, action);
                RuntimeShellStateUtility.SyncGameplayGateAndCursor(ref shell);
                return;
            }

            if (shell.RestMenuAdvancing == 0)
                return;

            if (shell.RestMenuProgressHours >= shell.RestMenuTargetHours)
            {
                RuntimeShellStateUtility.CloseRestMenu(ref shell);
                RuntimeShellStateUtility.SyncGameplayGateAndCursor(ref shell);
                return;
            }

            AdvanceOneHour(ref shell);
            RuntimeShellStateUtility.SyncGameplayGateAndCursor(ref shell);
        }

        void HandleAction(ref RuntimeShellState shell, ref RuntimeShellActionRequest request, RuntimeShellRestMenuActionId action)
        {
            int requestedHours = request.RestMenuHours;
            request.RestMenuAction = 0;
            request.RestMenuHours = 0;

            switch (action)
            {
                case RuntimeShellRestMenuActionId.SetHours:
                    EnsureRestMenuOpen(shell, "set hours");
                    shell.RestMenuSelectedHours = RuntimeRestUtility.ClampHours(requestedHours);
                    return;

                case RuntimeShellRestMenuActionId.Cancel:
                    RuntimeShellStateUtility.CloseRestMenu(ref shell);
                    return;

                case RuntimeShellRestMenuActionId.Start:
                    EnsureRestMenuOpen(shell, "start rest");
                    StartAdvancing(ref shell, RuntimeRestUtility.ClampHours(shell.RestMenuSelectedHours), shell.RestMenuCanSleep != 0);
                    return;

                case RuntimeShellRestMenuActionId.UntilHealed:
                    EnsureRestMenuOpen(shell, "rest until healed");
                    if (shell.RestMenuCanSleep == 0)
                        throw new InvalidOperationException("[VVardenfell][Rest] Until Healed was requested while sleep/rest is unavailable.");

                    StartAdvancing(ref shell, ComputeUntilHealedHours(), sleeping: true);
                    return;

                default:
                    throw new InvalidOperationException($"[VVardenfell][Rest] Unsupported rest menu action '{action}'.");
            }
        }

        void StartAdvancing(ref RuntimeShellState shell, int hours, bool sleeping)
        {
            if (shell.RestDisabled != 0)
            {
                RuntimeShellStateUtility.ShowMessageBox(ref shell, "You cannot rest now.");
                return;
            }

            shell.RestMenuOpen = 1;
            shell.RestMenuAdvancing = 1;
            shell.RestMenuSleeping = sleeping ? (byte)1 : (byte)0;
            shell.RestMenuProgressHours = 0;
            shell.RestMenuTargetHours = RuntimeRestUtility.ClampHours(hours);
            shell.PlayerSleeping = sleeping ? (byte)1 : (byte)0;
        }

        void AdvanceOneHour(ref RuntimeShellState shell)
        {
            bool sleeping = shell.RestMenuSleeping != 0;
            RestorePlayerVitalsForHour(sleeping);

            var timeRequests = SystemAPI.GetSingletonBuffer<MorrowindTimeAdvanceRequest>();
            timeRequests.Add(new MorrowindTimeAdvanceRequest
            {
                Hours = 1f,
                Kind = (byte)(sleeping ? MorrowindTimeAdvanceKind.Sleep : MorrowindTimeAdvanceKind.Rest),
            });

            shell.RestMenuProgressHours = math.min(shell.RestMenuProgressHours + 1, shell.RestMenuTargetHours);
            shell.PlayerSleeping = sleeping ? (byte)1 : (byte)0;
        }

        int ComputeUntilHealedHours()
        {
            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            var attributes = _playerQuery.GetSingleton<ActorAttributeSet>();
            var vitals = _playerQuery.GetSingleton<ActorVitalSet>();
            var activeEffects = EntityManager.GetBuffer<ActorActiveMagicEffect>(_playerQuery.GetSingletonEntity(), true);
            bool stuntedMagicka = RuntimeRestUtility.HasStuntedMagicka(activeEffects);
            return RuntimeRestUtility.ComputeUntilHealedHours(ref contentBlob, vitals, attributes, stuntedMagicka);
        }

        void RestorePlayerVitalsForHour(bool sleeping)
        {
            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            var attributes = _playerQuery.GetSingleton<ActorAttributeSet>();
            var skills = _playerQuery.GetSingleton<ActorSkillSet>();
            ref var vitals = ref _playerQuery.GetSingletonRW<ActorVitalSet>().ValueRW;
            var effectModifiers = _playerQuery.GetSingleton<ActorEffectStatModifiers>();
            var derived = _playerQuery.GetSingleton<ActorDerivedMovementStats>();
            var movementSpeed = _playerQuery.GetSingleton<MorrowindMovementSpeed>();
            var activeEffects = EntityManager.GetBuffer<ActorActiveMagicEffect>(_playerQuery.GetSingletonEntity());
            var time = SystemAPI.GetSingleton<MorrowindTimeState>();

            MorrowindActorMovementStats.ApplyVitalBases(ref contentBlob, attributes, ref vitals, initializeMissingCurrents: false);
            var context = MorrowindPlayerSpeedResolver.Build(ref contentBlob, attributes, skills, vitals, effectModifiers, derived, movementSpeed);
            vitals.CurrentFatigue = math.min(vitals.ModifiedFatigueBase, vitals.CurrentFatigue + context.GetFatigueRestorePerSecond() * 3600f);

            if (sleeping)
            {
                vitals.CurrentHealth = math.min(
                    vitals.ModifiedHealthBase,
                    vitals.CurrentHealth + RuntimeRestUtility.HealthPerSleepHour(attributes));

                float magickaRestoreHours = RuntimeRestUtility.MagickaRestoreHoursForSleep(1f, time.TimeScale, activeEffects);
                if (magickaRestoreHours > 0f)
                {
                    vitals.CurrentMagicka = math.min(
                        vitals.ModifiedMagickaBase,
                        vitals.CurrentMagicka + RuntimeRestUtility.MagickaPerSleepHour(ref contentBlob, attributes) * magickaRestoreHours);
                }
            }

            if (RuntimeRestUtility.AdvanceTimedActiveMagicEffects(activeEffects, 1f, time.TimeScale))
                EntityManager.SetComponentEnabled<ActorActiveMagicEffectDirty>(_playerQuery.GetSingletonEntity(), true);

            vitals.CurrentHealth = math.clamp(vitals.CurrentHealth, 0f, math.max(0f, vitals.ModifiedHealthBase));
            vitals.CurrentMagicka = math.clamp(vitals.CurrentMagicka, 0f, math.max(0f, vitals.ModifiedMagickaBase));
            vitals.CurrentFatigue = math.clamp(vitals.CurrentFatigue, 0f, math.max(0f, vitals.ModifiedFatigueBase));
        }

        static void EnsureRestMenuOpen(in RuntimeShellState shell, string action)
        {
            if (shell.RestMenuOpen == 0 || shell.RestMenuAdvancing != 0)
                throw new InvalidOperationException($"[VVardenfell][Rest] Cannot {action}; rest menu is not accepting input.");
        }
    }
}
