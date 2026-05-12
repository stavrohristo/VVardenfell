using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.AI
{
    public enum ActorAiRuntimePackageType : byte
    {
        Wander = 0,
        Travel = 1,
        Escort = 2,
        Follow = 3,
        Activate = 4,
        Pursue = 5,
        Combat = 6,
    }

    public enum ActorAiPlannerStatus : byte
    {
        Idle = 0,
        Waiting = 1,
        Traversing = 2,
        Complete = 3,
        Failed = 4,
    }

    public struct ActorAiState : IComponentData
    {
        public int CurrentPackageIndex;
        public int CurrentNodeIndex;
        public int GoalNodeIndex;
        public float3 HomePosition;
        public float WaitUntilTime;
        public float LastPackageActionTime;
        public float NextCombatEngagementTime;
        public uint RandomSeed;
        public byte Status;
        public byte FollowActive;
        public byte PendingIdleGroup;
        public ulong ActiveIdleGroupHash;
    }

    public struct ActorAiNavigationAnchor : IComponentData
    {
        public int PathGridIndex;
        public int GridX;
        public int GridY;
        public ulong InteriorCellHash;
        public byte IsResolved;
        public byte IsInterior;
    }

    public struct ActorAiPackageRuntime : IBufferElementData
    {
        public Entity FollowTargetEntity;
        public byte Type;
        public byte ShouldRepeat;
        public byte AllowPartial;
        public int SourcePackageIndex;
        public int TargetPathGridIndex;
        public float3 TargetPosition;
        public float WanderRadius;
        public float IdleSeconds;
        public float DurationHours;
        public float RemainingDurationHours;
        public float FollowDistance;
        public ulong DestinationInteriorCellHash;
        public uint FollowTargetPlacedRefId;
        public byte IdleChance0;
        public byte IdleChance1;
        public byte IdleChance2;
        public byte IdleChance3;
        public byte IdleChance4;
        public byte IdleChance5;
        public byte IdleChance6;
        public byte IdleChance7;
        public FixedString128Bytes TargetId;
    }

    public static class ActorAiRuntimeAuthoringUtility
    {
        public static bool HasPackage(ref RuntimeContentBlob content, ActorDefHandle actorHandle)
        {
            if (!actorHandle.IsValid)
                return false;

            ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref content, actorHandle);
            RuntimeContentBlobUtility.RequireRange(actor.FirstAiPackageIndex, actor.AiPackageCount, content.ActorAiPackages.Length, "actor AI package");
            return actor.AiPackageCount > 0;
        }

        public static void HydratePackages(
            ref RuntimeContentBlob content,
            ActorDefHandle actorHandle,
            in ActorAiNavigationAnchor anchor,
            DynamicBuffer<ActorAiPackageRuntime> target)
        {
            ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref content, actorHandle);
            RuntimeContentBlobUtility.RequireRange(actor.FirstAiPackageIndex, actor.AiPackageCount, content.ActorAiPackages.Length, "actor AI package");
            for (int i = 0; i < actor.AiPackageCount; i++)
            {
                ref RuntimeActorAiPackageDefBlob package = ref content.ActorAiPackages[actor.FirstAiPackageIndex + i];
                FixedString128Bytes cellName = VVardenfell.Runtime.RuntimeFixedStringUtility.ToFixed128OrDefault(ref package.CellName);
                FixedString128Bytes targetId = VVardenfell.Runtime.RuntimeFixedStringUtility.ToFixed128OrDefault(ref package.TargetId);
                if (package.Type == ActorAiPackageType.Travel)
                {
                    target.Add(new ActorAiPackageRuntime
                    {
                        Type = (byte)ActorAiRuntimePackageType.Travel,
                        ShouldRepeat = package.ShouldRepeat,
                        SourcePackageIndex = i,
                        TargetPathGridIndex = ResolvePackagePathGrid(ref content, cellName, anchor),
                        TargetPosition = ConvertMwPosition(package.X, package.Y, package.Z),
                        IdleSeconds = 0.5f,
                    });
                }
                else if (package.Type == ActorAiPackageType.Wander)
                {
                    target.Add(new ActorAiPackageRuntime
                    {
                        Type = (byte)ActorAiRuntimePackageType.Wander,
                        ShouldRepeat = package.ShouldRepeat,
                        SourcePackageIndex = i,
                        TargetPathGridIndex = -1,
                        WanderRadius = math.max(0f, package.WanderDistance) * WorldScale.MwUnitsToMeters,
                        DurationHours = math.max(0, package.Duration),
                        RemainingDurationHours = math.max(0, package.Duration),
                        IdleChance0 = package.Idle0,
                        IdleChance1 = package.Idle1,
                        IdleChance2 = package.Idle2,
                        IdleChance3 = package.Idle3,
                        IdleChance4 = package.Idle4,
                        IdleChance5 = package.Idle5,
                        IdleChance6 = package.Idle6,
                        IdleChance7 = package.Idle7,
                    });
                }
                else if (package.Type == ActorAiPackageType.Follow)
                {
                    target.Add(new ActorAiPackageRuntime
                    {
                        Type = (byte)ActorAiRuntimePackageType.Follow,
                        ShouldRepeat = package.ShouldRepeat,
                        AllowPartial = 1,
                        SourcePackageIndex = i,
                        TargetPathGridIndex = ResolvePackagePathGrid(ref content, cellName, anchor),
                        TargetPosition = ConvertMwPosition(package.X, package.Y, package.Z),
                        FollowDistance = 256f * WorldScale.MwUnitsToMeters,
                        IdleSeconds = 0.5f,
                        DurationHours = math.max(0, package.Duration),
                        RemainingDurationHours = math.max(0, package.Duration),
                        DestinationInteriorCellHash = ResolveInteriorCellHash(cellName),
                        TargetId = targetId,
                    });
                }
                else if (package.Type == ActorAiPackageType.Escort)
                {
                    target.Add(new ActorAiPackageRuntime
                    {
                        Type = (byte)ActorAiRuntimePackageType.Escort,
                        ShouldRepeat = package.ShouldRepeat,
                        AllowPartial = 1,
                        SourcePackageIndex = i,
                        TargetPathGridIndex = ResolvePackagePathGrid(ref content, cellName, anchor),
                        TargetPosition = ConvertMwPosition(package.X, package.Y, package.Z),
                        FollowDistance = 450f * WorldScale.MwUnitsToMeters,
                        IdleSeconds = 0.5f,
                        DurationHours = math.max(0, package.Duration),
                        RemainingDurationHours = math.max(0, package.Duration),
                        DestinationInteriorCellHash = ResolveInteriorCellHash(cellName),
                        TargetId = targetId,
                    });
                }
                else if (package.Type == ActorAiPackageType.Activate)
                {
                    target.Add(new ActorAiPackageRuntime
                    {
                        Type = (byte)ActorAiRuntimePackageType.Activate,
                        ShouldRepeat = package.ShouldRepeat,
                        AllowPartial = 1,
                        SourcePackageIndex = i,
                        TargetPathGridIndex = anchor.IsResolved != 0 ? anchor.PathGridIndex : -1,
                        FollowDistance = 128f * WorldScale.MwUnitsToMeters,
                        IdleSeconds = 0.25f,
                        TargetId = targetId,
                    });
                }
            }
        }

        static int ResolvePackagePathGrid(ref RuntimeContentBlob content, FixedString128Bytes cellName, in ActorAiNavigationAnchor anchor)
        {
            if (!cellName.IsEmpty &&
                RuntimeContentBlobUtility.TryGetInteriorPathGridHandleByCellHash(ref content, ResolveInteriorCellHash(cellName), out var handle) &&
                handle.IsValid)
            {
                return handle.Index;
            }

            return anchor.IsResolved != 0 ? anchor.PathGridIndex : -1;
        }

        static float3 ConvertMwPosition(float x, float y, float z)
            => new float3(x, z, y) * WorldScale.MwUnitsToMeters;

        static ulong ResolveInteriorCellHash(FixedString128Bytes cellName)
        {
            if (cellName.IsEmpty)
                return 0UL;

            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < cellName.Length; i++)
            {
                byte c = cellName[i];
                if (c >= (byte)'A' && c <= (byte)'Z')
                    c = (byte)(c + 32);
                hash ^= c;
                hash *= 1099511628211UL;
            }

            return hash == 0UL ? 1UL : hash;
        }
    }
}
