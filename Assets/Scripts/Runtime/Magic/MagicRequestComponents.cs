using Unity.Entities;
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
}
