using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
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
        public bool AddRuntimeSpawnIdentity;
        public byte RuntimeSpawnPersistencePolicy;
    }

    internal static class LogicalRefEntityFactory
    {
        public static Entity Create(
            EntityManager entityManager,
            RuntimeContentDatabase contentDb,
            in LogicalRefEntityDescriptor descriptor)
        {
            Entity logicalEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(logicalEntity, new LogicalRefTag());
            entityManager.AddComponentData(logicalEntity, new PlacedRefIdentity { Value = descriptor.PlacedRefId });
            if (descriptor.AddRuntimeSpawnIdentity)
            {
                entityManager.AddComponentData(logicalEntity, new RuntimeSpawnedRefIdentity
                {
                    RuntimeRefId = descriptor.PlacedRefId,
                    PersistencePolicy = descriptor.RuntimeSpawnPersistencePolicy,
                });
            }

            entityManager.AddComponentData(logicalEntity, new LogicalRefContentRef { Value = descriptor.ContentReference });
            entityManager.AddComponentData(logicalEntity, new LogicalRefLocation
            {
                ExteriorCell = descriptor.ExteriorCell,
                InteriorCellId = descriptor.InteriorCellId,
                IsInterior = (byte)(descriptor.IsInterior ? 1 : 0),
            });
            entityManager.AddBuffer<LogicalRefChild>(logicalEntity);
            entityManager.AddComponentData(logicalEntity, LocalTransform.FromPositionRotationScale(
                descriptor.Position,
                descriptor.Rotation,
                descriptor.Scale));
            entityManager.AddComponentData(logicalEntity, new LocalToWorld
            {
                Value = float4x4.TRS(
                    descriptor.Position,
                    descriptor.Rotation,
                    new float3(descriptor.Scale)),
            });

            if (descriptor.IsInterior)
                entityManager.AddComponent<InteriorCellMember>(logicalEntity);
            else
                entityManager.AddComponentData(logicalEntity, new CellLink { Value = descriptor.ExteriorCell });

            LogicalRefAuthoringUtility.TryAttach(
                entityManager,
                logicalEntity,
                contentDb,
                descriptor.ContentReference,
                descriptor.AttachDoorInteractable,
                descriptor.DoorInteractable);
            return logicalEntity;
        }

        public static bool EnsureInteractionProxyQueued(EntityManager entityManager, Entity logicalEntity)
        {
            if (entityManager.HasComponent<InteractionActivationProxyBuildPending>(logicalEntity))
                return false;

            return InteractionActivationProxyBuildUtility.EnsureQueued(entityManager, logicalEntity);
        }
    }
}
