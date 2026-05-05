using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Projectiles
{
    public enum MorrowindProjectileSourceKind : byte
    {
        None = 0,
        Magic = 1,
        Arrow = 2,
        Bolt = 3,
        ThrownWeapon = 4,
    }

    public enum MorrowindProjectileHitKind : byte
    {
        None = 0,
        Actor = 1,
        Object = 2,
        Geometry = 3,
        Projectile = 4,
    }

    public struct MorrowindProjectileLaunchRequest : IComponentData
    {
        public Entity Caster;
        public Entity Target;
        public ContentReference SourceContent;
        public MorrowindProjectileSourceKind SourceKind;
        public int SpellHandleValue;
        public short EffectId;
        public float3 Position;
        public quaternion Rotation;
        public float3 Direction;
        public float Speed;
        public float AttackStrength;
        public float CollisionRadius;
        public int ModelPrefabIndex;
        public ulong ModelPathHash;
        public ulong TextureOverridePathHash;
        public byte SpawnVisual;
        public byte Scripted;
        public byte IgnoreReflect;
        public byte IgnoreSpellAbsorption;
        public int2 ExteriorCell;
        public FixedString128Bytes InteriorCellId;
        public ulong InteriorCellHash;
        public byte IsInterior;
    }

    public struct MorrowindProjectile : IComponentData
    {
        public Entity Caster;
        public Entity Target;
        public ContentReference SourceContent;
        public MorrowindProjectileSourceKind SourceKind;
        public int SpellHandleValue;
        public short EffectId;
        public float3 Velocity;
        public float AttackStrength;
        public float Radius;
        public int ModelPrefabIndex;
        public ulong ModelPathHash;
        public ulong TextureOverridePathHash;
        public int2 ExteriorCell;
        public FixedString128Bytes InteriorCellId;
        public ulong InteriorCellHash;
        public byte IsInterior;
        public byte Scripted;
        public byte IgnoreReflect;
        public byte IgnoreSpellAbsorption;
        public uint PendingQuerySequence;
        public uint PendingQueryFixedTick;
    }

    public struct MorrowindProjectileHitEvent : IComponentData
    {
        public Entity Projectile;
        public Entity Caster;
        public Entity Target;
        public ContentReference SourceContent;
        public MorrowindProjectileSourceKind SourceKind;
        public MorrowindProjectileHitKind HitKind;
        public int SpellHandleValue;
        public short EffectId;
        public float AttackStrength;
        public float3 HitPosition;
        public float3 HitNormal;
        public int ModelPrefabIndex;
        public ulong ModelPathHash;
        public ulong TextureOverridePathHash;
        public byte Scripted;
        public byte IgnoreReflect;
        public byte IgnoreSpellAbsorption;
    }
}
