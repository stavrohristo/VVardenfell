using Unity.Entities;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.WorldRefs
{
    internal static class LogicalRefChildUtility
    {
        public static Entity[][] SnapshotLogicalChildGroups(EntityManager entityManager, Entity[] rootChildren)
        {
            var snapshots = new Entity[rootChildren.Length][];
            for (int i = 0; i < rootChildren.Length; i++)
            {
                Entity rootChild = rootChildren[i];
                if (rootChild == Entity.Null || !entityManager.Exists(rootChild))
                    continue;

                snapshots[i] = SnapshotLinkedEntityGroup(entityManager, rootChild) ?? new[] { rootChild };
            }

            return snapshots;
        }

        public static Entity[] SnapshotLinkedEntityGroup(EntityManager entityManager, Entity root)
        {
            if (root == Entity.Null || !entityManager.Exists(root))
                return null;

            if (!entityManager.HasBuffer<LinkedEntityGroup>(root))
                return null;

            var linked = entityManager.GetBuffer<LinkedEntityGroup>(root);
            var linkedEntities = new Entity[linked.Length];
            for (int i = 0; i < linked.Length; i++)
                linkedEntities[i] = linked[i].Value;

            return linkedEntities;
        }

        public static void QueueAppendChildren(EntityManager entityManager, ref EntityCommandBuffer ecb, Entity logicalEntity, Entity[] children)
        {
            if (children == null)
                return;

            for (int i = 0; i < children.Length; i++)
                QueueLinkChild(entityManager, ref ecb, logicalEntity, children[i]);
        }

        public static void QueueAppendLinkedEntityGroup(EntityManager entityManager, ref EntityCommandBuffer ecb, Entity logicalEntity, Entity root)
        {
            Entity[] linkedEntities = SnapshotLinkedEntityGroup(entityManager, root);
            if (linkedEntities == null)
            {
                QueueLinkChild(entityManager, ref ecb, logicalEntity, root);
                return;
            }

            QueueAppendChildren(entityManager, ref ecb, logicalEntity, linkedEntities);
        }

        public static void QueueLinkChild(EntityManager entityManager, ref EntityCommandBuffer ecb, Entity logicalEntity, Entity child)
        {
            if (child == Entity.Null || !entityManager.Exists(child))
                return;

            if (entityManager.HasComponent<LogicalRefParent>(child))
                ecb.SetComponent(child, new LogicalRefParent { Value = logicalEntity });
            else
                ecb.AddComponent(child, new LogicalRefParent { Value = logicalEntity });

            ecb.AppendToBuffer(logicalEntity, new LogicalRefChild { Value = child });
        }

        public static void QueueLinkNewChild(ref EntityCommandBuffer ecb, Entity logicalEntity, Entity child)
        {
            if (child == Entity.Null)
                return;

            ecb.AddComponent(child, new LogicalRefParent { Value = logicalEntity });
            ecb.AppendToBuffer(logicalEntity, new LogicalRefChild { Value = child });
        }

        public static Entity[] SnapshotChildBuffer(EntityManager entityManager, Entity logicalEntity)
        {
            if (!entityManager.HasBuffer<LogicalRefChild>(logicalEntity))
                return System.Array.Empty<Entity>();

            var children = entityManager.GetBuffer<LogicalRefChild>(logicalEntity);
            if (children.Length == 0)
                return System.Array.Empty<Entity>();

            var snapshot = new Entity[children.Length];
            for (int i = 0; i < children.Length; i++)
                snapshot[i] = children[i].Value;

            return snapshot;
        }
    }
}
