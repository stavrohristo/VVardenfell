#if !VVARDENFELL_OLD_ACTOR_ANIMATION
using Unity.Entities;
using UnityEngine;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Rendering;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindPresentationBuildSystemGroup))]
    [UpdateAfter(typeof(ActorPresentationSpawnSystem))]
    [UpdateBefore(typeof(ActorRigidEquipmentVisibilitySystem))]
    public partial class ActorRigidEquipmentRenderOwnerSystem : SystemBase
    {
        static int s_RigidEquipmentLogCount;

        protected override void OnCreate()
        {
            RequireForUpdate<ActorRigidEquipmentAttachment>();
        }

        protected override void OnUpdate()
        {
            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            int attachmentCount = 0;
            int renderLeafCount = 0;
            int ownedLeafCount = 0;
            int addedOwnerCount = 0;
            foreach (var (attachment, entity) in
                     SystemAPI.Query<RefRO<ActorRigidEquipmentAttachment>>()
                         .WithEntityAccess())
            {
                attachmentCount++;
                Entity renderOwner = ResolveRenderOwner(attachment.ValueRO);
                AssignOwner(ref ecb, entity, renderOwner, ref renderLeafCount, ref ownedLeafCount, ref addedOwnerCount);
                if (!EntityManager.HasBuffer<LinkedEntityGroup>(entity))
                    continue;

                var linked = EntityManager.GetBuffer<LinkedEntityGroup>(entity);
                for (int i = 0; i < linked.Length; i++)
                {
                    Entity child = linked[i].Value;
                    if (child != entity)
                        AssignOwner(ref ecb, child, renderOwner, ref renderLeafCount, ref ownedLeafCount, ref addedOwnerCount);
                }
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
            if (attachmentCount > 0 && s_RigidEquipmentLogCount < 16)
            {
                s_RigidEquipmentLogCount++;
                Debug.Log(
                    $"[VVardenfell][EquipmentDiag] rigidEquipment attachments={attachmentCount} " +
                    $"renderLeaves={renderLeafCount} ownedLeaves={ownedLeafCount} addedOwners={addedOwnerCount}");
            }
        }

        Entity ResolveRenderOwner(in ActorRigidEquipmentAttachment attachment)
        {
            if (attachment.Actor == Entity.Null || !EntityManager.HasComponent<ActorWeaponAnimationState>(attachment.Actor))
                return Entity.Null;

            var weaponState = EntityManager.GetComponentData<ActorWeaponAnimationState>(attachment.Actor);
            return weaponState.Drawn != 0 || weaponState.Phase == ActorWeaponAnimationPhase.Equipping
                ? attachment.Actor
                : Entity.Null;
        }

        void AssignOwner(
            ref EntityCommandBuffer ecb,
            Entity entity,
            Entity actor,
            ref int renderLeafCount,
            ref int ownedLeafCount,
            ref int addedOwnerCount)
        {
            if (entity == Entity.Null || !EntityManager.Exists(entity))
                return;

            if (EntityManager.HasComponent<ModelPrefabRenderLeaf>(entity))
                renderLeafCount++;

            if (EntityManager.HasComponent<ActorRenderMeshInstance>(entity))
            {
                var instance = EntityManager.GetComponentData<ActorRenderMeshInstance>(entity);
                if (actor != Entity.Null)
                    ownedLeafCount++;
                if (instance.Actor == actor)
                    return;

                instance.Actor = actor;
                ecb.SetComponent(entity, instance);
                return;
            }

            if (!EntityManager.HasComponent<ModelPrefabRenderLeaf>(entity))
                return;

            ecb.AddComponent(entity, new ActorRenderMeshInstance
            {
                Actor = actor,
                SkinMeshIndex = -1,
            });
            addedOwnerCount++;
            if (actor != Entity.Null)
                ownedLeafCount++;
        }
    }
}
#endif
