using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
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
            _movementLookup = GetComponentLookup<MorrowindMovementState>(isReadOnly: true);
            _equipmentLookup = GetBufferLookup<ActorEquipmentSlot>(isReadOnly: true);
        }

        protected override void OnUpdate()
        {
            var contentDb = RuntimeContentDatabase.Active;
            if (contentDb == null || SystemAPI.GetSingleton<AudioContextState>().Mode != AudioPlaybackMode.World)
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

                ref readonly var actor = ref contentDb.Get(source.ValueRO.Definition);
                for (int i = 0; i < events.Length; i++)
                {
                    var animationEvent = events[i];
                    string group = animationEvent.Group.ToString();
                    if (string.Equals(group, "sound", StringComparison.OrdinalIgnoreCase))
                    {
                        string soundId = animationEvent.Value.ToString();
                        if (contentDb.TryGetSoundHandle(soundId, out var sound) && sound.IsValid)
                            EmitAudioRequest(ref audioState, ref ecb, entity, placedRef.ValueRO.Value, transform.ValueRO.Position, sound, 1f, 1f, spatial: true);
                    }
                    else if (string.Equals(group, "soundgen", StringComparison.OrdinalIgnoreCase))
                    {
                        ParseSoundGeneratorEvent(animationEvent.Value.ToString(), out string typeName, out float volume, out float pitch);
                        if (TryResolveSoundGenerator(contentDb, actor, entity, typeName, ref random, out var sound) && sound.IsValid)
                            EmitAudioRequest(ref audioState, ref ecb, entity, placedRef.ValueRO.Value, transform.ValueRO.Position, sound, volume, pitch, spatial: true);
                    }
                }
            }

            ProcessLocalPlayerVisualAudio(contentDb, ref audioState, ref ecb, ref random);

            ecb.Playback(EntityManager);
            ecb.Dispose();
            _randomState = random.state;
        }

        void ProcessLocalPlayerVisualAudio(
            RuntimeContentDatabase contentDb,
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

                ref readonly var actor = ref contentDb.Get(source.ValueRO.Definition);
                for (int i = 0; i < events.Length; i++)
                {
                    var animationEvent = events[i];
                    string group = animationEvent.Group.ToString();
                    if (string.Equals(group, "sound", StringComparison.OrdinalIgnoreCase))
                    {
                        string soundId = animationEvent.Value.ToString();
                        if (contentDb.TryGetSoundHandle(soundId, out var sound) && sound.IsValid)
                            EmitAudioRequest(ref audioState, ref ecb, entity, 0u, transform.ValueRO.Position, sound, 1f, 1f, spatial: false);
                    }
                    else if (string.Equals(group, "soundgen", StringComparison.OrdinalIgnoreCase))
                    {
                        ParseSoundGeneratorEvent(animationEvent.Value.ToString(), out string typeName, out float volume, out float pitch);
                        if (TryResolveSoundGenerator(contentDb, actor, entity, typeName, ref random, out var sound) && sound.IsValid)
                            EmitAudioRequest(ref audioState, ref ecb, entity, 0u, transform.ValueRO.Position, sound, volume, pitch, spatial: false);
                    }
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

        bool TryResolveSoundGenerator(
            RuntimeContentDatabase contentDb,
            in ActorDef actor,
            Entity actorEntity,
            string typeName,
            ref Unity.Mathematics.Random random,
            out SoundDefHandle sound)
        {
            sound = default;
            if (actor.Kind == ActorDefKind.Npc)
                return TryResolveNpcSoundGenerator(contentDb, actorEntity, typeName, out sound);

            if (!TryResolveSoundGeneratorType(typeName, out int type))
                return false;

            string actorId = string.IsNullOrWhiteSpace(actor.OriginalId) ? actor.Id : actor.OriginalId;
            if (TryPickSoundGenerator(contentDb, type, actorId, ref random, out sound))
                return true;

            string fallbackActorId = ResolveCreatureModelFallback(contentDb, actor);
            if (!string.IsNullOrWhiteSpace(fallbackActorId)
                && !string.Equals(fallbackActorId, actorId, StringComparison.OrdinalIgnoreCase)
                && TryPickSoundGenerator(contentDb, type, fallbackActorId, ref random, out sound))
                return true;

            return TryPickSoundGenerator(contentDb, type, string.Empty, ref random, out sound);
        }

        bool TryResolveNpcSoundGenerator(
            RuntimeContentDatabase contentDb,
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
                    return TryGetNpcSound(contentDb, left ? "FootWaterLeft" : "FootWaterRight", out sound);
                if (!movement.Grounded)
                    return false;

                return TryResolveNpcFootstepSound(contentDb, actorEntity, left, out sound);
            }

            if (string.Equals(typeName, "swimleft", StringComparison.OrdinalIgnoreCase))
                return TryGetNpcSound(contentDb, "Swim Left", out sound);
            if (string.Equals(typeName, "swimright", StringComparison.OrdinalIgnoreCase))
                return TryGetNpcSound(contentDb, "Swim Right", out sound);

            return false;
        }

        bool TryResolveNpcFootstepSound(
            RuntimeContentDatabase contentDb,
            Entity actorEntity,
            bool left,
            out SoundDefHandle sound)
        {
            sound = default;
            if (!_equipmentLookup.HasBuffer(actorEntity))
                return TryGetNpcSound(contentDb, left ? "FootBareLeft" : "FootBareRight", out sound);

            var equipment = _equipmentLookup[actorEntity];
            if (!TryGetEquippedBoots(equipment, out var boots)
                || boots.Content.Kind != ContentReferenceKind.Item)
            {
                return TryGetNpcSound(contentDb, left ? "FootBareLeft" : "FootBareRight", out sound);
            }

            var itemHandle = new ItemDefHandle { Value = boots.Content.HandleValue };
            if (!contentDb.TryGetItemEquipment(itemHandle, out var itemEquipment)
                || itemEquipment.Kind != ItemEquipmentKind.Armor)
            {
                return TryGetNpcSound(contentDb, left ? "FootBareLeft" : "FootBareRight", out sound);
            }

            if (!TryResolveArmorFootstepClass(contentDb, itemEquipment, out var armorClass))
                return false;

            string soundId = armorClass switch
            {
                NpcFootstepArmorClass.Light => left ? "footLightLeft" : "footLightRight",
                NpcFootstepArmorClass.Medium => left ? "FootMedLeft" : "FootMedRight",
                NpcFootstepArmorClass.Heavy => left ? "footHeavyLeft" : "footHeavyRight",
                _ => string.Empty,
            };

            return !string.IsNullOrEmpty(soundId) && TryGetNpcSound(contentDb, soundId, out sound);
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
            RuntimeContentDatabase contentDb,
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

            if (string.IsNullOrEmpty(weightSetting)
                || !contentDb.TryGetGameSettingFloat(weightSetting, out float typeWeight)
                || !contentDb.TryGetGameSettingFloat("fLightMaxMod", out float lightMax)
                || !contentDb.TryGetGameSettingFloat("fMedMaxMod", out float mediumMax))
            {
                return false;
            }

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

        static bool TryGetNpcSound(RuntimeContentDatabase contentDb, string soundId, out SoundDefHandle sound)
            => contentDb.TryGetSoundHandle(soundId, out sound) && sound.IsValid;

        enum NpcFootstepArmorClass : byte
        {
            Light,
            Medium,
            Heavy,
        }

        static string ResolveCreatureModelFallback(RuntimeContentDatabase contentDb, in ActorDef actor)
        {
            if (string.IsNullOrWhiteSpace(actor.Model))
                return string.Empty;

            string actorId = string.IsNullOrWhiteSpace(actor.OriginalId) ? actor.Id : actor.OriginalId;
            var actors = contentDb.Data.Actors;
            for (int i = 0; i < actors.Length; i++)
            {
                var candidate = actors[i];
                if (candidate.Kind != ActorDefKind.Creature)
                    continue;
                if (string.Equals(candidate.Id, actorId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate.OriginalId, actorId, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.Equals(candidate.Model, actor.Model, StringComparison.OrdinalIgnoreCase))
                    continue;

                return string.IsNullOrWhiteSpace(candidate.OriginalId) ? candidate.Id : candidate.OriginalId;
            }

            return string.Empty;
        }

        static bool TryPickSoundGenerator(
            RuntimeContentDatabase contentDb,
            int type,
            string creatureId,
            ref Unity.Mathematics.Random random,
            out SoundDefHandle sound)
        {
            sound = default;
            var records = contentDb.Data.SoundGenerators;
            int count = 0;
            for (int i = 0; i < records.Length; i++)
            {
                if (!MatchesSoundGenerator(records[i], type, creatureId))
                    continue;
                count++;
            }

            if (count <= 0)
                return false;

            int selected = random.NextInt(count);
            for (int i = 0; i < records.Length; i++)
            {
                var record = records[i];
                if (!MatchesSoundGenerator(record, type, creatureId))
                    continue;
                if (selected-- != 0)
                    continue;

                return !string.IsNullOrWhiteSpace(record.Text)
                       && contentDb.TryGetSoundHandle(record.Text, out sound)
                       && sound.IsValid;
            }

            return false;
        }

        static bool MatchesSoundGenerator(in GenericRecordDef record, int type, string creatureId)
        {
            if (record.Int0 != type)
                return false;
            if (string.IsNullOrWhiteSpace(creatureId))
                return string.IsNullOrWhiteSpace(record.ScriptId);
            return string.Equals(record.ScriptId, creatureId, StringComparison.OrdinalIgnoreCase);
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
