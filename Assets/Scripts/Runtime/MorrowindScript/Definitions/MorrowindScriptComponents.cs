using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Components
{
    public struct MorrowindScriptRuntimeState : IComponentData
    {
        public uint NextAudioRequestSequence;
        public uint RandomState;
    }

    public struct MorrowindScriptFaultReported : IComponentData
    {
    }

    public struct MorrowindGlobalScriptInstance : IComponentData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
    }

    public struct VanillaNewGameStartupPending : IComponentData
    {
    }

    public struct MorrowindScriptStartRequest : IBufferElementData
    {
        public MorrowindScriptProgramDefHandle Program;
        public int ProgramIndex;
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
    }

    public struct MorrowindScriptStopRequest : IBufferElementData
    {
        public int ProgramIndex;
    }

    public struct MorrowindScriptGlobalValue : IBufferElementData
    {
        public int IntValue;
        public float FloatValue;
        public byte ValueKind;
    }

    public struct MorrowindActorDeathCount : IBufferElementData
    {
        public int Count;
    }

    public struct MorrowindActorDeathCounted : IComponentData
    {
    }

    public struct MorrowindActorOnDeathConsumed : IComponentData
    {
    }

    public struct MorrowindQuestJournalState : IComponentData
    {
        public int QuestCount;
        public uint NextEntrySequence;
    }

    public struct MorrowindQuestJournalIndex : IBufferElementData
    {
        public int Index;
        public byte Started;
        public byte Finished;
    }

    public struct MorrowindQuestJournalEntry : IBufferElementData
    {
        public uint Sequence;
        public int DialogueIndex;
        public int InfoIndex;
        public int JournalIndex;
        public int Day;
        public int Month;
        public int DayOfMonth;
        public byte QuestStatus;
    }

    public enum MorrowindQuestJournalRequestOperation : byte
    {
        Journal = 0,
        SetIndex = 1,
    }

    public struct MorrowindQuestJournalRequest : IBufferElementData
    {
        public int DialogueIndex;
        public int InfoIndex;
        public int JournalIndex;
        public byte QuestStatus;
        public byte Operation;
    }

    public struct MorrowindScriptActiveSource : IBufferElementData
    {
        public ulong LoopSourceKey;
    }

    public struct MorrowindScriptPlayingSound : IBufferElementData
    {
        public ulong LoopKey;
    }

    public struct MorrowindScriptActiveSay : IBufferElementData
    {
        public Entity SourceEntity;
        public uint SourcePlacedRefId;
        public float Loudness;
    }

    public struct MorrowindScriptRefStateRequest : IBufferElementData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public byte Disabled;
    }

    public struct MorrowindScriptTransformRequest : IBufferElementData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public float Radians;
        public float3 Position;
        public ulong InteriorCellHash;
        public byte Axis;
        public byte Operation;
    }

    public enum MorrowindScriptAiPackageRequestType : byte
    {
        Wander = 1,
        Travel = 2,
        Follow = 3,
        StopCombat = 4,
        StartCombat = 5,
        Escort = 6,
        Activate = 7,
    }

    public struct MorrowindScriptActorAiSettingRequest : IBufferElementData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public int Value;
        public byte Kind;
        public byte IsMod;
    }

    public struct MorrowindScriptDispositionRequest : IBufferElementData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public int Value;
        public byte IsMod;
    }

    public enum MorrowindScriptActorVitalRequestKind : byte
    {
        Health = 1,
        Magicka = 2,
        Fatigue = 3,
        Resurrect = 4,
    }

    public struct MorrowindScriptActorVitalRequest : IBufferElementData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public float Value;
        public byte Kind;
        public byte IsMod;
    }

    public struct MorrowindScriptHurtStandingActorRequest : IBufferElementData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public float HealthPerSecond;
    }

    public struct MorrowindScriptAnimationGroupRequest : IBufferElementData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public int GroupMessageIndex;
        public uint LoopCount;
        public byte Mode;
        public byte Operation;
    }

    public struct MorrowindScriptInventoryMutationRequest : IBufferElementData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public ContentReference Content;
        public int Count;
        public int SoulActorHandleValue;
        public byte TargetMode;
        public byte Operation;
    }

    public struct MorrowindScriptSayRequest : IBufferElementData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public FixedString512Bytes VoicePath;
        public FixedString512Bytes Subtitle;
        public byte AllowMissingVoicePath;
    }

    public struct MorrowindCombatHitVoiceSayRequest : IBufferElementData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public FixedString512Bytes VoicePath;
        public FixedString512Bytes Subtitle;
    }

    public struct MorrowindCombatHitVoiceResolveRequest : IBufferElementData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public ActorDefHandle Actor;
        public int DialogueIndex;
        public uint RandomState;
    }

    public struct MorrowindCombatVoiceResolveRequest : IBufferElementData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public ActorDefHandle Actor;
        public ulong DialogueIdHash;
        public uint RandomState;
    }

    public struct MorrowindScriptActorLocalSetRequest : IBufferElementData
    {
        public int ActorHandleValue;
        public int LocalIndex;
        public MorrowindScriptLocalValue Value;
    }

    public struct MorrowindScriptFactionReactionRequest : IBufferElementData
    {
        public int SourceFactionIndex;
        public int TargetFactionIndex;
        public int Value;
        public byte IsMod;
    }

    public struct MorrowindScriptExternalActorLocalSnapshot
    {
        public int ActorHandleValue;
        public int LocalIndex;
        public MorrowindScriptLocalValue Value;
    }

    public struct MorrowindScriptActorAiStatusSnapshot
    {
        public uint PlacedRefId;
        public byte Status;
        public int CurrentPackageTypeId;
    }

    public struct MorrowindScriptActorCombatTargetSnapshot
    {
        public Entity ActorEntity;
        public uint ActorPlacedRefId;
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public byte Active;
    }

    public struct MorrowindScriptLockStateSnapshot
    {
        public uint PlacedRefId;
        public int LockLevel;
        public byte Locked;
    }

    public struct MorrowindScriptActorEventSnapshot
    {
        public Entity Entity;
        public uint PlacedRefId;
        public byte Murdered;
        public byte Attacked;
        public byte KnockedDownOneFrame;
        public Entity LastHitAttemptActor;
        public uint LastHitAttemptActorPlacedRefId;
        public ContentReference LastHitAttemptObject;
        public ContentReference LastHitObject;
    }

    public struct MorrowindScriptActorVitalSnapshot
    {
        public uint PlacedRefId;
        public float Health;
        public float Magicka;
        public float Fatigue;
    }

    public struct MorrowindScriptActorAttributeSnapshot
    {
        public uint PlacedRefId;
        public ActorAttributeSet Attributes;
    }

    public struct MorrowindScriptActorActiveEffectSnapshot
    {
        public Entity ActorEntity;
        public uint PlacedRefId;
        public SpellDefHandle SourceSpell;
        public short EffectId;
    }

    public struct MorrowindScriptActorDiseaseSnapshot
    {
        public uint PlacedRefId;
        public byte HasCommonDisease;
        public byte HasBlightDisease;
    }

    public struct MorrowindScriptActorIdentitySnapshot
    {
        public Entity ActorEntity;
        public uint PlacedRefId;
        public FixedString64Bytes RaceName;
    }

    public struct MorrowindScriptActorAiSettingSnapshot
    {
        public Entity ActorEntity;
        public uint PlacedRefId;
        public int Hello;
        public int Fight;
        public int Flee;
        public int Alarm;
    }

    public struct MorrowindScriptActorDispositionSnapshot
    {
        public Entity ActorEntity;
        public uint PlacedRefId;
        public int BaseDisposition;
    }

    public struct MorrowindScriptActorLineOfSightSnapshot
    {
        public uint SourcePlacedRefId;
        public uint TargetPlacedRefId;
        public byte HasLineOfSight;
    }

    public struct MorrowindScriptActiveSaySnapshot
    {
        public Entity SourceEntity;
        public uint SourcePlacedRefId;
        public float Loudness;
    }

    public struct MorrowindScriptActorKnownSpellSnapshot
    {
        public Entity ActorEntity;
        public uint PlacedRefId;
        public SpellDefHandle Spell;
    }

    public struct MorrowindScriptRunningProgramSnapshot
    {
        public int ProgramIndex;
        public byte Running;
    }

    public struct MorrowindScriptOnDeathConsumeRequest : IBufferElementData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
    }

    public enum MorrowindScriptActorEventConsumeKind : byte
    {
        Murdered = 1,
        LastHitObject = 2,
    }

    public struct MorrowindScriptActorEventConsumeRequest : IBufferElementData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public byte Kind;
    }

    public enum MorrowindScriptShellRequestOperation : byte
    {
        WakeUpPlayer = 1,
        ScreenFade = 2,
        PlayerControls = 3,
        Teleporting = 4,
        MenuEnabled = 5,
        PlayerFighting = 6,
        PlayerJumping = 7,
        PlayerMagic = 8,
        PlayerViewSwitch = 9,
        Rest = 10,
        VanityMode = 11,
        ShowRestMenu = 12,
        PlayBink = 13,
        PlayerLooking = 14,
    }

    public struct MorrowindScriptShellRequest : IBufferElementData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public byte Operation;
        public byte FadeOut;
        public byte Enabled;
        public byte MenuKind;
        public byte AllowSkipping;
        public float Duration;
        public FixedString128Bytes MovieName;
    }

    public struct MorrowindScriptJailRequest : IBufferElementData
    {
        public int Days;
    }

    public struct MorrowindScriptMovementFlagRequest : IBufferElementData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public byte FlagKind;
        public byte Enabled;
    }

    public struct MorrowindScriptPlaceAtRequest : IBufferElementData
    {
        public ContentReference Content;
        public int Count;
        public float Distance;
        public byte Direction;
    }

    public struct MorrowindScriptAiPackageRequest : IBufferElementData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public Entity FollowTargetEntity;
        public uint FollowTargetPlacedRefId;
        public float3 TargetPosition;
        public float WanderRadius;
        public float IdleSeconds;
        public float DurationHours;
        public float FollowDistance;
        public ulong DestinationInteriorCellHash;
        public byte PackageType;
        public byte ShouldRepeat;
        public byte AllowPartial;
        public byte IdleChance0;
        public byte IdleChance1;
        public byte IdleChance2;
        public byte IdleChance3;
        public byte IdleChance4;
        public byte IdleChance5;
        public byte IdleChance6;
        public byte IdleChance7;
        public FixedString128Bytes TargetId;
    }

    public struct MorrowindScriptAudioRequest : IComponentData
    {
        public uint Sequence;
        public SoundDefHandle Sound;
        public FixedString512Bytes DirectPath;
        public Entity SourceEntity;
        public uint SourcePlacedRefId;
        public float3 Position;
        public float Volume;
        public float Pitch;
        public byte Kind;
        public byte Spatial;
        public byte Looping;
        public byte AllowMissingDirectPath;
    }
}
