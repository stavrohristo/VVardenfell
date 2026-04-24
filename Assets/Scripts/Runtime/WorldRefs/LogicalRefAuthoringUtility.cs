using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Content;

namespace VVardenfell.Runtime.Components
{
    public static class LogicalRefAuthoringUtility
    {
        public static bool QueueAttach(
            EntityManager entityManager,
            ref EntityCommandBuffer ecb,
            Entity logicalEntity,
            RuntimeContentDatabase contentDb,
            ContentReference contentReference,
            bool attachDoorInteractable = false,
            DoorInteractable doorInteractable = default)
        {
            if (contentDb == null || !contentDb.IsValid(contentReference))
                return false;

            switch (contentReference.Kind)
            {
                case ContentReferenceKind.Actor:
                {
                    var handle = new ActorDefHandle { Value = contentReference.HandleValue };
                    ref readonly var actor = ref contentDb.Get(handle);
                    ecb.AddComponent(logicalEntity, new ActorSpawnSource { Definition = handle });
                    ecb.AddComponent(logicalEntity, new DialogueSpeakerAuthoring { Definition = handle });
                    ecb.AddComponent(logicalEntity, BuildPassiveActorPresence(handle, actor));
                    return true;
                }

                case ContentReferenceKind.Activator:
                {
                    var handle = new ActivatorDefHandle { Value = contentReference.HandleValue };
                    ref readonly var def = ref contentDb.Get(handle);
                    ecb.AddComponent(logicalEntity, new ActivatorAuthoring { Definition = handle });
                    TryQueueAudioEmitterAuthoring(ref ecb, logicalEntity, contentDb, def.SoundId, def.AuxSoundId);
                    return true;
                }

                case ContentReferenceKind.Door:
                {
                    var handle = new DoorDefHandle { Value = contentReference.HandleValue };
                    ref readonly var def = ref contentDb.Get(handle);
                    ecb.AddComponent(logicalEntity, new DoorAuthoring { Definition = handle });
                    TryQueueAudioEmitterAuthoring(ref ecb, logicalEntity, contentDb, def.SoundId, def.AuxSoundId);
                    if (attachDoorInteractable)
                        ecb.AddComponent(logicalEntity, doorInteractable);
                    return true;
                }

                case ContentReferenceKind.Container:
                {
                    var handle = new ContainerDefHandle { Value = contentReference.HandleValue };
                    ref readonly var def = ref contentDb.Get(handle);
                    ecb.AddComponent(logicalEntity, new ContainerAuthoring { Definition = handle });
                    TryQueueAudioEmitterAuthoring(ref ecb, logicalEntity, contentDb, def.SoundId, def.AuxSoundId);
                    return true;
                }

                case ContentReferenceKind.Item:
                {
                    var handle = new ItemDefHandle { Value = contentReference.HandleValue };
                    ref readonly var def = ref contentDb.Get(handle);
                    ecb.AddComponent(logicalEntity, new ItemPickupAuthoring { Definition = handle });
                    TryQueueAudioEmitterAuthoring(ref ecb, logicalEntity, contentDb, def.SoundId, def.AuxSoundId);
                    return true;
                }

                case ContentReferenceKind.Light:
                {
                    var handle = new LightDefHandle { Value = contentReference.HandleValue };
                    ref readonly var def = ref contentDb.Get(handle);
                    ecb.AddComponent(logicalEntity, new LightSourceAuthoring { Definition = handle });
                    ecb.AddComponent(logicalEntity, BuildLightInstanceFlags(def.Flags));
                    ecb.AddComponent(logicalEntity, BuildLightInstanceState(def));
                    ecb.AddComponent(logicalEntity, new LightPresentationLink { Slot = -1 });
                    TryQueueAudioEmitterAuthoring(ref ecb, logicalEntity, contentDb, def.SoundId, null);
                    if (contentDb.TryGetSoundHandle(def.SoundId, out SoundDefHandle ambientSound) && ambientSound.IsValid)
                    {
                        ecb.AddComponent(logicalEntity, new InteriorAmbientSourceAuthoring
                        {
                            AmbientSound = ambientSound,
                        });
                    }
                    return true;
                }

                case ContentReferenceKind.Static:
                {
                    var handle = new GenericRecordDefHandle { Value = contentReference.HandleValue };
                    ecb.AddComponent(logicalEntity, new StaticRefAuthoring { Definition = handle });
                    return true;
                }

                case ContentReferenceKind.LeveledItem:
                {
                    var handle = new ItemLeveledListDefHandle { Value = contentReference.HandleValue };
                    ecb.AddComponent(logicalEntity, new LeveledItemAuthoring { Definition = handle });
                    return true;
                }

                case ContentReferenceKind.LeveledCreature:
                {
                    var handle = new CreatureLeveledListDefHandle { Value = contentReference.HandleValue };
                    ecb.AddComponent(logicalEntity, new LeveledCreatureAuthoring { Definition = handle });
                    return true;
                }

                default:
                    return false;
            }
        }

