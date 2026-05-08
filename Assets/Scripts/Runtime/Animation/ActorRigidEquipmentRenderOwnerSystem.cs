using Unity.Burst;
#if !VVARDENFELL_OLD_ACTOR_ANIMATION
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Rendering;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindPresentationBuildSystemGroup))]
    [UpdateAfter(typeof(ActorPresentationSpawnSystem))]
    [UpdateBefore(typeof(ActorRigidEquipmentVisibilitySystem))]
    public partial struct ActorRigidEquipmentRenderOwnerSystem : ISystem
    {
        EntityQuery _dirtyQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _dirtyQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<ActorRigidEquipmentAttachment>(),
                ComponentType.ReadOnly<ActorRigidEquipmentRenderOwnerDirty>());
            systemState.RequireForUpdate(_dirtyQuery);
        }
        [BurstCompile]
        public void OnUpdate(ref SystemState systemState)
        {

            
            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            foreach (var (attachment, entity) in
                     SystemAPI.Query<RefRO<ActorRigidEquipmentAttachment>>()
                         .WithAll<ActorRigidEquipmentRenderOwnerDirty>()
                         .WithEntityAccess())
            {
                Entity renderOwner = ResolveRenderOwner(ref systemState, attachment.ValueRO);
                AssignOwner(ref systemState, ref ecb, entity, renderOwner);
                if (!systemState.EntityManager.HasBuffer<LinkedEntityGroup>(entity))
                {
                    ecb.SetComponentEnabled<ActorRigidEquipmentRenderOwnerDirty>(entity, false);
                    continue;
                }

                var linked = systemState.EntityManager.GetBuffer<LinkedEntityGroup>(entity);
                for (int i = 0; i < linked.Length; i++)
                {
                    Entity child = linked[i].Value;
                    if (child != entity)
                        AssignOwner(ref systemState, ref ecb, child, renderOwner);
                }

                ecb.SetComponentEnabled<ActorRigidEquipmentRenderOwnerDirty>(entity, false);
            }

            ecb.Playback(systemState.EntityManager);
            ecb.Dispose();
        }

        Entity ResolveRenderOwner(ref SystemState systemState, in ActorRigidEquipmentAttachment attachment)
        {
            if (attachment.Actor == Entity.Null || !systemState.EntityManager.HasComponent<ActorWeaponAnimationState>(attachment.Actor))
                return Entity.Null;

            var weaponState = systemState.EntityManager.GetComponentData<ActorWeaponAnimationState>(attachment.Actor);
            return weaponState.WeaponType != ActorWeaponAnimationUtility.SpellWeaponType
                   && (weaponState.Drawn != 0 || weaponState.Phase == ActorWeaponAnimationPhase.Equipping)
                ? attachment.Actor
                : Entity.Null;
        }

        void AssignOwner(ref SystemState systemState, 
            ref EntityCommandBuffer ecb,
            Entity entity,
            Entity actor)
        {
            if (entity == Entity.Null || !systemState.EntityManager.Exists(entity))
                return;

            if (systemState.EntityManager.HasComponent<ActorRenderMeshInstance>(entity))
            {
                var instance = systemState.EntityManager.GetComponentData<ActorRenderMeshInstance>(entity);
                if (instance.Actor == actor)
                    return;

                instance.Actor = actor;
                ecb.SetComponent(entity, instance);
                return;
            }

            if (!systemState.EntityManager.HasComponent<ModelPrefabRenderLeaf>(entity))
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
