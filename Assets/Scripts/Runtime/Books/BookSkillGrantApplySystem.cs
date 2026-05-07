using System;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Books
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(BookReadRequestSystem))]
    public partial struct BookSkillGrantApplySystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<BookSkillGrantRequest>();
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<PlayerSkillMutationRequest>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            ref var request = ref SystemAPI.GetSingletonRW<BookSkillGrantRequest>().ValueRW;
            if (request.Pending == 0)
                return;

            int skillId = request.SkillId;
            request = default;

            if (skillId < 0 || skillId >= 27)
                throw new InvalidOperationException($"[VVardenfell][Books] Skill book requested invalid skill index {skillId}.");

            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var mutations = systemState.EntityManager.GetBuffer<PlayerSkillMutationRequest>(runtimeEntity);
            mutations.Add(new PlayerSkillMutationRequest
            {
                Kind = (byte)PlayerSkillMutationKind.Mod,
                Skill = (byte)(skillId + 1),
                Value = 1f,
            });
        }
    }
}
