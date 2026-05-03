using System;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.Player
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial class ActorAttributeMutationApplySystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindScriptRuntimeState>();
            RequireForUpdate<ActorAttributeMutationRequest>();
            RequireForUpdate<LogicalRefLookup>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = EntityManager.GetBuffer<ActorAttributeMutationRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            var lookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            for (int i = 0; i < requests.Length; i++)
                ApplyRequest(requests[i], lookup);

            requests.Clear();
        }

        void ApplyRequest(in ActorAttributeMutationRequest request, in LogicalRefLookup lookup)
        {
            Entity target = MorrowindRuntimeTargetResolver.ResolveLiveTarget(EntityManager, request.TargetEntity, request.TargetPlacedRefId, lookup);
            if (target == Entity.Null || !EntityManager.Exists(target))
                throw new InvalidOperationException($"[VVardenfell][Player] Actor attribute mutation target ref={request.TargetPlacedRefId} is not loaded.");

            if (!EntityManager.HasComponent<ActorAttributeSet>(target))
                throw new InvalidOperationException($"[VVardenfell][Player] Actor attribute mutation target ref={request.TargetPlacedRefId} has no ActorAttributeSet.");

            if ((ActorAttributeKind)request.Attribute == ActorAttributeKind.None)
                throw new InvalidOperationException("[VVardenfell][Player] Actor attribute mutation requires a concrete attribute.");

            var attributes = EntityManager.GetComponentData<ActorAttributeSet>(target);
            float current = GetAttribute(attributes, (ActorAttributeKind)request.Attribute);
            float value = (ActorAttributeMutationKind)request.Kind switch
            {
                ActorAttributeMutationKind.Set => request.Value,
                ActorAttributeMutationKind.Mod => current + request.Value,
                _ => throw new InvalidOperationException($"[VVardenfell][Player] Unknown actor attribute mutation kind {request.Kind}."),
            };
            SetAttribute(ref attributes, (ActorAttributeKind)request.Attribute, value);
            EntityManager.SetComponentData(target, attributes);
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
