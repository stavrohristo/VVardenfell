using System;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Player
{
    [UpdateInGroup(typeof(MorrowindGameplayMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial struct PlayerSkillMutationApplySystem : ISystem
    {
        EntityQuery _playerQuery;

        public void OnCreate(ref SystemState state)
        {
            _playerQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadWrite<ActorSkillSet>());
            state.RequireForUpdate<MorrowindScriptRuntimeState>();
            state.RequireForUpdate<PlayerSkillMutationRequest>();
            state.RequireForUpdate(_playerQuery);
        }

        public void OnUpdate(ref SystemState state)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = state.EntityManager.GetBuffer<PlayerSkillMutationRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            Entity player = _playerQuery.GetSingletonEntity();
            var skills = state.EntityManager.GetComponentData<ActorSkillSet>(player);
            for (int i = 0; i < requests.Length; i++)
                ApplyRequest(ref skills, requests[i]);
            state.EntityManager.SetComponentData(player, skills);

            requests.Clear();
        }

        static void ApplyRequest(ref ActorSkillSet skills, in PlayerSkillMutationRequest request)
        {
            if ((ActorSkillKind)request.Skill == ActorSkillKind.None)
                throw new InvalidOperationException("[VVardenfell][Player] Player skill mutation requires a concrete skill.");

            float current = GetSkill(skills, (ActorSkillKind)request.Skill);
            float value = (PlayerSkillMutationKind)request.Kind switch
            {
                PlayerSkillMutationKind.Set => request.Value,
                PlayerSkillMutationKind.Mod => current + request.Value,
                _ => throw new InvalidOperationException($"[VVardenfell][Player] Unknown player skill mutation kind {request.Kind}."),
            };
            SetSkill(ref skills, (ActorSkillKind)request.Skill, value);
        }

        public static float GetSkill(in ActorSkillSet skills, ActorSkillKind skill)
            => skill switch
            {
                ActorSkillKind.Block => skills.Block,
                ActorSkillKind.Armorer => skills.Armorer,
                ActorSkillKind.MediumArmor => skills.MediumArmor,
                ActorSkillKind.HeavyArmor => skills.HeavyArmor,
                ActorSkillKind.BluntWeapon => skills.BluntWeapon,
                ActorSkillKind.LongBlade => skills.LongBlade,
                ActorSkillKind.Axe => skills.Axe,
                ActorSkillKind.Spear => skills.Spear,
                ActorSkillKind.Athletics => skills.Athletics,
                ActorSkillKind.Enchant => skills.Enchant,
                ActorSkillKind.Destruction => skills.Destruction,
                ActorSkillKind.Alteration => skills.Alteration,
                ActorSkillKind.Illusion => skills.Illusion,
                ActorSkillKind.Conjuration => skills.Conjuration,
                ActorSkillKind.Mysticism => skills.Mysticism,
                ActorSkillKind.Restoration => skills.Restoration,
                ActorSkillKind.Alchemy => skills.Alchemy,
                ActorSkillKind.Unarmored => skills.Unarmored,
                ActorSkillKind.Security => skills.Security,
                ActorSkillKind.Sneak => skills.Sneak,
                ActorSkillKind.Acrobatics => skills.Acrobatics,
                ActorSkillKind.LightArmor => skills.LightArmor,
                ActorSkillKind.ShortBlade => skills.ShortBlade,
                ActorSkillKind.Marksman => skills.Marksman,
                ActorSkillKind.Mercantile => skills.Mercantile,
                ActorSkillKind.Speechcraft => skills.Speechcraft,
                ActorSkillKind.HandToHand => skills.HandToHand,
                _ => throw new InvalidOperationException($"[VVardenfell][Player] Unknown actor skill kind {(byte)skill}."),
            };

        static void SetSkill(ref ActorSkillSet skills, ActorSkillKind skill, float value)
        {
            switch (skill)
            {
                case ActorSkillKind.Block: skills.Block = value; break;
                case ActorSkillKind.Armorer: skills.Armorer = value; break;
                case ActorSkillKind.MediumArmor: skills.MediumArmor = value; break;
                case ActorSkillKind.HeavyArmor: skills.HeavyArmor = value; break;
                case ActorSkillKind.BluntWeapon: skills.BluntWeapon = value; break;
                case ActorSkillKind.LongBlade: skills.LongBlade = value; break;
                case ActorSkillKind.Axe: skills.Axe = value; break;
                case ActorSkillKind.Spear: skills.Spear = value; break;
                case ActorSkillKind.Athletics: skills.Athletics = value; break;
                case ActorSkillKind.Enchant: skills.Enchant = value; break;
                case ActorSkillKind.Destruction: skills.Destruction = value; break;
                case ActorSkillKind.Alteration: skills.Alteration = value; break;
                case ActorSkillKind.Illusion: skills.Illusion = value; break;
                case ActorSkillKind.Conjuration: skills.Conjuration = value; break;
                case ActorSkillKind.Mysticism: skills.Mysticism = value; break;
                case ActorSkillKind.Restoration: skills.Restoration = value; break;
                case ActorSkillKind.Alchemy: skills.Alchemy = value; break;
                case ActorSkillKind.Unarmored: skills.Unarmored = value; break;
                case ActorSkillKind.Security: skills.Security = value; break;
                case ActorSkillKind.Sneak: skills.Sneak = value; break;
                case ActorSkillKind.Acrobatics: skills.Acrobatics = value; break;
                case ActorSkillKind.LightArmor: skills.LightArmor = value; break;
                case ActorSkillKind.ShortBlade: skills.ShortBlade = value; break;
                case ActorSkillKind.Marksman: skills.Marksman = value; break;
                case ActorSkillKind.Mercantile: skills.Mercantile = value; break;
                case ActorSkillKind.Speechcraft: skills.Speechcraft = value; break;
                case ActorSkillKind.HandToHand: skills.HandToHand = value; break;
                default: throw new InvalidOperationException($"[VVardenfell][Player] Unknown actor skill kind {(byte)skill}.");
            }
        }
    }
}
