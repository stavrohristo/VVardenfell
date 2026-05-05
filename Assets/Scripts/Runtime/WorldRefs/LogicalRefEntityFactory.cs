using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Streaming;

namespace VVardenfell.Runtime.WorldRefs
{
    internal struct LogicalRefEntityDescriptor
    {
        public ContentReference ContentReference;
        public uint PlacedRefId;
        public float3 Position;
        public quaternion Rotation;
        public float Scale;
        public bool IsInterior;
        public int2 ExteriorCell;
        public FixedString128Bytes InteriorCellId;
        public bool AttachDoorInteractable;
        public DoorInteractable DoorInteractable;
        public FixedString64Bytes CapturedSoulId;
        public int CapturedSoulActorHandleValue;
        public bool AddRuntimeSpawnIdentity;
        public byte RuntimeSpawnPersistencePolicy;
        public PlacedRefLockState LockState;
        public bool HasLockState;
    }

    internal static class LogicalRefEntityFactory
    {
        public static Entity QueueCreate(
            EntityManager entityManager,
            ref EntityCommandBuffer ecb,
            ref RuntimeContentBlob content,
            in LogicalRefEntityDescriptor descriptor)
        {
            Entity logicalEntity = ecb.CreateEntity();
            ecb.SetName(logicalEntity, new FixedString64Bytes($"LogicalRef({descriptor.PlacedRefId:X8})"));
            ecb.AddComponent(logicalEntity, new LogicalRefTag());
            ecb.AddComponent(logicalEntity, new PlacedRefIdentity { Value = descriptor.PlacedRefId });
            ecb.AddComponent(logicalEntity, new LogicalRefContent { Value = descriptor.ContentReference });
            ecb.AddComponent(logicalEntity, new PlacedRefRuntimeState());
            ecb.AddComponent(logicalEntity, new PlacedRefInitialTransform
            {
                Position = descriptor.Position,
                Rotation = descriptor.Rotation,
                Scale = descriptor.Scale,
            });
            if (descriptor.HasLockState)
                ecb.AddComponent(logicalEntity, descriptor.LockState);
            if (!descriptor.CapturedSoulId.IsEmpty && descriptor.CapturedSoulActorHandleValue > 0)
            {
                ecb.AddComponent(logicalEntity, new PlacedRefCapturedSoul
                {
                    SoulId = descriptor.CapturedSoulId,
                    SoulActorHandleValue = descriptor.CapturedSoulActorHandleValue,
                });
            }

            if (descriptor.AddRuntimeSpawnIdentity)
            {
                ecb.AddComponent(logicalEntity, new RuntimeSpawnedRefIdentity
                {
                    RuntimeRefId = descriptor.PlacedRefId,
                    PersistencePolicy = descriptor.RuntimeSpawnPersistencePolicy,
                });
            }

            ecb.AddComponent(logicalEntity, new LogicalRefLocation
            {
                ExteriorCell = descriptor.ExteriorCell,
                InteriorCellId = descriptor.InteriorCellId,
                InteriorCellHash = descriptor.IsInterior ? InteriorCellIdHash.Hash(descriptor.InteriorCellId) : 0UL,
                IsInterior = (byte)(descriptor.IsInterior ? 1 : 0),
            });
            ecb.AddBuffer<LogicalRefChild>(logicalEntity);
            ecb.AddComponent(logicalEntity, LocalTransform.FromPositionRotationScale(
                descriptor.Position,
                descriptor.Rotation,
                descriptor.Scale));
            ecb.AddComponent(logicalEntity, new LocalToWorld
            {
                Value = float4x4.TRS(
                    descriptor.Position,
                    descriptor.Rotation,
                    new float3(descriptor.Scale)),
            });

            if (descriptor.IsInterior)
                ecb.AddComponent<InteriorCellMember>(logicalEntity);
            else
                ecb.AddComponent(logicalEntity, new CellLink { Value = descriptor.ExteriorCell });

            LogicalRefAuthoringUtility.QueueAttach(
                entityManager,
                ref ecb,
                logicalEntity,
                ref content,
                descriptor.ContentReference,
                descriptor.Position,
                descriptor.IsInterior,
                descriptor.ExteriorCell,
                descriptor.InteriorCellId,
                descriptor.PlacedRefId,
                descriptor.AttachDoorInteractable,
                descriptor.DoorInteractable);
            return logicalEntity;
        }

        public static bool QueueEnsureInteractionProxyQueued(EntityManager entityManager, ref EntityCommandBuffer ecb, Entity logicalEntity, bool assumeNewEntity = false)
        {
            return false;
        }
    }
}
