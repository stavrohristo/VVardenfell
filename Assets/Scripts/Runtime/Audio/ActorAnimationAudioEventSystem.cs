using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Audio
{
    [UpdateInGroup(typeof(MorrowindAudioSimulationSystemGroup))]
    public partial class ActorAnimationAudioEventSystem : SystemBase
    {
        uint _randomState = 1u;
        ComponentLookup<MorrowindMovementState> _movementLookup;
        BufferLookup<ActorEquipmentSlot> _equipmentLookup;

        protected override void OnCreate()
        {
            RequireForUpdate<AudioContextState>();
            RequireForUpdate<InteractionAudioRequestState>();
            RequireForUpdate<RuntimeContentBlobReference>();
            _movementLookup = GetComponentLookup<MorrowindMovementState>(isReadOnly: true);
            _equipmentLookup = GetBufferLookup<ActorEquipmentSlot>(isReadOnly: true);
        }

        protected override void OnUpdate()
        {
            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            if (SystemAPI.GetSingleton<AudioContextState>().Mode != AudioPlaybackMode.World)
                return;

            _movementLookup.Update(this);
            _equipmentLookup.Update(this);

            ref var audioState = ref SystemAPI.GetSingletonRW<InteractionAudioRequestState>().ValueRW;
            var random = new Unity.Mathematics.Random(_randomState == 0 ? 1u : _randomState);
            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

            foreach (var (source, placedRef, transform, events, entity) in
                     SystemAPI.Query<RefRO<ActorSpawnSource>, RefRO<PlacedRefIdentity>, RefRO<LocalTransform>, DynamicBuffer<ActorAnimationEvent>>()
                         .WithAll<ActorPresentation>()
                         .WithEntityAccess())
            {
                if (events.Length == 0)
                    continue;

                ref var actor = ref RuntimeContentBlobUtility.Get(ref contentBlob, source.ValueRO.Definition);
                ProcessAnimationEvents(
                    ref contentBlob,
                    ref actor,
                    entity,
                    placedRef.ValueRO.Value,
                    transform.ValueRO.Position,
                    events,
                    spatial: true,
                    ref audioState,
                    ref ecb,
                    ref random);
            }

            ProcessLocalPlayerVisualAudio(ref contentBlob, ref audioState, ref ecb, ref random);

            ecb.Playback(EntityManager);
            ecb.Dispose();
            _randomState = random.state;
        }

        void ProcessLocalPlayerVisualAudio(
            ref RuntimeContentBlob contentBlob,
            ref InteractionAudioRequestState audioState,
            ref EntityCommandBuffer ecb,
            ref Unity.Mathematics.Random random)
        {
            if (!SystemAPI.TryGetSingleton<LocalPlayerPresentationState>(out var presentation))
                return;

            Entity activeVisual = presentation.Mode == PlayerViewMode.FirstPerson
                ? presentation.FirstPersonVisual
                : presentation.ThirdPersonVisual;
            if (activeVisual == Entity.Null)
                return;

            foreach (var (source, visual, transform, events, entity) in
                     SystemAPI.Query<RefRO<ActorSpawnSource>, RefRO<LocalPlayerVisual>, RefRO<LocalTransform>, DynamicBuffer<ActorAnimationEvent>>()
                         .WithAll<ActorPresentation>()
                         .WithEntityAccess())
            {
                if (entity != activeVisual || visual.ValueRO.Player == Entity.Null || events.Length == 0)
                    continue;

                ref var actor = ref RuntimeContentBlobUtility.Get(ref contentBlob, source.ValueRO.Definition);
                ProcessAnimationEvents(
                    ref contentBlob,
                    ref actor,
                    entity,
                    0u,
                    transform.ValueRO.Position,
                    events,
                    spatial: false,
                    ref audioState,
                    ref ecb,
                    ref random);
            }
        }

        void ProcessAnimationEvents(
            ref RuntimeContentBlob contentBlob,
            ref RuntimeActorDefBlob actor,
            Entity entity,
            uint placedRefId,
            float3 position,
            DynamicBuffer<ActorAnimationEvent> events,
            bool spatial,
            ref InteractionAudioRequestState audioState,
            ref EntityCommandBuffer ecb,
            ref Unity.Mathematics.Random random)
        {
            for (int i = 0; i < events.Length; i++)
            {
                var animationEvent = events[i];
                if (EqualsAsciiIgnoreCase(animationEvent.Group, "sound"))
                {
                    string soundId = animationEvent.Value.ToString();
                    if (RuntimeContentBlobUtility.TryGetSoundHandleByIdHash(ref contentBlob, RuntimeContentStableHash.HashId(soundId), out var sound) && sound.IsValid)
                        EmitAudioRequest(ref audioState, ref ecb, entity, placedRefId, position, sound, 1f, 1f, spatial);
                }
                else if (EqualsAsciiIgnoreCase(animationEvent.Group, "soundgen"))
                {
                    ParseSoundGeneratorEvent(animationEvent.Value.ToString(), out string typeName, out float volume, out float pitch);
                    if (TryResolveSoundGenerator(ref contentBlob, ref actor, entity, typeName, ref random, out var sound) && sound.IsValid)
                        EmitAudioRequest(ref audioState, ref ecb, entity, placedRefId, position, sound, volume, pitch, spatial);
                }
            }
        }

        void EmitAudioRequest(
            ref InteractionAudioRequestState audioState,
            ref EntityCommandBuffer ecb,
            Entity sourceEntity,
            uint placedRefId,
            float3 position,
            SoundDefHandle sound,
            float volume,
            float pitch,
            bool spatial)
        {
            audioState.NextSequence++;
            Entity requestEntity = ecb.CreateEntity();
            ecb.AddComponent(requestEntity, new MorrowindScriptAudioRequest
            {
                Sequence = audioState.NextSequence,
                Sound = sound,
                SourceEntity = sourceEntity,
                SourcePlacedRefId = placedRefId,
                Position = position,
                Volume = math.max(0f, volume),
                Pitch = math.max(0.01f, pitch),
                Kind = (byte)MorrowindScriptAudioKind.PlaySound3DVP,
                Spatial = (byte)(spatial ? 1 : 0),
                Looping = 0,
            });
        }

        static void ParseSoundGeneratorEvent(string value, out string typeName, out float volume, out float pitch)
        {
            typeName = string.Empty;
            volume = 1f;
            pitch = 1f;

            if (string.IsNullOrWhiteSpace(value))
                return;

            string[] parts = value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return;

            typeName = parts[0];
            if (parts.Length >= 2 && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedVolume))
                volume = parsedVolume;
            if (parts.Length >= 3 && float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedPitch))
                pitch = parsedPitch;
        }

        static bool EqualsAsciiIgnoreCase(in FixedString64Bytes value, string expected)
        {
            if (value.Length != expected.Length)
                return false;

            for (int i = 0; i < expected.Length; i++)
            {
                byte left = value[i];
                char right = expected[i];
                if (left >= (byte)'A' && left <= (byte)'Z')
                    left = (byte)(left + 32);
                if (right >= 'A' && right <= 'Z')
                    right = (char)(right + 32);
                if (left != right)
                    return false;
            }

            return true;
        }

        bool TryResolveSoundGenerator(
            ref RuntimeContentBlob contentBlob,
            ref RuntimeActorDefBlob actor,
            Entity actorEntity,
            string typeName,
            ref Unity.Mathematics.Random random,
            out SoundDefHandle sound)
        {
            sound = default;
            if (actor.Kind == ActorDefKind.Npc)
                return TryResolveNpcSoundGenerator(ref contentBlob, actorEntity, typeName, out sound);

            if (!TryResolveSoundGeneratorType(typeName, out int type))
                return false;

            string originalActorId = actor.OriginalId.ToString();
            string actorId = string.IsNullOrWhiteSpace(originalActorId) ? actor.Id.ToString() : originalActorId;
            if (TryPickSoundGenerator(ref contentBlob, type, actorId, ref random, out sound))
                return true;

            string fallbackActorId = ResolveCreatureModelFallback(ref contentBlob, ref actor);
            if (!string.IsNullOrWhiteSpace(fallbackActorId)
                && !string.Equals(fallbackActorId, actorId, StringComparison.OrdinalIgnoreCase)
                && TryPickSoundGenerator(ref contentBlob, type, fallbackActorId, ref random, out sound))
                return true;

            return TryPickSoundGenerator(ref contentBlob, type, string.Empty, ref random, out sound);
        }

        bool TryResolveNpcSoundGenerator(
            ref RuntimeContentBlob contentBlob,
            Entity actorEntity,
            string typeName,
            out SoundDefHandle sound)
        {
            sound = default;
            if (string.Equals(typeName, "left", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "right", StringComparison.OrdinalIgnoreCase))
            {
                if (!_movementLookup.HasComponent(actorEntity))
                    return false;

                bool left = string.Equals(typeName, "left", StringComparison.OrdinalIgnoreCase);
                var movement = _movementLookup[actorEntity];
                if (movement.WalkingOnWater)
                    return TryGetNpcSound(ref contentBlob, left ? "FootWaterLeft" : "FootWaterRight", out sound);
                if (!movement.Grounded)
                    return false;

                return TryResolveNpcFootstepSound(ref contentBlob, actorEntity, left, out sound);
            }

            if (string.Equals(typeName, "swimleft", StringComparison.OrdinalIgnoreCase))
                return TryGetNpcSound(ref contentBlob, "Swim Left", out sound);
            if (string.Equals(typeName, "swimright", StringComparison.OrdinalIgnoreCase))
                return TryGetNpcSound(ref contentBlob, "Swim Right", out sound);

            return false;
        }

        bool TryResolveNpcFootstepSound(
            ref RuntimeContentBlob contentBlob,
            Entity actorEntity,
            bool left,
            out SoundDefHandle sound)
        {
            sound = default;
            if (!_equipmentLookup.HasBuffer(actorEntity))
                return TryGetNpcSound(ref contentBlob, left ? "FootBareLeft" : "FootBareRight", out sound);

            var equipment = _equipmentLookup[actorEntity];
            if (!TryGetEquippedBoots(equipment, out var boots)
                || boots.Content.Kind != ContentReferenceKind.Item)
            {
                return TryGetNpcSound(ref contentBlob, left ? "FootBareLeft" : "FootBareRight", out sound);
            }

            var itemHandle = new ItemDefHandle { Value = boots.Content.HandleValue };
            if (!RuntimeContentBlobUtility.TryGetItemEquipment(ref contentBlob, itemHandle, out var itemEquipment)
                || itemEquipment.Kind != ItemEquipmentKind.Armor)
            {
                return TryGetNpcSound(ref contentBlob, left ? "FootBareLeft" : "FootBareRight", out sound);
            }

            if (!TryResolveArmorFootstepClass(ref contentBlob, itemEquipment, out var armorClass))
                return false;

            string soundId = armorClass switch
            {
                NpcFootstepArmorClass.Light => left ? "footLightLeft" : "footLightRight",
                NpcFootstepArmorClass.Medium => left ? "FootMedLeft" : "FootMedRight",
                NpcFootstepArmorClass.Heavy => left ? "footHeavyLeft" : "footHeavyRight",
                _ => string.Empty,
            };

            return !string.IsNullOrEmpty(soundId) && TryGetNpcSound(ref contentBlob, soundId, out sound);
        }

        static bool TryGetEquippedBoots(
            DynamicBuffer<ActorEquipmentSlot> equipment,
            out ActorEquipmentSlot boots)
        {
            for (int i = 0; i < equipment.Length; i++)
            {
                var slot = equipment[i];
                if (slot.Slot == ItemEquipmentSlot.Boots || slot.Slot == ItemEquipmentSlot.Shoes)
                {
                    boots = slot;
                    return true;
                }
            }

            boots = default;
            return false;
        }

        static bool TryResolveArmorFootstepClass(
            ref RuntimeContentBlob contentBlob,
            in ItemEquipmentDef equipment,
            out NpcFootstepArmorClass armorClass)
        {
            armorClass = default;
            string weightSetting = equipment.Type switch
            {
                0 => "iHelmWeight",
                1 => "iCuirassWeight",
                2 or 3 => "iPauldronWeight",
                4 => "iGreavesWeight",
                5 => "iBootsWeight",
                6 or 7 or 9 or 10 => "iGauntletWeight",
                8 => "iShieldWeight",
                _ => string.Empty,
            };

            if (string.IsNullOrEmpty(weightSetting))
            {
                return false;
            }

            int typeWeight = RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref contentBlob, RuntimeContentStableHash.HashId(weightSetting));
            float lightMax = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref contentBlob, RuntimeContentKnownHashes.fLightMaxMod);
            float mediumMax = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref contentBlob, RuntimeContentKnownHashes.fMedMaxMod);
            float baseWeight = math.floor(typeWeight);
            if (equipment.Weight <= baseWeight * lightMax + 0.0005f)
            {
                armorClass = NpcFootstepArmorClass.Light;
                return true;
            }

            if (equipment.Weight <= baseWeight * mediumMax + 0.0005f)
            {
                armorClass = NpcFootstepArmorClass.Medium;
                return true;
            }

            armorClass = NpcFootstepArmorClass.Heavy;
            return true;
        }

        static bool TryGetNpcSound(ref RuntimeContentBlob contentBlob, string soundId, out SoundDefHandle sound)
            => RuntimeContentBlobUtility.TryGetSoundHandleByIdHash(ref contentBlob, RuntimeContentStableHash.HashId(soundId), out sound) && sound.IsValid;

        enum NpcFootstepArmorClass : byte
        {
            Light,
            Medium,
            Heavy,
        }

        static string ResolveCreatureModelFallback(ref RuntimeContentBlob contentBlob, ref RuntimeActorDefBlob actor)
        {
            string actorModel = actor.Model.ToString();
            if (string.IsNullOrWhiteSpace(actorModel))
                return string.Empty;

            string originalActorId = actor.OriginalId.ToString();
            string actorId = string.IsNullOrWhiteSpace(originalActorId) ? actor.Id.ToString() : originalActorId;
            for (int i = 0; i < contentBlob.Actors.Length; i++)
            {
                ref var candidate = ref contentBlob.Actors[i];
                if (candidate.Kind != ActorDefKind.Creature)
                    continue;
                if (string.Equals(candidate.Id.ToString(), actorId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate.OriginalId.ToString(), actorId, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.Equals(candidate.Model.ToString(), actorModel, StringComparison.OrdinalIgnoreCase))
                    continue;

                string candidateOriginalId = candidate.OriginalId.ToString();
                return string.IsNullOrWhiteSpace(candidateOriginalId) ? candidate.Id.ToString() : candidateOriginalId;
            }

            return string.Empty;
        }

        static bool TryPickSoundGenerator(
            ref RuntimeContentBlob contentBlob,
            int type,
            string creatureId,
            ref Unity.Mathematics.Random random,
            out SoundDefHandle sound)
        {
            sound = default;
            int count = 0;
            for (int i = 0; i < contentBlob.SoundGenerators.Length; i++)
            {
                ref var record = ref contentBlob.SoundGenerators[i];
                if (!MatchesSoundGenerator(ref record, type, creatureId))
                    continue;
                count++;
            }

            if (count <= 0)
                return false;

            int selected = random.NextInt(count);
            for (int i = 0; i < contentBlob.SoundGenerators.Length; i++)
            {
                ref var record = ref contentBlob.SoundGenerators[i];
                if (!MatchesSoundGenerator(ref record, type, creatureId))
                    continue;
                if (selected-- != 0)
                    continue;

                string soundId = record.Text.ToString();
                return !string.IsNullOrWhiteSpace(soundId)
                       && RuntimeContentBlobUtility.TryGetSoundHandleByIdHash(ref contentBlob, RuntimeContentStableHash.HashId(soundId), out sound)
                       && sound.IsValid;
            }

            return false;
        }

        static bool MatchesSoundGenerator(ref RuntimeGenericRecordDefBlob record, int type, string creatureId)
        {
            if (record.Int0 != type)
                return false;
            if (string.IsNullOrWhiteSpace(creatureId))
                return string.IsNullOrWhiteSpace(record.ScriptId.ToString());
            return string.Equals(record.ScriptId.ToString(), creatureId, StringComparison.OrdinalIgnoreCase);
        }

        static bool TryResolveSoundGeneratorType(string name, out int type)
        {
            if (string.Equals(name, "left", StringComparison.OrdinalIgnoreCase))
            {
                type = 0;
                return true;
            }
            if (string.Equals(name, "right", StringComparison.OrdinalIgnoreCase))
            {
                type = 1;
                return true;
            }
            if (string.Equals(name, "swimleft", StringComparison.OrdinalIgnoreCase))
            {
                type = 2;
                return true;
            }
            if (string.Equals(name, "swimright", StringComparison.OrdinalIgnoreCase))
            {
                type = 3;
                return true;
            }
            if (string.Equals(name, "moan", StringComparison.OrdinalIgnoreCase))
            {
                type = 4;
                return true;
            }
            if (string.Equals(name, "roar", StringComparison.OrdinalIgnoreCase))
            {
                type = 5;
                return true;
            }
            if (string.Equals(name, "scream", StringComparison.OrdinalIgnoreCase))
            {
                type = 6;
                return true;
            }
            if (string.Equals(name, "land", StringComparison.OrdinalIgnoreCase))
            {
                type = 7;
                return true;
            }

            type = -1;
            return false;
        }
    }
}
