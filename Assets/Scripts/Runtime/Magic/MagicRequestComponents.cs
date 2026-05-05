using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Components
{
    public struct ActorSpellMutationRequest : IBufferElementData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public SpellDefHandle Spell;
        public byte Remove;
    }

    public struct ScriptedCastRequest : IBufferElementData
    {
        public Entity CasterEntity;
        public uint CasterPlacedRefId;
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public SpellDefHandle Spell;
    }

    public struct ActorSpellCastRequest : IBufferElementData
    {
        public Entity CasterEntity;
        public uint CasterPlacedRefId;
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public SpellDefHandle Spell;
        public byte Scripted;
        public byte AlwaysSucceed;
        public byte IgnoreReflect;
        public byte IgnoreSpellAbsorption;
        public byte ProjectileImpact;
        public byte HasHitPosition;
        public float3 HitPosition;
    }

    public struct MorrowindMagicRuntimeState : IComponentData
    {
        public uint RandomState;
        public int NextActiveSpellId;
    }
}
