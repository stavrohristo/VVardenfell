#if !VVARDENFELL_OLD_ACTOR_ANIMATION
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Rendering;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Animation
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(ActorWeaponAnimationSystem))]
    public partial struct ActorRigidEquipmentVisibilitySystem : ISystem
    {
        ComponentLookup<ActorWeaponAnimationState> _weaponStateLookup;
        ComponentLookup<MaterialMeshInfo> _materialMeshLookup;
        BufferLookup<LinkedEntityGroup> _linkedLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ActorRigidEquipmentAttachment>();
            _weaponStateLookup = state.GetComponentLookup<ActorWeaponAnimationState>(isReadOnly: true);
            _materialMeshLookup = state.GetComponentLookup<MaterialMeshInfo>(isReadOnly: false);
            _linkedLookup = state.GetBufferLookup<LinkedEntityGroup>(isReadOnly: true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _weaponStateLookup.Update(ref state);
            _materialMeshLookup.Update(ref state);
            _linkedLookup.Update(ref state);

            state.Dependency = new SyncRigidEquipmentVisibilityJob
            {
                WeaponStateLookup = _weaponStateLookup,
                MaterialMeshLookup = _materialMeshLookup,
                LinkedLookup = _linkedLookup,
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        partial struct SyncRigidEquipmentVisibilityJob : IJobEntity
        {
            [ReadOnly] public ComponentLookup<ActorWeaponAnimationState> WeaponStateLookup;
            [ReadOnly] public BufferLookup<LinkedEntityGroup> LinkedLookup;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<MaterialMeshInfo> MaterialMeshLookup;

            void Execute(Entity entity, in ActorRigidEquipmentAttachment attachment)
            {
                if (attachment.Slot != ItemEquipmentSlot.Weapon)
                    return;

                bool visible = false;
                Entity actor = attachment.Actor;
                if (actor != Entity.Null && WeaponStateLookup.HasComponent(actor))
                {
                    ActorWeaponAnimationState weaponState = WeaponStateLookup[actor];
                    visible = weaponState.Drawn != 0 || weaponState.Phase == ActorWeaponAnimationPhase.Equipping;
                }

                SetRenderLeafVisible(entity, visible);
                if (!LinkedLookup.HasBuffer(entity))
                    return;

                DynamicBuffer<LinkedEntityGroup> linked = LinkedLookup[entity];
                for (int i = 0; i < linked.Length; i++)
                {
                    Entity child = linked[i].Value;
                    if (child != entity)
                        SetRenderLeafVisible(child, visible);
                }
            }

            void SetRenderLeafVisible(Entity entity, bool visible)
            {
                if (entity == Entity.Null || !MaterialMeshLookup.HasComponent(entity))
                    return;

                if (MaterialMeshLookup.IsComponentEnabled(entity) != visible)
                    MaterialMeshLookup.SetComponentEnabled(entity, visible);
            }
        }
    }
}
#endif
