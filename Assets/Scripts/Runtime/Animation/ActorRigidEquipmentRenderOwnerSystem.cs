#if !VVARDENFELL_OLD_ACTOR_ANIMATION
using Unity.Entities;
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
        EntityQuery _dirtyQuery;

        protected override void OnCreate()
        {
            _dirtyQuery = GetEntityQuery(
                ComponentType.ReadOnly<ActorRigidEquipmentAttachment>(),
                ComponentType.ReadOnly<ActorRigidEquipmentRenderOwnerDirty>());
            RequireForUpdate(_dirtyQuery);
        }

        protected override void OnUpdate()
        {

            
            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            foreach (var (attachment, entity) in
                     SystemAPI.Query<RefRO<ActorRigidEquipmentAttachment>>()
                         .WithAll<ActorRigidEquipmentRenderOwnerDirty>()
                         .WithEntityAccess())
            {
                Entity renderOwner = ResolveRenderOwner(attachment.ValueRO);
                AssignOwner(ref ecb, entity, renderOwner);
                if (!EntityManager.HasBuffer<LinkedEntityGroup>(entity))
                {
                    ecb.SetComponentEnabled<ActorRigidEquipmentRenderOwnerDirty>(entity, false);
                    continue;
                }

                var linked = EntityManager.GetBuffer<LinkedEntityGroup>(entity);
                for (int i = 0; i < linked.Length; i++)
                {
                    Entity child = linked[i].Value;
                    if (child != entity)
                        AssignOwner(ref ecb, child, renderOwner);
                }

                ecb.SetComponentEnabled<ActorRigidEquipmentRenderOwnerDirty>(entity, false);
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
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
            Entity actor)
        {
            if (entity == Entity.Null || !EntityManager.Exists(entity))
                return;

            if (EntityManager.HasComponent<ActorRenderMeshInstance>(entity))
            {
                var instance = EntityManager.GetComponentData<ActorRenderMeshInstance>(entity);
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
        }
    }
}
#endif
