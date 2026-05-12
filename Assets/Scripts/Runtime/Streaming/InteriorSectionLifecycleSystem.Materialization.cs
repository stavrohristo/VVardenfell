using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.Streaming
{
    public partial class InteriorSectionLifecycleSystem
    {
        readonly struct MaterializationContext
        {
            public readonly EntityManager EntityManager;
            public readonly RuntimeMaterializationResources Resources;

            public MaterializationContext(InteriorSectionLifecycleSystem system)
            {
                EntityManager = system.EntityManager;
                Resources = RuntimeMaterializationResources.Require(system.EntityManager);
            }
        }

        public bool MaterializeInteriorSection(
            RuntimeCellSectionLoadResult section,
            float3 worldOffset,
            ref RuntimeSectionRegistry sectionRegistry,
            ref LogicalRefLookup logicalRefs,
            Action<ulong, bool> setInteriorActive,
            out FixedString128Bytes interiorCellId)
        {
            interiorCellId = default;
            if (section.SectionEntity == Entity.Null)
                return false;

            var em = EntityManager;
            interiorCellId = section.Header.CellId;
            if (!em.HasComponent<RuntimeCellSectionResourcesBound>(section.SectionEntity))
                throw new InvalidOperationException("[VVardenfell][CellSection] section root is missing resource binding state; rebake required.");
            if (!em.IsComponentEnabled<RuntimeCellSectionResourcesBound>(section.SectionEntity))
            {
                var context = new MaterializationContext(this);
                PatchSectionTransformsFromBuffer(context, section.SectionEntity, worldOffset, isInterior: true);
                RuntimeRenderIdBindingUtility.BindCellSection(em, context.Resources, section.SectionEntity);
                BindColliderSourcesFromBuffer(context, section.SectionEntity);
                RegisterLogicalRefsAndInitializeActorsFromBuffers(context, section.SectionEntity, ref logicalRefs, isInterior: true);
                em.SetComponentEnabled<RuntimeCellSectionResourcesBound>(section.SectionEntity, true);
            }

            if (sectionRegistry.InteriorSectionsByHash.IsCreated && section.Header.InteriorCellHash != 0UL)
                sectionRegistry.InteriorSectionsByHash[section.Header.InteriorCellHash] = section.SectionEntity;
            setInteriorActive(section.Header.InteriorCellHash, true);
            return true;
        }

        static void PatchSectionTransformsFromBuffer(MaterializationContext context, Entity sectionEntity, float3 worldOffset, bool isInterior)
        {
            EntityManager em = context.EntityManager;
            var roots = RequireBuffer<RuntimeCellSectionTransformRootEntity>(em, sectionEntity, "transform roots");
            for (int i = 0; i < roots.Length; i++)
            {
                Entity entity = RequireExisting(em, roots[i].Value, "transform root");
                if (!em.HasComponent<LocalTransform>(entity))
                    throw new InvalidOperationException("[VVardenfell][CellSection] transform root is missing LocalTransform; rebake required.");
                if (em.HasComponent<Parent>(entity))
                    throw new InvalidOperationException("[VVardenfell][CellSection] transform root has a Parent; rebake required.");

                var transform = em.GetComponentData<LocalTransform>(entity);
                if (isInterior)
                {
                    transform.Position += worldOffset;
                    em.SetComponentData(entity, transform);
                }

                if (em.HasComponent<LocalToWorld>(entity))
                {
                    em.SetComponentData(entity, new LocalToWorld
                    {
                        Value = float4x4.TRS(transform.Position, transform.Rotation, new float3(transform.Scale)),
                    });
                }
            }
        }

        static void BindColliderSourcesFromBuffer(MaterializationContext context, Entity sectionEntity)
        {
            EntityManager em = context.EntityManager;
            var colliderEntities = RequireBuffer<RuntimeCellSectionColliderEntity>(em, sectionEntity, "collider entities");
            for (int i = 0; i < colliderEntities.Length; i++)
            {
                Entity entity = RequireExisting(em, colliderEntities[i].Value, "collider entity");
                if (em.HasComponent<RuntimeColliderSource>(entity))
                {
                    var source = em.GetComponentData<RuntimeColliderSource>(entity);
                    if (source.Value.IsCreated)
                        continue;
                }

                if (em.HasComponent<RuntimeCellSectionRenderRoot>(entity))
                {
                    var root = em.GetComponentData<RuntimeCellSectionRenderRoot>(entity);
                    if (root.CollisionIndex >= 0)
                    {
                        var placeholder = em.GetComponentData<RuntimeColliderSource>(entity);
                        if (placeholder.Value.IsCreated || placeholder.Kind != RuntimeColliderKind.PlacedRef || placeholder.Temporary != 0)
                            throw new InvalidOperationException("[VVardenfell][CellSection] placed-ref root has invalid RuntimeColliderSource placeholder; rebake required.");
                        if (em.HasComponent<RuntimeGeneratedColliderBlobCleanup>(entity))
                            throw new InvalidOperationException("[VVardenfell][CellSection] placed-ref root has generated collider cleanup in a baked section; rebake required.");
                        var collider = RequireGlobalCollider(context.Resources, root.CollisionIndex, "[VVardenfell][CellSection] placed-ref root");
                        if (!RuntimeColliderAttachmentUtility.BindExistingSource(em, entity, collider, RuntimeColliderKind.PlacedRef))
                            throw new InvalidOperationException($"[VVardenfell][CellSection] failed to attach placed-ref collider {root.CollisionIndex}.");
                        continue;
                    }
                }

                if (em.HasComponent<RuntimeSpawnPrefabPickCollider>(entity))
                {
                    var pick = em.GetComponentData<RuntimeSpawnPrefabPickCollider>(entity);
                    var placeholder = em.GetComponentData<RuntimeColliderSource>(entity);
                    if (placeholder.Value.IsCreated || placeholder.Kind != RuntimeColliderKind.InteractionPick || placeholder.Temporary != 0)
                        throw new InvalidOperationException("[VVardenfell][CellSection] pick collider has invalid RuntimeColliderSource placeholder; rebake required.");
                    if (em.HasComponent<RuntimeGeneratedColliderBlobCleanup>(entity))
                        throw new InvalidOperationException("[VVardenfell][CellSection] pick collider has generated collider cleanup in a baked section; rebake required.");
                    var collider = RequireGlobalCollider(context.Resources, pick.ColliderIndex, "[VVardenfell][CellSection] pick collider");
                    if (!RuntimeColliderAttachmentUtility.BindExistingSource(em, entity, collider, RuntimeColliderKind.InteractionPick))
                        throw new InvalidOperationException($"[VVardenfell][CellSection] failed to attach pick collider {pick.ColliderIndex}.");
                    continue;
                }

                throw new InvalidOperationException("[VVardenfell][CellSection] collider buffer contains entity with no bindable collider source; rebake required.");
            }
        }

        static void RegisterLogicalRefsAndInitializeActorsFromBuffers(MaterializationContext context, Entity sectionEntity, ref LogicalRefLookup logicalRefs, bool isInterior)
        {
            EntityManager em = context.EntityManager;
            var contentBlob = context.Resources.ContentBlob;
            if (!contentBlob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][CellSection] Runtime content blob is not loaded.");
            ref RuntimeContentBlob content = ref contentBlob.Value;

            var logicalEntities = RequireBuffer<RuntimeCellSectionLogicalRefEntity>(em, sectionEntity, "logical ref entities");
            for (int i = 0; i < logicalEntities.Length; i++)
            {
                Entity entity = RequireExisting(em, logicalEntities[i].Value, "logical ref entity");
                var identity = em.GetComponentData<PlacedRefIdentity>(entity);
                LogicalRefLookupUtility.AddOrThrow(ref logicalRefs, identity.Value, entity, isInterior);
            }

            var actorEntities = RequireBuffer<RuntimeCellSectionActorInitEntity>(em, sectionEntity, "actor init entities");
            if (actorEntities.Length == 0)
                return;

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            try
            {
                for (int i = 0; i < actorEntities.Length; i++)
                {
                    Entity entity = RequireExisting(em, actorEntities[i].Value, "actor init entity");
                    var location = em.GetComponentData<LogicalRefLocation>(entity);
                    LogicalRefAuthoringUtility.QueueAttachRuntimeActorState(
                        em,
                        ref ecb,
                        entity,
                        ref content,
                        em.GetComponentData<ActorSpawnSource>(entity).Definition,
                        em.GetComponentData<PlacedRefInitialTransform>(entity).Position,
                        location.IsInterior != 0,
                        location.ExteriorCell,
                        location.InteriorCellId,
                        em.GetComponentData<PlacedRefIdentity>(entity).Value);
                    ecb.SetComponentEnabled<RuntimeCellSectionActorNeedsInitialization>(entity, false);
                }
                ecb.Playback(em);
            }
            finally
            {
                ecb.Dispose();
            }
        }

        static DynamicBuffer<T> RequireBuffer<T>(EntityManager em, Entity entity, string label)
            where T : unmanaged, IBufferElementData
        {
            if (entity == Entity.Null || !em.Exists(entity) || !em.HasBuffer<T>(entity))
                throw new InvalidOperationException($"[VVardenfell][CellSection] section root is missing {label}; rebake required.");
            return em.GetBuffer<T>(entity);
        }

        static Entity RequireExisting(EntityManager em, Entity entity, string label)
        {
            if (entity == Entity.Null || !em.Exists(entity))
                throw new InvalidOperationException($"[VVardenfell][CellSection] section buffer references missing {label}; rebake required.");
            return entity;
        }

        static BlobAssetReference<Unity.Physics.Collider> RequireGlobalCollider(RuntimeMaterializationResources resources, int index, string context)
        {
            var blobs = resources.ColliderBlobs;
            if (blobs == null || (uint)index >= (uint)blobs.Length || !blobs[index].IsCreated)
                throw new InvalidOperationException($"{context} references missing global collider {index}; rebake required.");
            return blobs[index];
        }

    }
}
