using System;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.Player
{
    [UpdateInGroup(typeof(MorrowindGameplayMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial struct ActorAttributeMutationApplySystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<MorrowindScriptRuntimeState>();
            state.RequireForUpdate<ActorAttributeMutationRequest>();
            state.RequireForUpdate<LogicalRefLookup>();
        }

        public void OnUpdate(ref SystemState state)
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = state.EntityManager.GetBuffer<ActorAttributeMutationRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            var lookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            for (int i = 0; i < requests.Length; i++)
                ApplyRequest(state.EntityManager, requests[i], lookup);

            requests.Clear();
        }

        static void ApplyRequest(EntityManager entityManager, in ActorAttributeMutationRequest request, in LogicalRefLookup lookup)
        {
            Entity target = MorrowindRuntimeTargetResolver.ResolveLiveTarget(entityManager, request.TargetEntity, request.TargetPlacedRefId, lookup);
            if (target == Entity.Null || !entityManager.Exists(target))
                throw new InvalidOperationException($"[VVardenfell][Player] Actor attribute mutation target ref={request.TargetPlacedRefId} is not loaded.");

            if (!entityManager.HasComponent<ActorAttributeSet>(target))
                throw new InvalidOperationException($"[VVardenfell][Player] Actor attribute mutation target ref={request.TargetPlacedRefId} has no ActorAttributeSet.");

            if ((ActorAttributeKind)request.Attribute == ActorAttributeKind.None)
                throw new InvalidOperationException("[VVardenfell][Player] Actor attribute mutation requires a concrete attribute.");

            var attributes = entityManager.GetComponentData<ActorAttributeSet>(target);
            float current = GetAttribute(attributes, (ActorAttributeKind)request.Attribute);
            float value = (ActorAttributeMutationKind)request.Kind switch
            {
                ActorAttributeMutationKind.Set => request.Value,
                ActorAttributeMutationKind.Mod => current + request.Value,
                _ => throw new InvalidOperationException($"[VVardenfell][Player] Unknown actor attribute mutation kind {request.Kind}."),
            };
            SetAttribute(ref attributes, (ActorAttributeKind)request.Attribute, value);
            entityManager.SetComponentData(target, attributes);
            if ((ActorAttributeKind)request.Attribute == ActorAttributeKind.Strength)
                PlayerEncumbranceDirtyUtility.MarkIfPlayer(entityManager, target);
        }

        static float GetAttribute(in ActorAttributeSet attributes, ActorAttributeKind attribute)
            => attribute switch
            {
                ActorAttributeKind.Strength => attributes.Strength,
                ActorAttributeKind.Intelligence => attributes.Intelligence,
                ActorAttributeKind.Willpower => attributes.Willpower,
                ActorAttributeKind.Agility => attributes.Agility,
                ActorAttributeKind.Speed => attributes.Speed,
                ActorAttributeKind.Endurance => attributes.Endurance,
                ActorAttributeKind.Personality => attributes.Personality,
                ActorAttributeKind.Luck => attributes.Luck,
                _ => throw new InvalidOperationException($"[VVardenfell][Player] Unknown actor attribute kind {(byte)attribute}."),
            };

        static void SetAttribute(ref ActorAttributeSet attributes, ActorAttributeKind attribute, float value)
        {
            switch (attribute)
            {
                case ActorAttributeKind.Strength: attributes.Strength = value; break;
                case ActorAttributeKind.Intelligence: attributes.Intelligence = value; break;
                case ActorAttributeKind.Willpower: attributes.Willpower = value; break;
                case ActorAttributeKind.Agility: attributes.Agility = value; break;
                case ActorAttributeKind.Speed: attributes.Speed = value; break;
                case ActorAttributeKind.Endurance: attributes.Endurance = value; break;
                case ActorAttributeKind.Personality: attributes.Personality = value; break;
                case ActorAttributeKind.Luck: attributes.Luck = value; break;
                default: throw new InvalidOperationException($"[VVardenfell][Player] Unknown actor attribute kind {(byte)attribute}.");
            }
        }
    }
}
