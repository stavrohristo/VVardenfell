using Unity.Entities;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Interactions
{
    readonly struct ResolvedInteractionTarget
    {
        public readonly Entity TargetEntity;
        public readonly uint PlacedRefId;
        public readonly InteractableKind Kind;
        public readonly float HitDistance;

        public ResolvedInteractionTarget(Entity targetEntity, uint placedRefId, InteractableKind kind, float hitDistance)
        {
            TargetEntity = targetEntity;
            PlacedRefId = placedRefId;
            Kind = kind;
            HitDistance = hitDistance;
        }
    }
}
