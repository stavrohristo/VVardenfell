#if !VVARDENFELL_OLD_ACTOR_ANIMATION
using Unity.Entities;
using Unity.Rendering;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(ActorWeaponAnimationSystem))]
    public partial class ActorRigidEquipmentVisibilitySystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<ActorRigidEquipmentAttachment>();
        }

        protected override void OnUpdate()
        {
            foreach (var (attachment, entity) in
                     SystemAPI.Query<RefRO<ActorRigidEquipmentAttachment>>()
                         .WithEntityAccess())
            {
                if (attachment.ValueRO.Slot != ItemEquipmentSlot.Weapon)
                    continue;

                bool visible = false;
                Entity actor = attachment.ValueRO.Actor;
                if (actor != Entity.Null
                    && EntityManager.Exists(actor)
                    && EntityManager.HasComponent<ActorWeaponAnimationState>(actor))
                {
                    var weaponState = EntityManager.GetComponentData<ActorWeaponAnimationState>(actor);
                    visible = weaponState.Drawn != 0 || weaponState.Phase == ActorWeaponAnimationPhase.Equipping;
                }

                SetModelPrefabVisible(entity, visible);
            }
        }

        void SetModelPrefabVisible(Entity root, bool visible)
        {
            SetRenderLeafVisible(root, visible);
            if (!EntityManager.HasBuffer<LinkedEntityGroup>(root))
                return;

            var linked = EntityManager.GetBuffer<LinkedEntityGroup>(root);
            for (int i = 0; i < linked.Length; i++)
                SetRenderLeafVisible(linked[i].Value, visible);
        }

        void SetRenderLeafVisible(Entity entity, bool visible)
        {
            if (entity == Entity.Null
                || !EntityManager.Exists(entity)
                || !EntityManager.HasComponent<MaterialMeshInfo>(entity))
            {
                return;
            }

            EntityManager.SetComponentEnabled<MaterialMeshInfo>(entity, visible);
        }
    }
}
#endif
