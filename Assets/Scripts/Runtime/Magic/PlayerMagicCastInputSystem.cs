using System;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Magic
{
    [UpdateInGroup(typeof(MorrowindGameplayInputSystemGroup))]
    [UpdateAfter(typeof(PlayerInputReceivingSystem))]
    public partial class PlayerMagicCastInputSystem : SystemBase
    {
        EntityQuery _playerQuery;

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadWrite<PlayerCharacterControl>(),
                ComponentType.ReadOnly<ActorKnownSpell>());

            RequireForUpdate(_playerQuery);
            RequireForUpdate<MorrowindScriptRuntimeState>();
            RequireForUpdate<SpellWindowState>();
        }

        protected override void OnUpdate()
        {
            Entity player = _playerQuery.GetSingletonEntity();
            var controlRef = _playerQuery.GetSingletonRW<PlayerCharacterControl>();
            ref var control = ref controlRef.ValueRW;
            if (!control.CastMagicPressed)
                return;

            control.CastMagicPressed = false;
            var knownSpells = EntityManager.GetBuffer<ActorKnownSpell>(player, true);
            if (knownSpells.Length == 0)
                throw new InvalidOperationException("[VVardenfell][Magic] Player attempted to cast with an empty spellbook.");

            var spellState = SystemAPI.GetSingleton<SpellWindowState>();
            int selectedIndex = spellState.SelectedSpellIndex;
            if (selectedIndex < 0 || selectedIndex >= knownSpells.Length)
                throw new InvalidOperationException($"[VVardenfell][Magic] Selected spell index {selectedIndex} is outside the spellbook length {knownSpells.Length}.");

            Entity target = Entity.Null;
            uint targetPlacedRefId = 0u;
            if (SystemAPI.TryGetSingleton<PlayerInteractionFocus>(out var focus) && focus.HasTarget != 0)
            {
                target = focus.TargetEntity;
                targetPlacedRefId = focus.PlacedRefId;
            }

            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = EntityManager.GetBuffer<ActorSpellCastRequest>(runtimeEntity);
            requests.Add(new ActorSpellCastRequest
            {
                CasterEntity = player,
                CasterPlacedRefId = 0u,
                TargetEntity = target,
                TargetPlacedRefId = targetPlacedRefId,
                Spell = knownSpells[selectedIndex].Spell,
            });
        }
    }
}