        static PassiveActorPresence BuildPassiveActorPresence(ActorDefHandle handle, in ActorDef actor)
        {
            string displayName = !string.IsNullOrWhiteSpace(actor.Name)
                ? actor.Name.Trim()
                : !string.IsNullOrWhiteSpace(actor.Id)
                    ? actor.Id.Trim()
                    : "Actor";
            if (displayName.Length > 127)
                displayName = displayName.Substring(0, 127);

            bool canTalk = actor.Kind == ActorDefKind.Npc;
            return new PassiveActorPresence
            {
                Definition = handle,
                Family = (byte)(actor.Kind == ActorDefKind.Npc ? PassiveActorFamily.Npc : PassiveActorFamily.Creature),
                CanTalk = (byte)(canTalk ? 1 : 0),
                DisplayName = displayName,
            };
        }

        static LightInstanceFlags BuildLightInstanceFlags(int flags)
        {
            const int Carry = 0x002;
            const int Negative = 0x004;
            const int Flicker = 0x008;
            const int OffDefault = 0x020;
            const int FlickerSlow = 0x040;
            const int Pulse = 0x080;
            const int PulseSlow = 0x100;

            return new LightInstanceFlags
            {
                Carry = (byte)((flags & Carry) != 0 ? 1 : 0),
                Negative = (byte)((flags & Negative) != 0 ? 1 : 0),
                Flicker = (byte)((flags & Flicker) != 0 ? 1 : 0),
                FlickerSlow = (byte)((flags & FlickerSlow) != 0 ? 1 : 0),
                Pulse = (byte)((flags & Pulse) != 0 ? 1 : 0),
                PulseSlow = (byte)((flags & PulseSlow) != 0 ? 1 : 0),
                OffDefault = (byte)((flags & OffDefault) != 0 ? 1 : 0),
            };
        }

        static LightInstanceState BuildLightInstanceState(in LightDef def)
        {
            float3 color = DecodeRgb(def.ColorRgba);
            float rangeMeters = math.max(0.25f, def.Radius * WorldScale.MwUnitsToMeters);
            float intensity = math.max(0.25f, math.cmax(color));
            bool enabled = (def.Flags & 0x020) == 0;

            return new LightInstanceState
            {
                Enabled = (byte)(enabled ? 1 : 0),
                BaseColorRgb = color,
                BaseIntensity = intensity,
                BaseRange = rangeMeters,
                CurrentIntensity = intensity,
                CurrentRange = rangeMeters,
                AnimationTime = 0f,
            };
        }

        static float3 DecodeRgb(uint value)
        {
            return new float3(
                ((value >> 0) & 0xFFu) / 255f,
                ((value >> 8) & 0xFFu) / 255f,
                ((value >> 16) & 0xFFu) / 255f);
        }

        static void TryQueueAudioEmitterAuthoring(
            ref EntityCommandBuffer ecb,
            Entity logicalEntity,
            RuntimeContentDatabase contentDb,
            string primarySoundId,
            string secondarySoundId)
        {
            contentDb.TryGetSoundHandle(primarySoundId, out SoundDefHandle primarySound);
            contentDb.TryGetSoundHandle(secondarySoundId, out SoundDefHandle secondarySound);
            if (!primarySound.IsValid && !secondarySound.IsValid)
                return;

            ecb.AddComponent(logicalEntity, new AudioEmitterAuthoring
            {
                PrimarySound = primarySound,
                SecondarySound = secondarySound,
            });
        }
    }
}
