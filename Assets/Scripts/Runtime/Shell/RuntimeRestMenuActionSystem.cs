using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Shell
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(RuntimeShellActionSystem))]
    public partial struct RuntimeRestMenuActionSystem : ISystem
    {
        const float RestTimelapseRealSecondsPerGameHour = 1f;

        EntityQuery _playerQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _playerQuery = systemState.GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<PlayerTag>(),
                    ComponentType.ReadOnly<ActorAttributeSet>(),
                    ComponentType.ReadOnly<ActorSkillSet>(),
                    ComponentType.ReadWrite<ActorVitalSet>(),
                    ComponentType.ReadOnly<ActorEffectStatModifiers>(),
                    ComponentType.ReadOnly<ActorDerivedMovementStats>(),
                    ComponentType.ReadOnly<MorrowindMovementSpeed>(),
                    ComponentType.ReadWrite<ActorActiveMagicEffect>(),
                    ComponentType.ReadOnly<ActorActiveMagicEffectDirty>(),
                },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });

            systemState.RequireForUpdate<RuntimeShellState>();
            systemState.RequireForUpdate<RuntimeShellActionRequest>();
            systemState.RequireForUpdate<MorrowindTimeState>();
            systemState.RequireForUpdate<MorrowindTimeAdvanceRequest>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
            systemState.RequireForUpdate(_playerQuery);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {
            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            ref var request = ref SystemAPI.GetSingletonRW<RuntimeShellActionRequest>().ValueRW;

            var action = (RuntimeShellRestMenuActionId)request.RestMenuAction;
            if (action != RuntimeShellRestMenuActionId.None)
            {
                HandleAction(ref systemState, ref shell, ref request, action);
                return;
            }

            if (shell.RestMenuAdvancing == 0)
                return;

            if (IsPlayerDead())
            {
                RuntimeShellStateUtility.CloseRestMenu(ref shell);
                return;
            }

            if (shell.RestMenuProgressHours >= shell.RestMenuTargetHours)
            {
                RuntimeShellStateUtility.CloseRestMenu(ref shell);
                return;
            }

            AdvanceTimelapse(ref systemState, ref shell);
        }

        void HandleAction(ref SystemState systemState, ref RuntimeShellState shell, ref RuntimeShellActionRequest request, RuntimeShellRestMenuActionId action)
        {
            int requestedHours = request.RestMenuHours;
            request.RestMenuAction = 0;
            request.RestMenuHours = 0;

            switch (action)
            {
                case RuntimeShellRestMenuActionId.SetHours:
                    EnsureRestMenuOpen(shell);
                    shell.RestMenuSelectedHours = RuntimeRestUtility.ClampHours(requestedHours);
                    return;

                case RuntimeShellRestMenuActionId.Cancel:
                    RuntimeShellStateUtility.CloseRestMenu(ref shell);
                    return;

                case RuntimeShellRestMenuActionId.Start:
                    EnsureRestMenuOpen(shell);
                    StartAdvancing(ref shell, RuntimeRestUtility.ClampHours(shell.RestMenuSelectedHours), shell.RestMenuCanSleep != 0);
                    return;

                case RuntimeShellRestMenuActionId.UntilHealed:
                    EnsureRestMenuOpen(shell);
                    if (shell.RestMenuCanSleep == 0)
                        throw new InvalidOperationException("[VVardenfell][Rest] Until Healed was requested while sleep/rest is unavailable.");

                    StartAdvancing(ref shell, ComputeUntilHealedHours(ref systemState), sleeping: true);
                    return;

                default:
                    throw new InvalidOperationException("[VVardenfell][Rest] Unsupported rest menu action.");
            }
        }

        void StartAdvancing(ref RuntimeShellState shell, int hours, bool sleeping)
        {
            if (shell.RestDisabled != 0)
            {
                RuntimeShellStateUtility.ShowMessageBox(ref shell, BuildCannotRestNowText());
                return;
            }

            shell.RestMenuOpen = 1;
            shell.RestMenuAdvancing = 1;
            shell.RestMenuSleeping = sleeping ? (byte)1 : (byte)0;
            shell.RestMenuProgressHours = 0;
            shell.RestMenuTargetHours = RuntimeRestUtility.ClampHours(hours);
            shell.RestMenuProgressHourFraction = 0f;
            shell.PlayerSleeping = sleeping ? (byte)1 : (byte)0;
        }

        void AdvanceTimelapse(ref SystemState systemState, ref RuntimeShellState shell)
        {
            bool sleeping = shell.RestMenuSleeping != 0;
            float remainingHours = math.max(0f, shell.RestMenuTargetHours - shell.RestMenuProgressHourFraction);
            if (remainingHours <= 0f)
                return;

            float deltaHours = math.min(
                remainingHours,
                math.max(0f, SystemAPI.Time.DeltaTime) / RestTimelapseRealSecondsPerGameHour);
            if (deltaHours <= 0f)
                return;

            RestorePlayerVitalsForElapsedHours(ref systemState, sleeping, deltaHours);

            var timeRequests = SystemAPI.GetSingletonBuffer<MorrowindTimeAdvanceRequest>();
            timeRequests.Add(new MorrowindTimeAdvanceRequest
            {
                Hours = deltaHours,
                Kind = (byte)(sleeping ? MorrowindTimeAdvanceKind.Sleep : MorrowindTimeAdvanceKind.Rest),
            });

            shell.RestMenuProgressHourFraction = math.min(shell.RestMenuProgressHourFraction + deltaHours, shell.RestMenuTargetHours);
            shell.RestMenuProgressHours = math.min((int)math.floor(shell.RestMenuProgressHourFraction), shell.RestMenuTargetHours);
            shell.PlayerSleeping = sleeping ? (byte)1 : (byte)0;
        }

        int ComputeUntilHealedHours(ref SystemState systemState)
        {
            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            var attributes = _playerQuery.GetSingleton<ActorAttributeSet>();
            var vitals = _playerQuery.GetSingleton<ActorVitalSet>();
            var activeEffects = systemState.EntityManager.GetBuffer<ActorActiveMagicEffect>(_playerQuery.GetSingletonEntity(), true);
            bool stuntedMagicka = RuntimeRestUtility.HasStuntedMagicka(activeEffects);
            return RuntimeRestUtility.ComputeUntilHealedHours(ref contentBlob, vitals, attributes, stuntedMagicka);
        }

        bool IsPlayerDead()
        {
            var vitals = _playerQuery.GetSingleton<ActorVitalSet>();
            return vitals.CurrentHealth <= 0f;
        }

        void RestorePlayerVitalsForElapsedHours(ref SystemState systemState, bool sleeping, float gameHours)
        {
            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            var attributes = _playerQuery.GetSingleton<ActorAttributeSet>();
            var skills = _playerQuery.GetSingleton<ActorSkillSet>();
            ref var vitals = ref _playerQuery.GetSingletonRW<ActorVitalSet>().ValueRW;
            var effectModifiers = _playerQuery.GetSingleton<ActorEffectStatModifiers>();
            var derived = _playerQuery.GetSingleton<ActorDerivedMovementStats>();
            var movementSpeed = _playerQuery.GetSingleton<MorrowindMovementSpeed>();
            var activeEffects = systemState.EntityManager.GetBuffer<ActorActiveMagicEffect>(_playerQuery.GetSingletonEntity());
            var time = SystemAPI.GetSingleton<MorrowindTimeState>();

            MorrowindActorMovementStats.ApplyVitalBases(ref contentBlob, attributes, ref vitals, initializeMissingCurrents: false);
            var context = MorrowindPlayerSpeedResolver.Build(ref contentBlob, attributes, skills, vitals, effectModifiers, derived, movementSpeed);
            vitals.CurrentFatigue = math.min(vitals.ModifiedFatigueBase, vitals.CurrentFatigue + context.GetFatigueRestorePerSecond() * 3600f * gameHours);

            if (sleeping)
            {
                vitals.CurrentHealth = math.min(
                    vitals.ModifiedHealthBase,
                    vitals.CurrentHealth + RuntimeRestUtility.HealthPerSleepHour(attributes) * gameHours);

                float magickaRestoreHours = RuntimeRestUtility.MagickaRestoreHoursForSleep(gameHours, time.TimeScale, activeEffects);
                if (magickaRestoreHours > 0f)
                {
                    vitals.CurrentMagicka = math.min(
                        vitals.ModifiedMagickaBase,
                        vitals.CurrentMagicka + RuntimeRestUtility.MagickaPerSleepHour(ref contentBlob, attributes) * magickaRestoreHours);
                }
            }

            if (RuntimeRestUtility.AdvanceTimedActiveMagicEffects(activeEffects, gameHours, time.TimeScale))
                systemState.EntityManager.SetComponentEnabled<ActorActiveMagicEffectDirty>(_playerQuery.GetSingletonEntity(), true);

            vitals.CurrentHealth = math.clamp(vitals.CurrentHealth, 0f, math.max(0f, vitals.ModifiedHealthBase));
            vitals.CurrentMagicka = math.clamp(vitals.CurrentMagicka, 0f, math.max(0f, vitals.ModifiedMagickaBase));
            vitals.CurrentFatigue = math.clamp(vitals.CurrentFatigue, 0f, math.max(0f, vitals.ModifiedFatigueBase));
        }

        static void EnsureRestMenuOpen(in RuntimeShellState shell)
        {
            if (shell.RestMenuOpen == 0 || shell.RestMenuAdvancing != 0)
                throw new InvalidOperationException("[VVardenfell][Rest] Rest menu is not accepting input.");
        }

        static Unity.Collections.FixedString512Bytes BuildCannotRestNowText()
        {
            var result = default(Unity.Collections.FixedString512Bytes);
            result.Append('Y'); result.Append('o'); result.Append('u'); result.Append(' ');
            result.Append('c'); result.Append('a'); result.Append('n'); result.Append('n'); result.Append('o'); result.Append('t'); result.Append(' ');
            result.Append('r'); result.Append('e'); result.Append('s'); result.Append('t'); result.Append(' ');
            result.Append('n'); result.Append('o'); result.Append('w'); result.Append('.');
            return result;
        }
    }
}
