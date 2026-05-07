using System;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Player
{
    [UpdateInGroup(typeof(MorrowindPresentationBuildSystemGroup))]
    [UpdateBefore(typeof(LocalPlayerPresentationSpawnSystem))]
    public partial struct LocalPlayerAppearanceRebuildSystem : ISystem
    {
        EntityQuery _visualQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _visualQuery = systemState.GetEntityQuery(ComponentType.ReadOnly<LocalPlayerVisual>());
            systemState.RequireForUpdate<PlayerRaceAppearance>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            ref RuntimeContentBlob content = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            bool changed = false;
            foreach (var (appearanceRef, entity) in
                     SystemAPI.Query<RefRW<PlayerRaceAppearance>>()
                         .WithAll<PlayerTag>()
                         .WithEntityAccess())
            {
                ref var appearance = ref appearanceRef.ValueRW;
                if (appearance.Dirty == 0)
                    continue;

                ValidateAppearance(ref content, appearance);
                LocalPlayerPresentationLifecycleUtility.QueueDestroyLocalPlayerVisuals(systemState.EntityManager, _visualQuery, ref ecb);
                if (systemState.EntityManager.HasComponent<LocalPlayerPresentationState>(entity))
                    ecb.RemoveComponent<LocalPlayerPresentationState>(entity);
                if (systemState.EntityManager.HasComponent<LocalPlayerPresentationPose>(entity))
                    ecb.RemoveComponent<LocalPlayerPresentationPose>(entity);
                appearance.Dirty = 0;
                changed = true;
            }

            if (changed)
                ecb.Playback(systemState.EntityManager);
            ecb.Dispose();
        }

        static void ValidateAppearance(ref RuntimeContentBlob content, in PlayerRaceAppearance appearance)
        {
            if (appearance.RaceId.IsEmpty)
                throw new InvalidOperationException("[VVardenfell][CharGen] Player appearance has no race id.");
            CharacterGenerationUtility.RequireRace(ref content, appearance.RaceId);
            if (!appearance.HeadId.IsEmpty)
                RequireBodyPart(ref content, appearance.HeadId, ActorBodyPartMeshPart.Head);
            if (!appearance.HairId.IsEmpty)
                RequireBodyPart(ref content, appearance.HairId, ActorBodyPartMeshPart.Hair);
        }

        static void RequireBodyPart(ref RuntimeContentBlob content, FixedString64Bytes id, ActorBodyPartMeshPart expectedPart)
        {
            ulong hash = RuntimeContentStableHash.HashId(id.ToString());
            if (!RuntimeContentBlobUtility.TryGetActorBodyPartHandleByIdHash(ref content, hash, out var handle)
                || !handle.IsValid
                || (uint)handle.Index >= (uint)content.ActorBodyParts.Length)
            {
                throw new InvalidOperationException($"[VVardenfell][CharGen] Missing selected body part '{id}'.");
            }

            ref RuntimeActorBodyPartDefBlob bodyPart = ref content.ActorBodyParts[handle.Index];
            if (bodyPart.Part != expectedPart || bodyPart.Type != ActorBodyPartMeshType.Skin || bodyPart.NotPlayable != 0)
                throw new InvalidOperationException($"[VVardenfell][CharGen] Body part '{id}' is not a playable {expectedPart} skin part.");
        }
    }
}
