using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.MorrowindScript
{
    public unsafe delegate void MorrowindScriptOpcodeDelegate(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction);

    public unsafe struct MorrowindScriptExecutionContext
    {
        public Entity Entity;
        public EntityCommandBuffer.ParallelWriter Ecb;
        public int SortKey;
        public int ProgramCounter;
        public int StackLength;
        public int StackCapacity;
        public MorrowindScriptStackValue* Stack;
        public MorrowindScriptLocalValue* Locals;
        public int LocalCount;
        public MorrowindScriptGlobalValue* Globals;
        public int GlobalCount;
        public MorrowindActorDeathCount* DeathCounts;
        public int DeathCountCount;
        public MorrowindQuestJournalIndex* QuestJournal;
        public int QuestJournalCount;
        public NativeParallelHashMap<uint, byte> RefDisabledStates;
        public NativeParallelHashMap<int, ActiveExplicitRefTarget> ActiveExplicitRefs;
        public float SecondsPassed;
        public float3 Position;
        public quaternion Rotation;
        public float3 PlayerPosition;
        public Entity PlayerEntity;
        public uint PlayerStandingOnPlacedRefId;
        public PlayerInventoryItem* PlayerInventory;
        public int PlayerInventoryCount;
        public ActorKnownSpell* ActorKnownSpells;
        public int ActorKnownSpellCount;
        public PlayerFactionMembership* PlayerFactions;
        public int PlayerFactionCount;
        public ActorSkillSet PlayerSkills;
        public PlayerCrimeState PlayerCrime;
        public int PlayerCrimeLevel;
        public MorrowindScriptExternalActorLocalSnapshot* ExternalActorLocals;
        public int ExternalActorLocalCount;
        public MorrowindScriptActorAiStatusSnapshot* ActorAiStatuses;
        public int ActorAiStatusCount;
        public MorrowindScriptActorCombatTargetSnapshot* ActorCombatTargets;
        public int ActorCombatTargetCount;
        public MorrowindScriptRefTransformSnapshot* RefTransforms;
        public int RefTransformCount;
        public MorrowindScriptInitialTransformSnapshot* InitialTransforms;
        public int InitialTransformCount;
        public MorrowindScriptLockStateSnapshot* LockStates;
        public int LockStateCount;
        public MorrowindScriptInventoryCountSnapshot* InventoryCounts;
        public int InventoryCountCount;
        public MorrowindScriptActorDeathSnapshot* ActorDeaths;
        public int ActorDeathCount;
        public MorrowindScriptActorEventSnapshot* ActorEvents;
        public int ActorEventCount;
        public MorrowindScriptActorVitalSnapshot* ActorVitals;
        public int ActorVitalCount;
        public MorrowindScriptActorAttributeSnapshot* ActorAttributes;
        public int ActorAttributeCount;
        public MorrowindScriptActorActiveEffectSnapshot* ActorActiveEffects;
        public int ActorActiveEffectCount;
        public MorrowindScriptActorDiseaseSnapshot* ActorDiseases;
        public int ActorDiseaseCount;
        public MorrowindScriptActorIdentitySnapshot* ActorIdentities;
        public int ActorIdentityCount;
        public MorrowindScriptActorAiSettingSnapshot* ActorAiSettings;
        public int ActorAiSettingCount;
        public MorrowindScriptActorDispositionSnapshot* ActorDispositions;
        public int ActorDispositionCount;
        public MorrowindScriptActorLineOfSightSnapshot* ActorLineOfSight;
        public int ActorLineOfSightCount;
        public MorrowindScriptActorKnownSpellSnapshot* ActorKnownSpellSnapshots;
        public int ActorKnownSpellSnapshotCount;
        public MorrowindScriptRunningProgramSnapshot* RunningPrograms;
        public int RunningProgramCount;
        public MorrowindScriptActiveSaySnapshot* ActiveSays;
        public int ActiveSayCount;
        public uint* RandomState;
        public FixedString512Bytes* Messages;
        public int MessageCount;
        public ulong* PlayingScriptSoundKeys;
        public int PlayingScriptSoundKeyCount;
        public uint PlacedRefId;
        public uint AudioSequenceBase;
        public Entity InteractionRuntimeEntity;
        public Entity QuestJournalRuntimeEntity;
        public Entity DialogueRuntimeEntity;
        public Entity MessageBoxRuntimeEntity;
        public Entity ShellRuntimeEntity;
        public Entity MovementRuntimeEntity;
        public Entity PlaceAtRuntimeEntity;
        public Entity AnimationRuntimeEntity;
        public Entity StartScriptRuntimeEntity;
        public Entity StopScriptRuntimeEntity;
        public Entity ActorVitalRuntimeEntity;
        public Entity ActorSpellRuntimeEntity;
        public Entity CastRuntimeEntity;
        public Entity ForceGreetingRuntimeEntity;
        public Entity PlayerReputationRuntimeEntity;
        public Entity ActorAttributeRuntimeEntity;
        public Entity PlayerSkillRuntimeEntity;
        public Entity PlayerFactionRuntimeEntity;
        public Entity ActorFactionRuntimeEntity;
        public Entity InventoryDropRuntimeEntity;
        public Entity WeatherRuntimeEntity;
        public Entity MusicRuntimeEntity;
        public Entity GlobalMapRevealRuntimeEntity;
        public Entity SayRuntimeEntity;
        public Entity OnDeathRuntimeEntity;
        public Entity ActorEventRuntimeEntity;
        public Entity RefStateRuntimeEntity;
        public Entity TransformRuntimeEntity;
        public Entity AiRuntimeEntity;
        public ScriptActivationEvent* ActivationEvents;
        public int ActivationEventCount;
        public int MatchedActivationEventIndex;
        public byte HasPlayerPosition;
        public byte HasPlayerSkills;
        public byte HasCellChanged;
        public byte CellChanged;
        public byte HasMenuMode;
        public byte MenuMode;
        public byte HasModalButtonPressed;
        public int ModalButtonPressed;
        public byte* ModalButtonPressedRead;
        public byte HasPlayerSleeping;
        public byte PlayerSleeping;
        public byte HasPlayerCellName;
        public FixedString128Bytes PlayerCellName;
        public byte HasCurrentWeather;
        public int CurrentWeather;
        public byte ObservedOnActivate;
        public byte SelfDisabled;
        public byte StopRequested;
        public byte Halted;
        public byte Faulted;
    }

    [BurstCompile]
    public static unsafe class MorrowindScriptOpcodeTable
    {
        const int OpcodeCount = 134;

        public static NativeArray<FunctionPointer<MorrowindScriptOpcodeDelegate>> CreateHandlers(Allocator allocator)
        {
            var handlers = new NativeArray<FunctionPointer<MorrowindScriptOpcodeDelegate>>(OpcodeCount, allocator);
            var unsupported = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(Unsupported);
            for (int i = 0; i < handlers.Length; i++)
                handlers[i] = unsupported;

            handlers[(int)MorrowindScriptOpcode.Nop] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(Nop);
            handlers[(int)MorrowindScriptOpcode.Return] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(Return);
            handlers[(int)MorrowindScriptOpcode.PushInt] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(PushInt);
            handlers[(int)MorrowindScriptOpcode.PushFloat] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(PushFloat);
            handlers[(int)MorrowindScriptOpcode.GetLocal] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetLocal);
            handlers[(int)MorrowindScriptOpcode.SetLocalInt] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(SetLocalInt);
            handlers[(int)MorrowindScriptOpcode.SetLocalFloat] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(SetLocalFloat);
            handlers[(int)MorrowindScriptOpcode.GetGlobal] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetGlobal);
            handlers[(int)MorrowindScriptOpcode.SetGlobalInt] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(SetGlobalInt);
            handlers[(int)MorrowindScriptOpcode.SetGlobalFloat] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(SetGlobalFloat);
            handlers[(int)MorrowindScriptOpcode.Add] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(Add);
            handlers[(int)MorrowindScriptOpcode.Subtract] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(Subtract);
            handlers[(int)MorrowindScriptOpcode.Multiply] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(Multiply);
            handlers[(int)MorrowindScriptOpcode.Divide] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(Divide);
            handlers[(int)MorrowindScriptOpcode.CompareEqual] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(CompareEqual);
            handlers[(int)MorrowindScriptOpcode.CompareNotEqual] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(CompareNotEqual);
            handlers[(int)MorrowindScriptOpcode.CompareLess] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(CompareLess);
            handlers[(int)MorrowindScriptOpcode.CompareLessOrEqual] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(CompareLessOrEqual);
            handlers[(int)MorrowindScriptOpcode.CompareGreater] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(CompareGreater);
            handlers[(int)MorrowindScriptOpcode.CompareGreaterOrEqual] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(CompareGreaterOrEqual);
            handlers[(int)MorrowindScriptOpcode.Jump] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(Jump);
            handlers[(int)MorrowindScriptOpcode.JumpIfZero] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(JumpIfZero);
            handlers[(int)MorrowindScriptOpcode.EmitAudioRequest] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(EmitAudioRequest);
            handlers[(int)MorrowindScriptOpcode.GetDistancePlayer] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetDistancePlayer);
            handlers[(int)MorrowindScriptOpcode.GetCellChanged] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetCellChanged);
            handlers[(int)MorrowindScriptOpcode.GetSoundPlaying] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetSoundPlaying);
            handlers[(int)MorrowindScriptOpcode.GetMenuMode] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetMenuMode);
            handlers[(int)MorrowindScriptOpcode.GetJournalIndex] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetJournalIndex);
            handlers[(int)MorrowindScriptOpcode.GetSecondsPassed] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetSecondsPassed);
            handlers[(int)MorrowindScriptOpcode.Negate] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(Negate);
            handlers[(int)MorrowindScriptOpcode.GetOnActivate] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetOnActivate);
            handlers[(int)MorrowindScriptOpcode.Activate] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(Activate);
            handlers[(int)MorrowindScriptOpcode.Rotate] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(Rotate);
            handlers[(int)MorrowindScriptOpcode.GetDisabled] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetDisabled);
            handlers[(int)MorrowindScriptOpcode.RequestSetDisabled] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(RequestSetDisabled);
            handlers[(int)MorrowindScriptOpcode.SetAngle] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(SetAngle);
            handlers[(int)MorrowindScriptOpcode.Journal] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(Journal);
            handlers[(int)MorrowindScriptOpcode.StopScript] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(StopScript);
            handlers[(int)MorrowindScriptOpcode.SetJournalIndex] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(SetJournalIndex);
            handlers[(int)MorrowindScriptOpcode.AddTopic] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(AddTopic);
            handlers[(int)MorrowindScriptOpcode.FillJournal] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(FillJournal);
            handlers[(int)MorrowindScriptOpcode.GetPCCell] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetPCCell);
            handlers[(int)MorrowindScriptOpcode.GetDeadCount] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetDeadCount);
            handlers[(int)MorrowindScriptOpcode.PositionCell] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(PositionCell);
            handlers[(int)MorrowindScriptOpcode.AiWander] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(AiWander);
            handlers[(int)MorrowindScriptOpcode.AiTravel] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(AiTravel);
            handlers[(int)MorrowindScriptOpcode.AiFollow] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(AiFollow);
            handlers[(int)MorrowindScriptOpcode.AiFollowCell] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(AiFollowCell);
            handlers[(int)MorrowindScriptOpcode.StopCombat] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(StopCombat);
            handlers[(int)MorrowindScriptOpcode.StartCombat] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(StartCombat);
            handlers[(int)MorrowindScriptOpcode.SetActorAiSetting] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(SetActorAiSetting);
            handlers[(int)MorrowindScriptOpcode.SetDisposition] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(SetDisposition);
            handlers[(int)MorrowindScriptOpcode.GetPlayerItemCount] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetPlayerItemCount);
            handlers[(int)MorrowindScriptOpcode.GetPlayerSpell] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetPlayerSpell);
            handlers[(int)MorrowindScriptOpcode.GetPCRank] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetPCRank);
            handlers[(int)MorrowindScriptOpcode.RequestInventoryMutation] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(RequestInventoryMutation);
            handlers[(int)MorrowindScriptOpcode.GetActorLocal] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetActorLocal);
            handlers[(int)MorrowindScriptOpcode.SetActorLocalInt] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(SetActorLocalInt);
            handlers[(int)MorrowindScriptOpcode.SetActorLocalFloat] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(SetActorLocalFloat);
            handlers[(int)MorrowindScriptOpcode.GetAiPackageDone] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetAiPackageDone);
            handlers[(int)MorrowindScriptOpcode.GetAngle] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetAngle);
            handlers[(int)MorrowindScriptOpcode.RequestMessageBox] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(RequestMessageBox);
            handlers[(int)MorrowindScriptOpcode.GetItemCount] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetItemCount);
            handlers[(int)MorrowindScriptOpcode.SetHealth] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(SetHealth);
            handlers[(int)MorrowindScriptOpcode.GetOnDeath] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetOnDeath);
            handlers[(int)MorrowindScriptOpcode.Position] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(Position);
            handlers[(int)MorrowindScriptOpcode.GetPCCrimeLevel] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetPCCrimeLevel);
            handlers[(int)MorrowindScriptOpcode.SetPCCrimeLevel] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(SetPCCrimeLevel);
            handlers[(int)MorrowindScriptOpcode.HasSoulGem] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(HasSoulGem);
            handlers[(int)MorrowindScriptOpcode.GetPCSleep] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetPCSleep);
            handlers[(int)MorrowindScriptOpcode.WakeUpPC] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(WakeUpPC);
            handlers[(int)MorrowindScriptOpcode.SetMovementFlag] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(SetMovementFlag);
            handlers[(int)MorrowindScriptOpcode.PlaceAtPC] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(PlaceAtPC);
            handlers[(int)MorrowindScriptOpcode.StartScript] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(StartScript);
            handlers[(int)MorrowindScriptOpcode.GetHealth] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetHealth);
            handlers[(int)MorrowindScriptOpcode.GetCurrentAiPackage] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetCurrentAiPackage);
            handlers[(int)MorrowindScriptOpcode.GetCommonDisease] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetCommonDisease);
            handlers[(int)MorrowindScriptOpcode.GetBlightDisease] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetBlightDisease);
            handlers[(int)MorrowindScriptOpcode.AddSpell] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(AddSpell);
            handlers[(int)MorrowindScriptOpcode.RemoveSpell] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(RemoveSpell);
            handlers[(int)MorrowindScriptOpcode.Say] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(Say);
            handlers[(int)MorrowindScriptOpcode.GetDistance] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetDistance);
            handlers[(int)MorrowindScriptOpcode.Cast] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(Cast);
            handlers[(int)MorrowindScriptOpcode.ForceGreeting] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(ForceGreeting);
            handlers[(int)MorrowindScriptOpcode.ModPlayerReputation] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(ModPlayerReputation);
            handlers[(int)MorrowindScriptOpcode.GetPos] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetPos);
            handlers[(int)MorrowindScriptOpcode.GetTarget] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetTarget);
            handlers[(int)MorrowindScriptOpcode.ShowMap] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(ShowMap);
            handlers[(int)MorrowindScriptOpcode.PlayerFactionMutation] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(PlayerFactionMutation);
            handlers[(int)MorrowindScriptOpcode.ActorRaiseRank] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(ActorRaiseRank);
            handlers[(int)MorrowindScriptOpcode.PlayerSkillMutation] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(PlayerSkillMutation);
            handlers[(int)MorrowindScriptOpcode.GetPlayerSkill] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetPlayerSkill);
            handlers[(int)MorrowindScriptOpcode.ActorAttributeMutation] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(ActorAttributeMutation);
            handlers[(int)MorrowindScriptOpcode.ScreenFade] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(ScreenFade);
            handlers[(int)MorrowindScriptOpcode.ShellControl] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(ShellControl);
            handlers[(int)MorrowindScriptOpcode.Random] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(Random);
            handlers[(int)MorrowindScriptOpcode.GetSpell] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetSpell);
            handlers[(int)MorrowindScriptOpcode.GetCurrentWeather] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetCurrentWeather);
            handlers[(int)MorrowindScriptOpcode.ScriptRunning] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(ScriptRunning);
            handlers[(int)MorrowindScriptOpcode.SetPos] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(SetPos);
            handlers[(int)MorrowindScriptOpcode.MoveWorld] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(MoveWorld);
            handlers[(int)MorrowindScriptOpcode.Move] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(Move);
            handlers[(int)MorrowindScriptOpcode.PCExpelled] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(PCExpelled);
            handlers[(int)MorrowindScriptOpcode.SetAtStart] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(SetAtStart);
            handlers[(int)MorrowindScriptOpcode.GetStartingAngle] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetStartingAngle);
            handlers[(int)MorrowindScriptOpcode.GetLocked] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetLocked);
            handlers[(int)MorrowindScriptOpcode.RequestLockState] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(RequestLockState);
            handlers[(int)MorrowindScriptOpcode.Drop] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(Drop);
            handlers[(int)MorrowindScriptOpcode.GetRace] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetRace);
            handlers[(int)MorrowindScriptOpcode.ChangeWeather] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(ChangeWeather);
            handlers[(int)MorrowindScriptOpcode.StreamMusic] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(StreamMusic);
            handlers[(int)MorrowindScriptOpcode.SayDone] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(SayDone);
            handlers[(int)MorrowindScriptOpcode.GetActorAiSetting] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetActorAiSetting);
            handlers[(int)MorrowindScriptOpcode.GetButtonPressed] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetButtonPressed);
            handlers[(int)MorrowindScriptOpcode.GetActorAttribute] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetActorAttribute);
            handlers[(int)MorrowindScriptOpcode.GetEffect] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetEffect);
            handlers[(int)MorrowindScriptOpcode.GetSpellEffects] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetSpellEffects);
            handlers[(int)MorrowindScriptOpcode.PayFine] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(PayFine);
            handlers[(int)MorrowindScriptOpcode.OnActivateStatement] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(OnActivateStatement);
            handlers[(int)MorrowindScriptOpcode.HurtStandingActor] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(HurtStandingActor);
            handlers[(int)MorrowindScriptOpcode.ShowRestMenu] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(ShowRestMenu);
            handlers[(int)MorrowindScriptOpcode.GetStandingPC] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetStandingPC);
            handlers[(int)MorrowindScriptOpcode.PlayAnimationGroup] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(PlayAnimationGroup);
            handlers[(int)MorrowindScriptOpcode.GetLOS] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetLOS);
            handlers[(int)MorrowindScriptOpcode.GetDetected] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetDetected);
            handlers[(int)MorrowindScriptOpcode.GetAttacked] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetAttacked);
            handlers[(int)MorrowindScriptOpcode.OnMurder] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(OnMurder);
            handlers[(int)MorrowindScriptOpcode.OnKnockout] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(OnKnockout);
            handlers[(int)MorrowindScriptOpcode.HitOnMe] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(HitOnMe);
            handlers[(int)MorrowindScriptOpcode.GetDisposition] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetDisposition);
            handlers[(int)MorrowindScriptOpcode.ModRegion] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(ModRegion);
            handlers[(int)MorrowindScriptOpcode.PlayBink] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(PlayBink);
            handlers[(int)MorrowindScriptOpcode.ModFactionReaction] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(ModFactionReaction);
            handlers[(int)MorrowindScriptOpcode.Resurrect] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(Resurrect);
            return handlers;
        }

        [BurstCompile]
        static void Unsupported(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction) => context->Faulted = 1;

        [BurstCompile]
        static void Nop(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction) { }

        [BurstCompile]
        static void Return(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction) => context->Halted = 1;

        [BurstCompile]
        static void StopScript(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (instruction->Operand0 != 0)
            {
                if (context->StopScriptRuntimeEntity == Entity.Null || instruction->Int0 < 0)
                {
                    context->Faulted = 1;
                    return;
                }

                context->Ecb.AppendToBuffer(context->SortKey, context->StopScriptRuntimeEntity, new MorrowindScriptStopRequest
                {
                    ProgramIndex = instruction->Int0,
                });
                context->Halted = 1;
                return;
            }

            context->StopRequested = 1;
            context->Halted = 1;
        }

        [BurstCompile]
        static void StartScript(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (context->StartScriptRuntimeEntity == Entity.Null || instruction->Int0 <= 0 || instruction->Int1 < 0)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->StartScriptRuntimeEntity, new MorrowindScriptStartRequest
            {
                Program = new MorrowindScriptProgramDefHandle { Value = instruction->Int0 },
                ProgramIndex = instruction->Int1,
                TargetEntity = context->Entity,
                TargetPlacedRefId = context->PlacedRefId,
            });
        }

        [BurstCompile]
        static void PushInt(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = instruction->Int0,
                FloatValue = instruction->Int0,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void PushFloat(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = (int)instruction->Float0,
                FloatValue = instruction->Float0,
                ValueKind = (byte)MorrowindScriptValueKind.Float,
            });
        }

        [BurstCompile]
        static void GetLocal(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            int index = instruction->Int0;
            if ((uint)index >= (uint)context->LocalCount)
            {
                context->Faulted = 1;
                return;
            }

            var local = context->Locals[index];
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = local.IntValue,
                FloatValue = local.FloatValue,
                ValueKind = local.ValueKind,
            });
        }

        [BurstCompile]
        static void SetLocalInt(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!Pop(context, out var value) || !TryGetLocal(context, instruction->Int0, out var local))
                return;

            local.IntValue = value.ValueKind == (byte)MorrowindScriptValueKind.Float ? (int)value.FloatValue : value.IntValue;
            local.FloatValue = local.IntValue;
            local.ValueKind = (byte)MorrowindScriptValueKind.Integer;
            context->Locals[instruction->Int0] = local;
        }

        [BurstCompile]
        static void SetLocalFloat(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!Pop(context, out var value) || !TryGetLocal(context, instruction->Int0, out var local))
                return;

            local.FloatValue = ToFloat(value);
            local.IntValue = (int)local.FloatValue;
            local.ValueKind = (byte)MorrowindScriptValueKind.Float;
            context->Locals[instruction->Int0] = local;
        }

        [BurstCompile]
        static void GetGlobal(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            int index = instruction->Int0;
            if ((uint)index >= (uint)context->GlobalCount)
            {
                context->Faulted = 1;
                return;
            }

            var global = context->Globals[index];
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = global.IntValue,
                FloatValue = global.FloatValue,
                ValueKind = global.ValueKind,
            });
        }

        [BurstCompile]
        static void SetGlobalInt(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!Pop(context, out var value) || !TryGetGlobal(context, instruction->Int0, out var global))
                return;

            global.IntValue = value.ValueKind == (byte)MorrowindScriptValueKind.Float ? (int)value.FloatValue : value.IntValue;
            global.FloatValue = global.IntValue;
            global.ValueKind = (byte)MorrowindScriptValueKind.Integer;
            context->Globals[instruction->Int0] = global;
        }

        [BurstCompile]
        static void SetGlobalFloat(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!Pop(context, out var value) || !TryGetGlobal(context, instruction->Int0, out var global))
                return;

            global.FloatValue = ToFloat(value);
            global.IntValue = (int)global.FloatValue;
            global.ValueKind = (byte)MorrowindScriptValueKind.Float;
            context->Globals[instruction->Int0] = global;
        }

        [BurstCompile]
        static void Add(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction) => BinaryMath(context, 0);

        [BurstCompile]
        static void Subtract(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction) => BinaryMath(context, 1);

        [BurstCompile]
        static void Multiply(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction) => BinaryMath(context, 2);

        [BurstCompile]
        static void Divide(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction) => BinaryMath(context, 3);

        [BurstCompile]
        static void CompareEqual(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction) => Compare(context, 0);

        [BurstCompile]
        static void CompareNotEqual(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction) => Compare(context, 1);

        [BurstCompile]
        static void CompareLess(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction) => Compare(context, 2);

        [BurstCompile]
        static void CompareLessOrEqual(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction) => Compare(context, 3);

        [BurstCompile]
        static void CompareGreater(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction) => Compare(context, 4);

        [BurstCompile]
        static void CompareGreaterOrEqual(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction) => Compare(context, 5);

        [BurstCompile]
        static void Jump(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction) => context->ProgramCounter += instruction->Int0;

        [BurstCompile]
        static void JumpIfZero(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!Pop(context, out var value))
                return;

            if (math.abs(ToFloat(value)) <= 0.000001f)
                context->ProgramCounter += instruction->Int0;
        }

        [BurstCompile]
        static void EmitAudioRequest(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            var kind = (MorrowindScriptAudioKind)instruction->Operand0;
            Entity requestEntity = context->Ecb.CreateEntity(context->SortKey);
            context->Ecb.AddComponent(context->SortKey, requestEntity, new MorrowindScriptAudioRequest
            {
                Sequence = context->AudioSequenceBase + (uint)context->SortKey + 1u,
                Sound = new SoundDefHandle { Value = instruction->Int0 },
                SourceEntity = context->Entity,
                SourcePlacedRefId = context->PlacedRefId,
                Position = context->Position,
                Volume = instruction->Float0 <= 0f ? 1f : instruction->Float0,
                Pitch = instruction->Float1 <= 0f ? 1f : instruction->Float1,
                Kind = instruction->Operand0,
                Spatial = (byte)(kind != MorrowindScriptAudioKind.PlaySound && kind != MorrowindScriptAudioKind.PlaySoundVP && kind != MorrowindScriptAudioKind.StopSound ? 1 : 0),
                Looping = (byte)(kind == MorrowindScriptAudioKind.PlayLoopSound3D || kind == MorrowindScriptAudioKind.PlayLoopSound3DVP ? 1 : 0),
            });
        }

        [BurstCompile]
        static void GetDistancePlayer(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (context->HasPlayerPosition == 0)
            {
                context->Faulted = 1;
                return;
            }

            float distanceMw = math.distance(context->Position, context->PlayerPosition) / WorldScale.MwUnitsToMeters;
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = (int)distanceMw,
                FloatValue = distanceMw,
                ValueKind = (byte)MorrowindScriptValueKind.Float,
            });
        }

        [BurstCompile]
        static void GetDistance(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveDistancePosition(
                    context,
                    instruction->Operand0,
                    instruction->Int0,
                    out float3 sourcePosition)
                || !TryResolveDistancePosition(
                    context,
                    (byte)instruction->Operand1,
                    instruction->Int1,
                    out float3 targetPosition))
            {
                return;
            }

            float distanceMw = math.distance(sourcePosition, targetPosition) / WorldScale.MwUnitsToMeters;
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = (int)distanceMw,
                FloatValue = distanceMw,
                ValueKind = (byte)MorrowindScriptValueKind.Float,
            });
        }

        [BurstCompile]
        static void GetPos(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveDistancePosition(context, instruction->Operand0, instruction->Int0, out float3 position))
                return;

            float valueMw;
            switch (instruction->Operand1)
            {
                case 0:
                    valueMw = position.x / WorldScale.MwUnitsToMeters;
                    break;
                case 1:
                    valueMw = position.z / WorldScale.MwUnitsToMeters;
                    break;
                case 2:
                    valueMw = position.y / WorldScale.MwUnitsToMeters;
                    break;
                default:
                    context->Faulted = 1;
                    return;
            }

            Push(context, new MorrowindScriptStackValue
            {
                IntValue = (int)valueMw,
                FloatValue = valueMw,
                ValueKind = (byte)MorrowindScriptValueKind.Float,
            });
        }

        [BurstCompile]
        static void GetTarget(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveActorTarget(context, instruction->Operand0, instruction->Int0, out uint actorPlacedRefId, out Entity actorEntity)
                || !TryResolveActorTarget(context, (byte)instruction->Operand1, instruction->Int1, out uint targetPlacedRefId, out Entity targetEntity))
            {
                return;
            }

            if (actorPlacedRefId != 0u && !IsLoadedPlacedRef(context, actorPlacedRefId))
            {
                context->Faulted = 1;
                return;
            }

            if ((context->ActorCombatTargets == null && context->ActorCombatTargetCount > 0)
                || context->ActorCombatTargetCount < 0)
            {
                context->Faulted = 1;
                return;
            }

            byte matched = 0;
            for (int i = 0; i < context->ActorCombatTargetCount; i++)
            {
                var snapshot = context->ActorCombatTargets[i];
                bool actorMatches = actorEntity != Entity.Null
                    ? snapshot.ActorEntity == actorEntity
                    : actorPlacedRefId != 0u && snapshot.ActorPlacedRefId == actorPlacedRefId;
                if (!actorMatches)
                    continue;

                if (snapshot.Active == 0)
                    break;

                bool targetMatches = targetEntity != Entity.Null
                    ? snapshot.TargetEntity == targetEntity
                    : targetPlacedRefId != 0u && snapshot.TargetPlacedRefId == targetPlacedRefId;
                matched = targetMatches ? (byte)1 : (byte)0;
                break;
            }

            Push(context, new MorrowindScriptStackValue
            {
                IntValue = matched,
                FloatValue = matched,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void GetCellChanged(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (context->HasCellChanged == 0)
            {
                context->Faulted = 1;
                return;
            }

            Push(context, new MorrowindScriptStackValue
            {
                IntValue = context->CellChanged,
                FloatValue = context->CellChanged,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void GetSoundPlaying(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            ulong key = BuildScriptLoopKey(context->PlacedRefId, context->Entity, instruction->Int0);
            bool isPlaying = false;
            for (int i = 0; i < context->PlayingScriptSoundKeyCount; i++)
            {
                if (context->PlayingScriptSoundKeys[i] != key)
                    continue;

                isPlaying = true;
                break;
            }

            Push(context, new MorrowindScriptStackValue
            {
                IntValue = isPlaying ? 1 : 0,
                FloatValue = isPlaying ? 1f : 0f,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void GetMenuMode(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (context->HasMenuMode == 0)
            {
                context->Faulted = 1;
                return;
            }

            Push(context, new MorrowindScriptStackValue
            {
                IntValue = context->MenuMode,
                FloatValue = context->MenuMode,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void GetButtonPressed(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            int value = context->HasModalButtonPressed != 0 ? context->ModalButtonPressed : -1;
            if (context->ModalButtonPressedRead != null)
                *context->ModalButtonPressedRead = 1;

            Push(context, new MorrowindScriptStackValue
            {
                IntValue = value,
                FloatValue = value,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void GetPCSleep(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (context->HasPlayerSleeping == 0)
            {
                context->Faulted = 1;
                return;
            }

            int sleeping = context->PlayerSleeping != 0 ? 1 : 0;
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = sleeping,
                FloatValue = sleeping,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void WakeUpPC(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (context->ShellRuntimeEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->ShellRuntimeEntity, new MorrowindScriptShellRequest
            {
                Operation = (byte)MorrowindScriptShellRequestOperation.WakeUpPlayer,
            });
        }

        [BurstCompile]
        static void ShowRestMenu(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (context->ShellRuntimeEntity == Entity.Null
                || !TryResolveRefTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity)
                || (targetEntity == Entity.Null && targetPlacedRefId == 0u))
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->ShellRuntimeEntity, new MorrowindScriptShellRequest
            {
                Operation = (byte)MorrowindScriptShellRequestOperation.ShowRestMenu,
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
            });
        }

        [BurstCompile]
        static void ScreenFade(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (context->ShellRuntimeEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->ShellRuntimeEntity, new MorrowindScriptShellRequest
            {
                Operation = (byte)MorrowindScriptShellRequestOperation.ScreenFade,
                FadeOut = instruction->Operand0,
                Duration = instruction->Float0,
            });
        }

        [BurstCompile]
        static void ShellControl(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (context->ShellRuntimeEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->ShellRuntimeEntity, new MorrowindScriptShellRequest
            {
                Operation = instruction->Operand0,
                Enabled = (byte)instruction->Int0,
                MenuKind = (byte)instruction->Int1,
            });
        }

        [BurstCompile]
        static void GetPCCell(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            int prefixLength = instruction->Int0;
            if (context->HasPlayerCellName == 0
                || prefixLength < 0
                || prefixLength > context->PlayerCellName.Length)
            {
                Push(context, new MorrowindScriptStackValue
                {
                    ValueKind = (byte)MorrowindScriptValueKind.Integer,
                });
                return;
            }

            ulong expected = ((ulong)(uint)instruction->Int2 << 32) | (uint)instruction->Int1;
            ulong actual = HashPrefix(context->PlayerCellName, prefixLength);
            int result = actual == expected ? 1 : 0;
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = result,
                FloatValue = result,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void GetDeadCount(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            int actorIndex = instruction->Int0;
            if (context->DeathCounts == null || (uint)actorIndex >= (uint)context->DeathCountCount)
            {
                context->Faulted = 1;
                return;
            }

            int count = context->DeathCounts[actorIndex].Count;
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = count,
                FloatValue = count,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void GetJournalIndex(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            int index = instruction->Int0;
            if (context->QuestJournal == null || (uint)index >= (uint)context->QuestJournalCount)
            {
                context->Faulted = 1;
                return;
            }

            int journalIndex = context->QuestJournal[index].Index;
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = journalIndex,
                FloatValue = journalIndex,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void Journal(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            int index = instruction->Int0;
            if (context->QuestJournal == null || (uint)index >= (uint)context->QuestJournalCount)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->QuestJournalRuntimeEntity, new MorrowindQuestJournalRequest
            {
                DialogueIndex = index,
                JournalIndex = instruction->Int1,
                InfoIndex = instruction->Int2,
                QuestStatus = instruction->Operand0,
                Operation = (byte)MorrowindQuestJournalRequestOperation.Journal,
            });
        }

        [BurstCompile]
        static void SetJournalIndex(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            int index = instruction->Int0;
            if (context->QuestJournal == null || (uint)index >= (uint)context->QuestJournalCount)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->QuestJournalRuntimeEntity, new MorrowindQuestJournalRequest
            {
                DialogueIndex = index,
                JournalIndex = instruction->Int1,
                InfoIndex = -1,
                QuestStatus = 0,
                Operation = (byte)MorrowindQuestJournalRequestOperation.SetIndex,
            });
        }

        [BurstCompile]
        static void AddTopic(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            context->Ecb.AppendToBuffer(context->SortKey, context->DialogueRuntimeEntity, new MorrowindDialogueRequest
            {
                DialogueIndex = instruction->Int0,
                Operation = (byte)MorrowindDialogueRequestOperation.AddTopic,
            });
        }

        [BurstCompile]
        static void FillJournal(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            context->Ecb.AppendToBuffer(context->SortKey, context->DialogueRuntimeEntity, new MorrowindDialogueRequest
            {
                DialogueIndex = -1,
                Operation = (byte)MorrowindDialogueRequestOperation.FillJournal,
            });
        }

        [BurstCompile]
        static void GetSecondsPassed(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = (int)context->SecondsPassed,
                FloatValue = context->SecondsPassed,
                ValueKind = (byte)MorrowindScriptValueKind.Float,
            });
        }

        [BurstCompile]
        static void GetCurrentWeather(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (context->HasCurrentWeather == 0)
            {
                context->Faulted = 1;
                return;
            }

            Push(context, new MorrowindScriptStackValue
            {
                IntValue = context->CurrentWeather,
                FloatValue = context->CurrentWeather,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void ChangeWeather(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (context->WeatherRuntimeEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->WeatherRuntimeEntity, new MorrowindWeatherChangeRequest
            {
                RegionHandleValue = instruction->Int0,
                Weather = instruction->Int1,
            });
        }

        [BurstCompile]
        static void ModRegion(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (context->WeatherRuntimeEntity == Entity.Null || instruction->Int0 <= 0)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->WeatherRuntimeEntity, new MorrowindRegionWeatherOverrideRequest
            {
                RegionHandleValue = instruction->Int0,
                ClearChance = UnpackChance(instruction->Int1, 0),
                CloudyChance = UnpackChance(instruction->Int1, 1),
                FoggyChance = UnpackChance(instruction->Int1, 2),
                OvercastChance = UnpackChance(instruction->Int1, 3),
                RainChance = UnpackChance(instruction->Int2, 0),
                ThunderChance = UnpackChance(instruction->Int2, 1),
                AshChance = UnpackChance(instruction->Int2, 2),
                BlightChance = UnpackChance(instruction->Int2, 3),
                SnowChance = instruction->Operand0,
                BlizzardChance = (byte)instruction->Operand1,
            });
        }

        static byte UnpackChance(int packed, int index)
            => (byte)((packed >> (index * 8)) & 0xFF);

        [BurstCompile]
        static void PlayBink(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (context->ShellRuntimeEntity == Entity.Null
                || context->Messages == null
                || (uint)instruction->Int0 >= (uint)context->MessageCount)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->ShellRuntimeEntity, new MorrowindScriptShellRequest
            {
                Operation = (byte)MorrowindScriptShellRequestOperation.PlayBink,
                MovieName = new FixedString128Bytes(context->Messages[instruction->Int0]),
                AllowSkipping = instruction->Int1 != 0 ? (byte)1 : (byte)0,
            });
        }

        [BurstCompile]
        static void ModFactionReaction(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (context->PlayerFactionRuntimeEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->PlayerFactionRuntimeEntity, new MorrowindScriptFactionReactionRequest
            {
                SourceFactionIndex = instruction->Int0,
                TargetFactionIndex = instruction->Int1,
                Value = instruction->Int2,
                IsMod = instruction->Operand0,
            });
        }

        [BurstCompile]
        static void StreamMusic(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (context->MusicRuntimeEntity == Entity.Null
                || (instruction->Int0 <= 0
                    && ((context->Messages == null && context->MessageCount > 0)
                        || (uint)instruction->Int1 >= (uint)context->MessageCount)))
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->MusicRuntimeEntity, new MorrowindMusicRequest
            {
                Track = new MusicTrackDefHandle { Value = instruction->Int0 },
                DirectPath = instruction->Int0 > 0 ? default : context->Messages[instruction->Int1],
            });
        }

        [BurstCompile]
        static void GetRace(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveActorTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity))
                return;

            if ((context->ActorIdentities == null && context->ActorIdentityCount > 0)
                || context->ActorIdentityCount < 0)
            {
                context->Faulted = 1;
                return;
            }

            ulong expectedRaceHash = ((ulong)(uint)instruction->Int2 << 32) | (uint)instruction->Int1;
            for (int i = 0; i < context->ActorIdentityCount; i++)
            {
                var identity = context->ActorIdentities[i];
                bool matches = targetEntity != Entity.Null
                    ? identity.ActorEntity == targetEntity
                    : targetPlacedRefId != 0u && identity.PlacedRefId == targetPlacedRefId;
                if (!matches)
                    continue;

                ulong actualRaceHash = HashFixedString64(identity.RaceName);
                int result = actualRaceHash == expectedRaceHash ? 1 : 0;
                Push(context, new MorrowindScriptStackValue
                {
                    IntValue = result,
                    FloatValue = result,
                    ValueKind = (byte)MorrowindScriptValueKind.Integer,
                });
                return;
            }

            context->Faulted = 1;
        }

        [BurstCompile]
        static void ScriptRunning(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if ((context->RunningPrograms == null && context->RunningProgramCount > 0) || context->RunningProgramCount < 0)
            {
                context->Faulted = 1;
                return;
            }

            int running = 0;
            for (int i = 0; i < context->RunningProgramCount; i++)
            {
                var snapshot = context->RunningPrograms[i];
                if (snapshot.ProgramIndex == instruction->Int0 && snapshot.Running != 0)
                {
                    running = 1;
                    break;
                }
            }

            Push(context, new MorrowindScriptStackValue
            {
                IntValue = running,
                FloatValue = running,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void Random(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (context->RandomState == null || !Pop(context, out var limitValue))
            {
                context->Faulted = 1;
                return;
            }

            int limit = limitValue.ValueKind == (byte)MorrowindScriptValueKind.Float
                ? (int)limitValue.FloatValue
                : limitValue.IntValue;
            if (limit < 0)
            {
                context->Faulted = 1;
                return;
            }

            int result = RollScriptRandom(context->RandomState, limit);
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = result,
                FloatValue = result,
                ValueKind = (byte)MorrowindScriptValueKind.Float,
            });
        }

        [BurstCompile]
        static void Negate(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!Pop(context, out var value))
                return;

            bool isFloat = value.ValueKind == (byte)MorrowindScriptValueKind.Float;
            int intValue = isFloat ? (int)-value.FloatValue : -value.IntValue;
            float floatValue = isFloat ? -value.FloatValue : -value.IntValue;
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = intValue,
                FloatValue = floatValue,
                ValueKind = value.ValueKind,
            });
        }

        [BurstCompile]
        static void GetOnActivate(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            context->ObservedOnActivate = 1;
            byte activated = IsCurrentActivationTarget(context) ? (byte)1 : (byte)0;
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = activated,
                FloatValue = activated,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void OnActivateStatement(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            context->ObservedOnActivate = 1;
            IsCurrentActivationTarget(context);
        }

        [BurstCompile]
        static void Activate(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!IsCurrentActivationTarget(context))
                return;

            var activationEvent = context->ActivationEvents[context->MatchedActivationEventIndex];
            context->Ecb.AppendToBuffer(context->SortKey, context->InteractionRuntimeEntity, new ScriptDefaultActivationRequest
            {
                TargetEntity = context->Entity,
                TargetPlacedRefId = context->PlacedRefId,
                Sequence = activationEvent.Sequence,
                Kind = activationEvent.Kind,
            });
        }

        [BurstCompile]
        static void Rotate(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            float radians = math.radians(instruction->Float0 * context->SecondsPassed);
            EmitTransformRequest(context, instruction, radians, instruction->Int1 != 0 ? (byte)7 : (byte)0);
        }

        [BurstCompile]
        static void SetAngle(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!Pop(context, out var angle))
                return;

            float radians = math.radians(ToFloat(angle));
            EmitTransformRequest(context, instruction, radians, 1);
        }

        [BurstCompile]
        static void SetAtStart(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveRefTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity))
                return;

            if (targetPlacedRefId == 0u || context->TransformRuntimeEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->TransformRuntimeEntity, new MorrowindScriptTransformRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                Operation = 6,
            });
        }

        [BurstCompile]
        static void PositionCell(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveRefTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity))
                return;

            ulong interiorCellHash = ((ulong)(uint)instruction->Int2 << 32) | (uint)instruction->Int1;
            if (interiorCellHash == 0UL || context->TransformRuntimeEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->TransformRuntimeEntity, new MorrowindScriptTransformRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                Position = new float3(instruction->Float0, instruction->Float1, instruction->Float2),
                Radians = instruction->Float3,
                InteriorCellHash = interiorCellHash,
                Operation = 2,
            });
        }

        [BurstCompile]
        static void Position(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!Pop(context, out var zRotMinutes))
                return;

            if (!TryResolveRefTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity))
                return;

            if (targetPlacedRefId == 0u || context->TransformRuntimeEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->TransformRuntimeEntity, new MorrowindScriptTransformRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                Position = new float3(instruction->Float0, instruction->Float1, instruction->Float2),
                Radians = math.radians(ToFloat(zRotMinutes) / 60f),
                Operation = 3,
            });
        }

        [BurstCompile]
        static void SetPos(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!Pop(context, out var value))
                return;

            if (!TryResolveRefTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity)
                || !TryResolveDistancePosition(context, instruction->Operand0, instruction->Int0, out float3 position))
            {
                return;
            }

            if (targetPlacedRefId == 0u || context->TransformRuntimeEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            float meters = ToFloat(value) * WorldScale.MwUnitsToMeters;
            switch (instruction->Operand1)
            {
                case 0:
                    position.x = meters;
                    break;
                case 1:
                    position.z = meters;
                    break;
                case 2:
                    position.y = meters;
                    break;
                default:
                    context->Faulted = 1;
                    return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->TransformRuntimeEntity, new MorrowindScriptTransformRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                Position = position,
                Operation = 4,
            });
        }

        [BurstCompile]
        static void MoveWorld(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!Pop(context, out var speed))
                return;

            if (!TryResolveRefTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity)
                || !TryResolveTargetTransform(context, instruction->Operand0, instruction->Int0, out float3 position, out _))
            {
                return;
            }

            if (targetPlacedRefId == 0u || context->TransformRuntimeEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            float delta = ToFloat(speed) * context->SecondsPassed * WorldScale.MwUnitsToMeters;
            position += AxisDelta((byte)instruction->Operand1, delta);
            context->Ecb.AppendToBuffer(context->SortKey, context->TransformRuntimeEntity, new MorrowindScriptTransformRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                Position = position,
                Operation = 5,
            });
        }

        [BurstCompile]
        static void Move(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!Pop(context, out var speed))
                return;

            if (!TryResolveRefTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity)
                || !TryResolveTargetTransform(context, instruction->Operand0, instruction->Int0, out float3 position, out quaternion rotation))
            {
                return;
            }

            if (targetPlacedRefId == 0u || context->TransformRuntimeEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            float delta = ToFloat(speed) * context->SecondsPassed * WorldScale.MwUnitsToMeters;
            position += math.mul(rotation, AxisDelta((byte)instruction->Operand1, delta));
            context->Ecb.AppendToBuffer(context->SortKey, context->TransformRuntimeEntity, new MorrowindScriptTransformRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                Position = position,
                Operation = 5,
            });
        }

        [BurstCompile]
        static void AiWander(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveRefTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity))
                return;

            if (targetPlacedRefId == 0u || context->AiRuntimeEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->AiRuntimeEntity, new MorrowindScriptAiPackageRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                PackageType = (byte)MorrowindScriptAiPackageRequestType.Wander,
                ShouldRepeat = instruction->Operand1 != 0 ? (byte)1 : (byte)0,
                WanderRadius = math.max(0f, instruction->Float0),
                IdleSeconds = 1.5f,
            });
        }

        [BurstCompile]
        static void AiTravel(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveRefTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity))
                return;

            if (targetPlacedRefId == 0u || context->AiRuntimeEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->AiRuntimeEntity, new MorrowindScriptAiPackageRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                PackageType = (byte)MorrowindScriptAiPackageRequestType.Travel,
                ShouldRepeat = instruction->Operand1 != 0 ? (byte)1 : (byte)0,
                TargetPosition = new float3(instruction->Float0, instruction->Float1, instruction->Float2),
                IdleSeconds = 0.5f,
            });
        }

        [BurstCompile]
        static void AiFollow(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            EmitFollowRequest(context, instruction, destinationInteriorCellHash: 0UL);
        }

        [BurstCompile]
        static void AiFollowCell(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            ulong interiorCellHash = ((ulong)(uint)instruction->Int2 << 32) | (uint)instruction->Int1;
            if (interiorCellHash == 0UL)
            {
                context->Faulted = 1;
                return;
            }

            EmitFollowRequest(context, instruction, interiorCellHash);
        }

        [BurstCompile]
        static void StopCombat(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveRefTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity))
                return;

            if (targetPlacedRefId == 0u || context->AiRuntimeEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->AiRuntimeEntity, new MorrowindScriptAiPackageRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                PackageType = (byte)MorrowindScriptAiPackageRequestType.StopCombat,
            });
        }

        [BurstCompile]
        static void StartCombat(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveRefTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity))
                return;

            if (!TryResolveActorTarget(context, (byte)instruction->Operand1, instruction->Int1, out uint combatTargetPlacedRefId, out Entity combatTargetEntity))
                return;

            if (targetPlacedRefId == 0u || context->AiRuntimeEntity == Entity.Null || combatTargetEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->AiRuntimeEntity, new MorrowindScriptAiPackageRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                FollowTargetEntity = combatTargetEntity,
                FollowTargetPlacedRefId = combatTargetPlacedRefId,
                PackageType = (byte)MorrowindScriptAiPackageRequestType.StartCombat,
            });
        }

        [BurstCompile]
        static void SetActorAiSetting(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveRefTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity))
                return;

            if (targetPlacedRefId == 0u || context->AiRuntimeEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->AiRuntimeEntity, new MorrowindScriptActorAiSettingRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                Value = instruction->Int1,
                Kind = (byte)instruction->Operand1,
                IsMod = instruction->Int2 != 0 ? (byte)1 : (byte)0,
            });
        }

        [BurstCompile]
        static void GetActorAiSetting(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveActorTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity))
                return;

            if ((context->ActorAiSettings == null && context->ActorAiSettingCount > 0)
                || context->ActorAiSettingCount < 0)
            {
                context->Faulted = 1;
                return;
            }

            int matchCount = 0;
            int value = 0;
            for (int i = 0; i < context->ActorAiSettingCount; i++)
            {
                var snapshot = context->ActorAiSettings[i];
                bool targetMatches = targetPlacedRefId != 0u
                    ? snapshot.PlacedRefId == targetPlacedRefId
                    : targetEntity != Entity.Null
                        && snapshot.ActorEntity.Index == targetEntity.Index
                        && snapshot.ActorEntity.Version == targetEntity.Version;
                if (!targetMatches)
                    continue;

                matchCount++;
                value = ((MorrowindScriptActorAiSettingKind)instruction->Operand1) switch
                {
                    MorrowindScriptActorAiSettingKind.Hello => snapshot.Hello,
                    MorrowindScriptActorAiSettingKind.Fight => snapshot.Fight,
                    MorrowindScriptActorAiSettingKind.Flee => snapshot.Flee,
                    MorrowindScriptActorAiSettingKind.Alarm => snapshot.Alarm,
                    _ => value,
                };
            }

            if (matchCount != 1 || instruction->Operand1 < 1 || instruction->Operand1 > 4)
            {
                context->Faulted = 1;
                return;
            }

            Push(context, new MorrowindScriptStackValue
            {
                IntValue = value,
                FloatValue = value,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void GetDisposition(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveActorTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity))
                return;

            if ((context->ActorDispositions == null && context->ActorDispositionCount > 0)
                || context->ActorDispositionCount < 0)
            {
                context->Faulted = 1;
                return;
            }

            int matchCount = 0;
            int value = 0;
            for (int i = 0; i < context->ActorDispositionCount; i++)
            {
                var snapshot = context->ActorDispositions[i];
                bool targetMatches = targetPlacedRefId != 0u
                    ? snapshot.PlacedRefId == targetPlacedRefId
                    : targetEntity != Entity.Null
                        && snapshot.ActorEntity.Index == targetEntity.Index
                        && snapshot.ActorEntity.Version == targetEntity.Version;
                if (!targetMatches)
                    continue;

                matchCount++;
                value = snapshot.BaseDisposition;
            }

            if (matchCount != 1)
            {
                context->Faulted = 1;
                return;
            }

            Push(context, new MorrowindScriptStackValue
            {
                IntValue = value,
                FloatValue = value,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void SetMovementFlag(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveActorTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity))
                return;

            if (context->MovementRuntimeEntity == Entity.Null
                || instruction->Operand1 != (byte)MorrowindScriptMovementFlagKind.ForceSneak)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->MovementRuntimeEntity, new MorrowindScriptMovementFlagRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                FlagKind = (byte)instruction->Operand1,
                Enabled = instruction->Int1 != 0 ? (byte)1 : (byte)0,
            });
        }

        [BurstCompile]
        static void PlaceAtPC(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (context->PlaceAtRuntimeEntity == Entity.Null
                || instruction->Int1 < 0
                || instruction->Int2 < 0
                || instruction->Int2 > 3)
            {
                context->Faulted = 1;
                return;
            }

            var content = new ContentReference
            {
                Kind = (ContentReferenceKind)instruction->Operand0,
                HandleValue = instruction->Int0,
            };

            if (!content.IsValid)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->PlaceAtRuntimeEntity, new MorrowindScriptPlaceAtRequest
            {
                Content = content,
                Count = instruction->Int1,
                Distance = math.max(0f, instruction->Float0),
                Direction = (byte)instruction->Int2,
            });
        }

        [BurstCompile]
        static void SetDisposition(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveRefTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity))
                return;

            if (targetPlacedRefId == 0u || context->AiRuntimeEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->AiRuntimeEntity, new MorrowindScriptDispositionRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                Value = instruction->Int1,
                IsMod = instruction->Int2 != 0 ? (byte)1 : (byte)0,
            });
        }

        [BurstCompile]
        static void SetHealth(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveActorTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity))
                return;

            if (context->ActorVitalRuntimeEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            float value = instruction->Float0;
            if (instruction->Int1 != 0)
            {
                if (!Pop(context, out var stackValue))
                    return;

                value = ToFloat(stackValue);
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->ActorVitalRuntimeEntity, new MorrowindScriptActorVitalRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                Value = value,
                Kind = (byte)instruction->Operand1,
                IsMod = instruction->Int2 != 0 ? (byte)1 : (byte)0,
            });
        }

        [BurstCompile]
        static void HurtStandingActor(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!Pop(context, out var value))
                return;

            if (context->ActorVitalRuntimeEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            if (!TryResolveRefTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity))
                return;

            context->Ecb.AppendToBuffer(context->SortKey, context->ActorVitalRuntimeEntity, new MorrowindScriptHurtStandingActorRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                HealthPerSecond = ToFloat(value),
            });
        }

        [BurstCompile]
        static void Resurrect(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveActorTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity))
                return;

            if (context->ActorVitalRuntimeEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->ActorVitalRuntimeEntity, new MorrowindScriptActorVitalRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                Kind = (byte)MorrowindScriptActorVitalRequestKind.Resurrect,
            });
        }

        [BurstCompile]
        static void PlayAnimationGroup(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (context->AnimationRuntimeEntity == Entity.Null
                || (context->Messages == null && context->MessageCount > 0)
                || (uint)instruction->Int1 >= (uint)context->MessageCount)
            {
                context->Faulted = 1;
                return;
            }

            if (!TryResolveRefTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity))
                return;

            if (targetEntity == Entity.Null && targetPlacedRefId == 0u)
            {
                context->Faulted = 1;
                return;
            }

            if (targetPlacedRefId != 0u && !IsLoadedPlacedRef(context, targetPlacedRefId))
            {
                context->Faulted = 1;
                return;
            }

            if (IsResolvedTargetDisabled(context, instruction->Operand0, targetPlacedRefId))
                return;

            context->Ecb.AppendToBuffer(context->SortKey, context->AnimationRuntimeEntity, new MorrowindScriptAnimationGroupRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                GroupMessageIndex = instruction->Int1,
                LoopCount = instruction->Operand1 == (short)MorrowindScriptAnimationGroupOperation.Play ? uint.MaxValue : unchecked((uint)instruction->Int2),
                Mode = (byte)instruction->Float0,
                Operation = (byte)instruction->Operand1,
            });
        }

        [BurstCompile]
        static void GetHealth(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveActorTarget(context, instruction, out uint targetPlacedRefId, out _))
                return;

            if (targetPlacedRefId == 0u && instruction->Operand0 != (byte)MorrowindScriptRefTargetMode.Player)
            {
                context->Faulted = 1;
                return;
            }

            if ((context->ActorVitals == null && context->ActorVitalCount > 0) || context->ActorVitalCount < 0)
            {
                context->Faulted = 1;
                return;
            }

            int matchCount = 0;
            float value = 0f;
            byte vitalKind = instruction->Operand1 != 0 ? (byte)instruction->Operand1 : (byte)1;
            if (instruction->Operand0 == (byte)MorrowindScriptRefTargetMode.Player)
            {
                for (int i = 0; i < context->ActorVitalCount; i++)
                {
                    var snapshot = context->ActorVitals[i];
                    if (snapshot.PlacedRefId != 0u)
                        continue;

                    matchCount++;
                    value = SelectVital(snapshot, vitalKind);
                }
            }
            else
            {
                for (int i = 0; i < context->ActorVitalCount; i++)
                {
                    var snapshot = context->ActorVitals[i];
                    if (snapshot.PlacedRefId != targetPlacedRefId)
                        continue;

                    matchCount++;
                    value = SelectVital(snapshot, vitalKind);
                }
            }

            if (matchCount != 1)
            {
                context->Faulted = 1;
                return;
            }

            Push(context, new MorrowindScriptStackValue
            {
                IntValue = (int)value,
                FloatValue = value,
                ValueKind = (byte)MorrowindScriptValueKind.Float,
            });
        }

        static float SelectVital(in MorrowindScriptActorVitalSnapshot snapshot, byte vitalKind)
            => vitalKind switch
            {
                1 => snapshot.Health,
                2 => snapshot.Magicka,
                3 => snapshot.Fatigue,
                _ => 0f,
            };

        [BurstCompile]
        static void GetCommonDisease(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
            => GetDisease(context, instruction, blight: 0);

        [BurstCompile]
        static void GetBlightDisease(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
            => GetDisease(context, instruction, blight: 1);

        [BurstCompile]
        static void AddSpell(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
            => EmitActorSpellRequest(context, instruction, remove: 0);

        [BurstCompile]
        static void RemoveSpell(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
            => EmitActorSpellRequest(context, instruction, remove: 1);

        [BurstCompile]
        static void Say(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveActorTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity))
                return;

            if (context->SayRuntimeEntity == Entity.Null
                || (context->Messages == null && context->MessageCount > 0)
                || (uint)instruction->Int1 >= (uint)context->MessageCount
                || (uint)instruction->Int2 >= (uint)context->MessageCount)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->SayRuntimeEntity, new MorrowindScriptSayRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                VoicePath = context->Messages[instruction->Int1],
                Subtitle = context->Messages[instruction->Int2],
            });
        }

        [BurstCompile]
        static void SayDone(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveActorTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity))
                return;

            if ((context->ActiveSays == null && context->ActiveSayCount > 0) || context->ActiveSayCount < 0)
            {
                context->Faulted = 1;
                return;
            }

            int done = 1;
            for (int i = 0; i < context->ActiveSayCount; i++)
            {
                var active = context->ActiveSays[i];
                bool matches = targetPlacedRefId != 0u
                    ? active.SourcePlacedRefId == targetPlacedRefId
                    : active.SourceEntity == targetEntity;
                if (!matches)
                    continue;

                done = 0;
                break;
            }

            Push(context, new MorrowindScriptStackValue
            {
                IntValue = done,
                FloatValue = done,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void Cast(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveRefTarget(context, instruction, out uint casterPlacedRefId, out Entity casterEntity))
                return;

            if (!TryResolveActorTarget(context, (byte)instruction->Operand1, instruction->Int1, out uint targetPlacedRefId, out Entity targetEntity))
                return;

            if (context->CastRuntimeEntity == Entity.Null || instruction->Int2 <= 0)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->CastRuntimeEntity, new ScriptedCastRequest
            {
                CasterEntity = casterEntity,
                CasterPlacedRefId = casterPlacedRefId,
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                Spell = new SpellDefHandle { Value = instruction->Int2 },
            });
        }

        [BurstCompile]
        static void ForceGreeting(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveActorTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity))
                return;

            if (context->ForceGreetingRuntimeEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->ForceGreetingRuntimeEntity, new ActorForceGreetingRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
            });
        }

        [BurstCompile]
        static void ModPlayerReputation(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (context->PlayerReputationRuntimeEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->PlayerReputationRuntimeEntity, new PlayerReputationMutationRequest
            {
                Delta = instruction->Int0,
            });
        }

        [BurstCompile]
        static void PlayerSkillMutation(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (context->PlayerSkillRuntimeEntity == Entity.Null
                || instruction->Operand0 == 0
                || instruction->Operand1 == 0)
            {
                context->Faulted = 1;
                return;
            }

            float value = instruction->Float0;
            if (instruction->Int0 != 0)
            {
                if (!Pop(context, out var stackValue))
                    return;

                value = ToFloat(stackValue);
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->PlayerSkillRuntimeEntity, new PlayerSkillMutationRequest
            {
                Skill = instruction->Operand0,
                Kind = (byte)instruction->Operand1,
                Value = value,
            });
        }

        [BurstCompile]
        static void ActorAttributeMutation(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (context->ActorAttributeRuntimeEntity == Entity.Null
                || instruction->Operand1 == 0
                || instruction->Int2 == 0)
            {
                context->Faulted = 1;
                return;
            }

            float value = instruction->Float0;
            if (instruction->Int1 != 0)
            {
                if (!Pop(context, out var stackValue))
                    return;

                value = ToFloat(stackValue);
            }

            if (!TryResolveActorTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity))
                return;

            context->Ecb.AppendToBuffer(context->SortKey, context->ActorAttributeRuntimeEntity, new ActorAttributeMutationRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                Attribute = (byte)instruction->Operand1,
                Kind = (byte)instruction->Int2,
                Value = value,
            });
        }

        [BurstCompile]
        static void GetPlayerSkill(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (context->HasPlayerSkills == 0 || instruction->Operand0 == 0)
            {
                context->Faulted = 1;
                return;
            }

            float value = GetSkill(context->PlayerSkills, (ActorSkillKind)instruction->Operand0);
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = (int)value,
                FloatValue = value,
                ValueKind = (byte)MorrowindScriptValueKind.Float,
            });
        }

        [BurstCompile]
        static void GetActorAttribute(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (instruction->Operand1 == 0
                || (context->ActorAttributes == null && context->ActorAttributeCount > 0)
                || context->ActorAttributeCount < 0)
            {
                context->Faulted = 1;
                return;
            }

            if (!TryResolveActorTarget(context, instruction, out uint targetPlacedRefId, out _))
                return;

            if (targetPlacedRefId == 0u && instruction->Operand0 != (byte)MorrowindScriptRefTargetMode.Player)
            {
                context->Faulted = 1;
                return;
            }

            int matchCount = 0;
            float value = 0f;
            for (int i = 0; i < context->ActorAttributeCount; i++)
            {
                var snapshot = context->ActorAttributes[i];
                if (snapshot.PlacedRefId != targetPlacedRefId)
                    continue;

                matchCount++;
                value = SelectAttribute(snapshot.Attributes, (ActorAttributeKind)instruction->Operand1);
            }

            if (matchCount != 1)
            {
                context->Faulted = 1;
                return;
            }

            Push(context, new MorrowindScriptStackValue
            {
                IntValue = (int)value,
                FloatValue = value,
                ValueKind = (byte)MorrowindScriptValueKind.Float,
            });
        }

        [BurstCompile]
        static void GetEffect(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if ((context->ActorActiveEffects == null && context->ActorActiveEffectCount > 0)
                || context->ActorActiveEffectCount < 0)
            {
                context->Faulted = 1;
                return;
            }

            if (!TryResolveActorTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity))
                return;

            int active = 0;
            short effectId = (short)instruction->Int1;
            for (int i = 0; i < context->ActorActiveEffectCount; i++)
            {
                var snapshot = context->ActorActiveEffects[i];
                bool matches = targetPlacedRefId != 0u
                    ? snapshot.PlacedRefId == targetPlacedRefId
                    : targetEntity != Entity.Null
                        && snapshot.ActorEntity.Index == targetEntity.Index
                        && snapshot.ActorEntity.Version == targetEntity.Version;
                if (matches && snapshot.EffectId == effectId)
                {
                    active = 1;
                    break;
                }
            }

            Push(context, new MorrowindScriptStackValue
            {
                IntValue = active,
                FloatValue = active,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void GetSpellEffects(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if ((context->ActorActiveEffects == null && context->ActorActiveEffectCount > 0)
                || context->ActorActiveEffectCount < 0)
            {
                context->Faulted = 1;
                return;
            }

            if (!TryResolveActorTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity))
                return;

            int active = 0;
            int spellHandleValue = instruction->Int1;
            for (int i = 0; i < context->ActorActiveEffectCount; i++)
            {
                var snapshot = context->ActorActiveEffects[i];
                bool matches = targetPlacedRefId != 0u
                    ? snapshot.PlacedRefId == targetPlacedRefId
                    : targetEntity != Entity.Null
                        && snapshot.ActorEntity.Index == targetEntity.Index
                        && snapshot.ActorEntity.Version == targetEntity.Version;
                if (matches && snapshot.SourceSpell.Value == spellHandleValue)
                {
                    active = 1;
                    break;
                }
            }

            Push(context, new MorrowindScriptStackValue
            {
                IntValue = active,
                FloatValue = active,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void PlayerFactionMutation(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (context->PlayerFactionRuntimeEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->PlayerFactionRuntimeEntity, new PlayerFactionMutationRequest
            {
                SourceEntity = context->Entity,
                SourcePlacedRefId = context->PlacedRefId,
                FactionIndex = instruction->Int0,
                Value = instruction->Int1,
                Kind = instruction->Operand0,
            });
        }

        [BurstCompile]
        static void ActorRaiseRank(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveActorTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity))
                return;

            if (context->ActorFactionRuntimeEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->ActorFactionRuntimeEntity, new ActorFactionRankMutationRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
            });
        }

        static float GetSkill(in ActorSkillSet skills, ActorSkillKind skill)
            => skill switch
            {
                ActorSkillKind.Block => skills.Block,
                ActorSkillKind.Armorer => skills.Armorer,
                ActorSkillKind.MediumArmor => skills.MediumArmor,
                ActorSkillKind.HeavyArmor => skills.HeavyArmor,
                ActorSkillKind.BluntWeapon => skills.BluntWeapon,
                ActorSkillKind.LongBlade => skills.LongBlade,
                ActorSkillKind.Axe => skills.Axe,
                ActorSkillKind.Spear => skills.Spear,
                ActorSkillKind.Athletics => skills.Athletics,
                ActorSkillKind.Enchant => skills.Enchant,
                ActorSkillKind.Destruction => skills.Destruction,
                ActorSkillKind.Alteration => skills.Alteration,
                ActorSkillKind.Illusion => skills.Illusion,
                ActorSkillKind.Conjuration => skills.Conjuration,
                ActorSkillKind.Mysticism => skills.Mysticism,
                ActorSkillKind.Restoration => skills.Restoration,
                ActorSkillKind.Alchemy => skills.Alchemy,
                ActorSkillKind.Unarmored => skills.Unarmored,
                ActorSkillKind.Security => skills.Security,
                ActorSkillKind.Sneak => skills.Sneak,
                ActorSkillKind.Acrobatics => skills.Acrobatics,
                ActorSkillKind.LightArmor => skills.LightArmor,
                ActorSkillKind.ShortBlade => skills.ShortBlade,
                ActorSkillKind.Marksman => skills.Marksman,
                ActorSkillKind.Mercantile => skills.Mercantile,
                ActorSkillKind.Speechcraft => skills.Speechcraft,
                ActorSkillKind.HandToHand => skills.HandToHand,
                _ => 0f,
            };

        static float SelectAttribute(in ActorAttributeSet attributes, ActorAttributeKind attribute)
            => attribute switch
            {
                ActorAttributeKind.Strength => attributes.Strength,
                ActorAttributeKind.Intelligence => attributes.Intelligence,
                ActorAttributeKind.Willpower => attributes.Willpower,
                ActorAttributeKind.Agility => attributes.Agility,
                ActorAttributeKind.Speed => attributes.Speed,
                ActorAttributeKind.Endurance => attributes.Endurance,
                ActorAttributeKind.Personality => attributes.Personality,
                ActorAttributeKind.Luck => attributes.Luck,
                _ => 0f,
            };

        static void EmitActorSpellRequest(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction, byte remove)
        {
            if (!TryResolveActorTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity))
                return;

            if (context->ActorSpellRuntimeEntity == Entity.Null || instruction->Int1 <= 0)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->ActorSpellRuntimeEntity, new ActorSpellMutationRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                Spell = new SpellDefHandle { Value = instruction->Int1 },
                Remove = remove,
            });
        }

        static void GetDisease(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction, byte blight)
        {
            if (!TryResolveActorTarget(context, instruction, out uint targetPlacedRefId, out _))
                return;

            if (targetPlacedRefId == 0u && instruction->Operand0 != (byte)MorrowindScriptRefTargetMode.Player)
            {
                context->Faulted = 1;
                return;
            }

            if ((context->ActorDiseases == null && context->ActorDiseaseCount > 0) || context->ActorDiseaseCount < 0)
            {
                context->Faulted = 1;
                return;
            }

            int matchCount = 0;
            int hasDisease = 0;
            for (int i = 0; i < context->ActorDiseaseCount; i++)
            {
                var snapshot = context->ActorDiseases[i];
                if (snapshot.PlacedRefId != targetPlacedRefId)
                    continue;

                matchCount++;
                hasDisease = blight != 0 ? snapshot.HasBlightDisease : snapshot.HasCommonDisease;
            }

            if (matchCount != 1)
            {
                context->Faulted = 1;
                return;
            }

            Push(context, new MorrowindScriptStackValue
            {
                IntValue = hasDisease,
                FloatValue = hasDisease,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void GetPlayerItemCount(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            PushItemCount(context, CountPlayerItems(context, (ContentReferenceKind)instruction->Operand0, instruction->Int0));
        }

        [BurstCompile]
        static void GetItemCount(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            var kind = (ContentReferenceKind)instruction->Operand1;
            int handleValue = instruction->Int1;
            if (instruction->Operand0 == (byte)MorrowindScriptRefTargetMode.Player)
            {
                PushItemCount(context, CountPlayerItems(context, kind, handleValue));
                return;
            }

            if (!TryResolveRefTarget(context, instruction, out uint targetPlacedRefId, out _))
                return;

            if (targetPlacedRefId == 0u || !IsLoadedPlacedRef(context, targetPlacedRefId))
            {
                context->Faulted = 1;
                return;
            }

            if ((context->InventoryCounts == null && context->InventoryCountCount > 0) || context->InventoryCountCount < 0)
            {
                context->Faulted = 1;
                return;
            }

            int count = 0;
            for (int i = 0; i < context->InventoryCountCount; i++)
            {
                var item = context->InventoryCounts[i];
                if (item.PlacedRefId == targetPlacedRefId
                    && item.Content.Kind == kind
                    && item.Content.HandleValue == handleValue)
                {
                    count += item.Count;
                }
            }

            PushItemCount(context, count);
        }

        static int CountPlayerItems(MorrowindScriptExecutionContext* context, ContentReferenceKind kind, int handleValue)
        {
            if ((context->PlayerInventory == null && context->PlayerInventoryCount > 0) || context->PlayerInventoryCount < 0)
            {
                context->Faulted = 1;
                return 0;
            }

            int count = 0;
            for (int i = 0; i < context->PlayerInventoryCount; i++)
            {
                var item = context->PlayerInventory[i];
                if (item.Content.Kind == kind && item.Content.HandleValue == handleValue)
                    count += item.Count;
            }

            return count;
        }

        static void PushItemCount(MorrowindScriptExecutionContext* context, int count)
        {
            if (context->Faulted != 0)
                return;

            Push(context, new MorrowindScriptStackValue
            {
                IntValue = count,
                FloatValue = count,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void HasSoulGem(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if ((context->PlayerInventory == null && context->PlayerInventoryCount > 0) || context->PlayerInventoryCount < 0)
            {
                context->Faulted = 1;
                return;
            }

            int soulActorHandleValue = instruction->Int0;
            int count = 0;
            for (int i = 0; i < context->PlayerInventoryCount; i++)
            {
                var item = context->PlayerInventory[i];
                if (item.SoulActorHandleValue == soulActorHandleValue && !item.SoulId.IsEmpty)
                    count += item.Count;
            }

            PushItemCount(context, count);
        }

        [BurstCompile]
        static void GetPlayerSpell(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if ((context->ActorKnownSpells == null && context->ActorKnownSpellCount > 0) || context->ActorKnownSpellCount < 0)
            {
                context->Faulted = 1;
                return;
            }

            int known = 0;
            int spellValue = instruction->Int0;
            for (int i = 0; i < context->ActorKnownSpellCount; i++)
            {
                if (context->ActorKnownSpells[i].Spell.Value == spellValue)
                {
                    known = 1;
                    break;
                }
            }

            Push(context, new MorrowindScriptStackValue
            {
                IntValue = known,
                FloatValue = known,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void GetSpell(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if ((context->ActorKnownSpellSnapshots == null && context->ActorKnownSpellSnapshotCount > 0)
                || context->ActorKnownSpellSnapshotCount < 0)
            {
                context->Faulted = 1;
                return;
            }

            if (!TryResolveActorTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity))
                return;

            int known = 0;
            int spellValue = instruction->Int1;
            for (int i = 0; i < context->ActorKnownSpellSnapshotCount; i++)
            {
                var snapshot = context->ActorKnownSpellSnapshots[i];
                bool targetMatches = targetPlacedRefId != 0u
                    ? snapshot.PlacedRefId == targetPlacedRefId
                    : targetEntity != Entity.Null
                        && snapshot.ActorEntity.Index == targetEntity.Index
                        && snapshot.ActorEntity.Version == targetEntity.Version;
                if (targetMatches && snapshot.Spell.Value == spellValue)
                {
                    known = 1;
                    break;
                }
            }

            Push(context, new MorrowindScriptStackValue
            {
                IntValue = known,
                FloatValue = known,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void GetPCRank(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if ((context->PlayerFactions == null && context->PlayerFactionCount > 0) || context->PlayerFactionCount < 0)
            {
                context->Faulted = 1;
                return;
            }

            int rank = -1;
            int factionIndex = instruction->Int0;
            for (int i = 0; i < context->PlayerFactionCount; i++)
            {
                var membership = context->PlayerFactions[i];
                if (membership.FactionIndex == factionIndex && membership.Joined != 0)
                {
                    rank = membership.Rank;
                    break;
                }
            }

            Push(context, new MorrowindScriptStackValue
            {
                IntValue = rank,
                FloatValue = rank,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void PCExpelled(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if ((context->PlayerFactions == null && context->PlayerFactionCount > 0) || context->PlayerFactionCount < 0)
            {
                context->Faulted = 1;
                return;
            }

            int expelled = 0;
            int factionIndex = instruction->Int0;
            for (int i = 0; i < context->PlayerFactionCount; i++)
            {
                var membership = context->PlayerFactions[i];
                if (membership.FactionIndex == factionIndex && membership.Expelled != 0)
                {
                    expelled = 1;
                    break;
                }
            }

            Push(context, new MorrowindScriptStackValue
            {
                IntValue = expelled,
                FloatValue = expelled,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void GetPCCrimeLevel(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            int bounty = math.max(0, context->PlayerCrimeLevel);
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = bounty,
                FloatValue = bounty,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void SetPCCrimeLevel(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!Pop(context, out var value))
                return;

            if (context->PlayerEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            int bounty = math.max(0, value.ValueKind == (byte)MorrowindScriptValueKind.Float ? (int)value.FloatValue : value.IntValue);
            var crime = context->PlayerCrime;
            crime.Bounty = bounty;
            if (bounty == 0)
                crime.PaidCrimeId = crime.CurrentCrimeId;

            context->Ecb.SetComponent(context->SortKey, context->PlayerEntity, new PlayerCrimeState
            {
                Bounty = crime.Bounty,
                CurrentCrimeId = crime.CurrentCrimeId,
                PaidCrimeId = crime.PaidCrimeId,
            });
            context->PlayerCrime = crime;
            context->PlayerCrimeLevel = bounty;
        }

        [BurstCompile]
        static void PayFine(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (context->PlayerEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            var crime = context->PlayerCrime;
            crime.Bounty = 0;
            crime.PaidCrimeId = crime.CurrentCrimeId;
            context->Ecb.SetComponent(context->SortKey, context->PlayerEntity, new PlayerCrimeState
            {
                Bounty = crime.Bounty,
                CurrentCrimeId = crime.CurrentCrimeId,
                PaidCrimeId = crime.PaidCrimeId,
            });
            context->PlayerCrime = crime;
            context->PlayerCrimeLevel = 0;
        }

        [BurstCompile]
        static void RequestInventoryMutation(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            int count = instruction->Int2;
            if (instruction->Float1 != 0f)
            {
                if (!Pop(context, out var stackValue))
                    return;

                count = (int)ToFloat(stackValue);
            }

            byte targetMode = instruction->Operand0;
            Entity targetEntity = Entity.Null;
            uint targetPlacedRefId = 0u;
            if (targetMode == (byte)MorrowindScriptRefTargetMode.Player)
            {
                if (context->PlayerEntity == Entity.Null)
                {
                    context->Faulted = 1;
                    return;
                }

                targetEntity = context->PlayerEntity;
            }
            else
            {
                if (!TryResolveRefTarget(context, instruction, out targetPlacedRefId, out targetEntity))
                    return;

                if (targetPlacedRefId == 0u)
                {
                    context->Faulted = 1;
                    return;
                }
            }

            if (context->QuestJournalRuntimeEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->QuestJournalRuntimeEntity, new MorrowindScriptInventoryMutationRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                Content = new ContentReference
                {
                    Kind = (ContentReferenceKind)instruction->Operand1,
                    HandleValue = instruction->Int1,
                },
                Count = count,
                SoulActorHandleValue = instruction->Int1,
                TargetMode = targetMode,
                Operation = (byte)math.max(0, (int)instruction->Float0),
            });
        }

        [BurstCompile]
        static void Drop(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            int count = instruction->Int2;
            if (instruction->Float1 != 0f)
            {
                if (!Pop(context, out var stackValue))
                    return;

                count = (int)ToFloat(stackValue);
            }

            if (count < 0 || context->InventoryDropRuntimeEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            Entity targetEntity = Entity.Null;
            uint targetPlacedRefId = 0u;
            if (instruction->Operand0 == (byte)MorrowindScriptRefTargetMode.Player)
            {
                if (context->PlayerEntity == Entity.Null)
                {
                    context->Faulted = 1;
                    return;
                }

                targetEntity = context->PlayerEntity;
            }
            else if (!TryResolveRefTarget(context, instruction, out targetPlacedRefId, out targetEntity))
            {
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->InventoryDropRuntimeEntity, new ActorInventoryDropRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                Content = new ContentReference
                {
                    Kind = (ContentReferenceKind)instruction->Operand1,
                    HandleValue = instruction->Int1,
                },
                Count = count,
            });
        }

        [BurstCompile]
        static void GetActorLocal(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryFindExternalActorLocal(context, instruction->Int0, instruction->Int1, out int snapshotIndex))
                return;

            var local = context->ExternalActorLocals[snapshotIndex].Value;
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = local.IntValue,
                FloatValue = local.FloatValue,
                ValueKind = local.ValueKind,
            });
        }

        [BurstCompile]
        static void SetActorLocalInt(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            SetActorLocal(context, instruction, (byte)MorrowindScriptValueKind.Integer);
        }

        [BurstCompile]
        static void SetActorLocalFloat(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            SetActorLocal(context, instruction, (byte)MorrowindScriptValueKind.Float);
        }

        static void SetActorLocal(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction, byte valueKind)
        {
            if (!Pop(context, out var value) || !TryFindExternalActorLocal(context, instruction->Int0, instruction->Int1, out int snapshotIndex))
                return;

            var local = new MorrowindScriptLocalValue
            {
                ValueKind = valueKind,
            };
            if (valueKind == (byte)MorrowindScriptValueKind.Float)
            {
                local.FloatValue = ToFloat(value);
                local.IntValue = (int)local.FloatValue;
            }
            else
            {
                local.IntValue = value.ValueKind == (byte)MorrowindScriptValueKind.Float ? (int)value.FloatValue : value.IntValue;
                local.FloatValue = local.IntValue;
            }

            var snapshot = context->ExternalActorLocals[snapshotIndex];
            snapshot.Value = local;
            context->ExternalActorLocals[snapshotIndex] = snapshot;

            if (context->QuestJournalRuntimeEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->QuestJournalRuntimeEntity, new MorrowindScriptActorLocalSetRequest
            {
                ActorHandleValue = instruction->Int0,
                LocalIndex = instruction->Int1,
                Value = local,
            });
        }

        static bool TryFindExternalActorLocal(
            MorrowindScriptExecutionContext* context,
            int actorHandleValue,
            int localIndex,
            out int snapshotIndex)
        {
            snapshotIndex = -1;
            if ((context->ExternalActorLocals == null && context->ExternalActorLocalCount > 0) || context->ExternalActorLocalCount < 0)
            {
                context->Faulted = 1;
                return false;
            }

            int matchCount = 0;
            for (int i = 0; i < context->ExternalActorLocalCount; i++)
            {
                var snapshot = context->ExternalActorLocals[i];
                if (snapshot.ActorHandleValue != actorHandleValue || snapshot.LocalIndex != localIndex)
                    continue;

                matchCount++;
                snapshotIndex = i;
            }

            if (matchCount == 1)
                return true;

            context->Faulted = 1;
            return false;
        }

        [BurstCompile]
        static void GetAiPackageDone(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveRefTarget(context, instruction, out uint targetPlacedRefId, out _))
                return;

            if (targetPlacedRefId == 0u
                || (context->ActorAiStatuses == null && context->ActorAiStatusCount > 0)
                || context->ActorAiStatusCount < 0)
            {
                context->Faulted = 1;
                return;
            }

            int matchCount = 0;
            byte status = 0;
            for (int i = 0; i < context->ActorAiStatusCount; i++)
            {
                var snapshot = context->ActorAiStatuses[i];
                if (snapshot.PlacedRefId != targetPlacedRefId)
                    continue;

                matchCount++;
                status = snapshot.Status;
            }

            if (matchCount != 1)
            {
                context->Faulted = 1;
                return;
            }

            int done = status == 3 || status == 4 ? 1 : 0;
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = done,
                FloatValue = done,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void GetCurrentAiPackage(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveRefTarget(context, instruction, out uint targetPlacedRefId, out _))
                return;

            if (targetPlacedRefId == 0u
                || (context->ActorAiStatuses == null && context->ActorAiStatusCount > 0)
                || context->ActorAiStatusCount < 0)
            {
                context->Faulted = 1;
                return;
            }

            int matchCount = 0;
            int packageType = -1;
            for (int i = 0; i < context->ActorAiStatusCount; i++)
            {
                var snapshot = context->ActorAiStatuses[i];
                if (snapshot.PlacedRefId != targetPlacedRefId)
                    continue;

                matchCount++;
                packageType = snapshot.CurrentPackageTypeId;
            }

            if (matchCount != 1)
            {
                context->Faulted = 1;
                return;
            }

            Push(context, new MorrowindScriptStackValue
            {
                IntValue = packageType,
                FloatValue = packageType,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void GetAngle(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveRefTarget(context, instruction, out uint targetPlacedRefId, out _))
                return;

            if (targetPlacedRefId == 0u || instruction->Operand1 < 0 || instruction->Operand1 > 2)
            {
                context->Faulted = 1;
                return;
            }

            quaternion rotation;
            if (instruction->Operand0 == (byte)MorrowindScriptRefTargetMode.Self)
            {
                rotation = context->Rotation;
            }
            else
            {
                if ((context->RefTransforms == null && context->RefTransformCount > 0) || context->RefTransformCount < 0)
                {
                    context->Faulted = 1;
                    return;
                }

                int matchCount = 0;
                rotation = quaternion.identity;
                for (int i = 0; i < context->RefTransformCount; i++)
                {
                    var snapshot = context->RefTransforms[i];
                    if (snapshot.PlacedRefId != targetPlacedRefId)
                        continue;

                    matchCount++;
                    rotation = snapshot.Rotation;
                }

                if (matchCount != 1)
                {
                    context->Faulted = 1;
                    return;
                }
            }

            float angle = math.degrees(LogicalRefRotationUtility.GetAngle(rotation, (byte)instruction->Operand1));
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = (int)angle,
                FloatValue = angle,
                ValueKind = (byte)MorrowindScriptValueKind.Float,
            });
        }

        [BurstCompile]
        static void GetStartingAngle(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveRefTarget(context, instruction, out uint targetPlacedRefId, out _))
                return;

            if (targetPlacedRefId == 0u
                || instruction->Operand1 < 0
                || instruction->Operand1 > 2
                || (context->InitialTransforms == null && context->InitialTransformCount > 0)
                || context->InitialTransformCount < 0)
            {
                context->Faulted = 1;
                return;
            }

            int matchCount = 0;
            quaternion rotation = quaternion.identity;
            for (int i = 0; i < context->InitialTransformCount; i++)
            {
                var snapshot = context->InitialTransforms[i];
                if (snapshot.PlacedRefId != targetPlacedRefId)
                    continue;

                matchCount++;
                rotation = snapshot.Rotation;
            }

            if (matchCount != 1)
            {
                context->Faulted = 1;
                return;
            }

            float angle = math.degrees(LogicalRefRotationUtility.GetAngle(rotation, (byte)instruction->Operand1));
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = (int)angle,
                FloatValue = angle,
                ValueKind = (byte)MorrowindScriptValueKind.Float,
            });
        }

        [BurstCompile]
        static void GetOnDeath(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveRefTarget(context, instruction, out uint targetPlacedRefId, out _))
                return;

            if (targetPlacedRefId == 0u
                || context->OnDeathRuntimeEntity == Entity.Null
                || (context->ActorDeaths == null && context->ActorDeathCount > 0)
                || context->ActorDeathCount < 0)
            {
                context->Faulted = 1;
                return;
            }

            int matchCount = 0;
            MorrowindScriptActorDeathSnapshot matched = default;
            for (int i = 0; i < context->ActorDeathCount; i++)
            {
                var snapshot = context->ActorDeaths[i];
                if (snapshot.PlacedRefId != targetPlacedRefId)
                    continue;

                matchCount++;
                matched = snapshot;
            }

            if (matchCount != 1 || matched.Entity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            int died = matched.Died != 0 && matched.Consumed == 0 ? 1 : 0;
            if (died != 0)
            {
                context->Ecb.AppendToBuffer(context->SortKey, context->OnDeathRuntimeEntity, new MorrowindScriptOnDeathConsumeRequest
                {
                    TargetEntity = matched.Entity,
                    TargetPlacedRefId = targetPlacedRefId,
                });
            }

            Push(context, new MorrowindScriptStackValue
            {
                IntValue = died,
                FloatValue = died,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void RequestMessageBox(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            int buttonCount = instruction->Operand0;
            int argCount = instruction->Operand1;
            if (context->MessageBoxRuntimeEntity == Entity.Null
                || (context->Messages == null && context->MessageCount > 0)
                || (uint)instruction->Int0 >= (uint)context->MessageCount
                || buttonCount < 0
                || buttonCount > 10
                || argCount < 0
                || argCount > 8
                || (buttonCount > 0 && ((uint)instruction->Int1 >= (uint)context->MessageCount
                    || instruction->Int1 + buttonCount > context->MessageCount)))
            {
                context->Faulted = 1;
                return;
            }

            var request = new ShellMessageBoxRequest
            {
                Body = context->Messages[instruction->Int0],
                ButtonCount = (byte)buttonCount,
                ArgCount = (byte)argCount,
            };

            for (int i = argCount - 1; i >= 0; i--)
            {
                if (!Pop(context, out var value))
                    return;
                SetMessageBoxArg(ref request, i, value);
            }

            for (int i = 0; i < buttonCount; i++)
                SetMessageBoxButton(ref request, i, context->Messages[instruction->Int1 + i]);

            context->Ecb.AppendToBuffer(context->SortKey, context->MessageBoxRuntimeEntity, request);
        }

        [BurstCompile]
        static void ShowMap(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (context->GlobalMapRevealRuntimeEntity == Entity.Null
                || (context->Messages == null && context->MessageCount > 0)
                || (uint)instruction->Int0 >= (uint)context->MessageCount)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->GlobalMapRevealRuntimeEntity, new GlobalMapRevealRequest
            {
                CellNamePrefix = context->Messages[instruction->Int0],
            });
        }

        static void EmitFollowRequest(
            MorrowindScriptExecutionContext* context,
            MorrowindScriptInstructionRuntime* instruction,
            ulong destinationInteriorCellHash)
        {
            if (!TryResolveRefTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity))
                return;

            if (targetPlacedRefId == 0u || context->AiRuntimeEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            byte followTargetMode = destinationInteriorCellHash != 0UL
                ? (byte)MorrowindScriptRefTargetMode.Player
                : (byte)instruction->Int2;
            int followTargetRefKey = destinationInteriorCellHash != 0UL ? 0 : instruction->Int1;
            if (followTargetMode == 0 && followTargetRefKey == 0)
                followTargetMode = (byte)MorrowindScriptRefTargetMode.Player;

            if (!TryResolveActorTarget(context, followTargetMode, followTargetRefKey, out uint followTargetPlacedRefId, out Entity followTargetEntity))
                return;

            context->Ecb.AppendToBuffer(context->SortKey, context->AiRuntimeEntity, new MorrowindScriptAiPackageRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                FollowTargetEntity = followTargetEntity,
                FollowTargetPlacedRefId = followTargetPlacedRefId,
                PackageType = (byte)MorrowindScriptAiPackageRequestType.Follow,
                ShouldRepeat = instruction->Operand1 != 0 ? (byte)1 : (byte)0,
                AllowPartial = 1,
                TargetPosition = new float3(instruction->Float1, instruction->Float2, instruction->Float3),
                DestinationInteriorCellHash = destinationInteriorCellHash,
                FollowDistance = 256f * WorldScale.MwUnitsToMeters,
                IdleSeconds = 0.5f,
            });
        }

        static void EmitTransformRequest(
            MorrowindScriptExecutionContext* context,
            MorrowindScriptInstructionRuntime* instruction,
            float radians,
            byte operation)
        {
            if (!TryResolveRefTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity))
                return;

            if (targetPlacedRefId == 0u || context->TransformRuntimeEntity == Entity.Null || instruction->Operand1 < 0 || instruction->Operand1 > 2)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->TransformRuntimeEntity, new MorrowindScriptTransformRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                Radians = radians,
                Axis = (byte)instruction->Operand1,
                Operation = operation,
            });
        }

        [BurstCompile]
        static void GetDisabled(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            byte disabled;
            if (instruction->Operand0 == (byte)MorrowindScriptRefTargetMode.Self)
            {
                disabled = context->SelfDisabled;
            }
            else if (instruction->Operand0 == (byte)MorrowindScriptRefTargetMode.PlacedRef)
            {
                uint placedRefId = unchecked((uint)instruction->Int0);
                if (!context->RefDisabledStates.IsCreated)
                {
                    context->Faulted = 1;
                    return;
                }

                if (!context->RefDisabledStates.TryGetValue(placedRefId, out disabled))
                    disabled = 0;
            }
            else
            {
                context->Faulted = 1;
                return;
            }

            Push(context, new MorrowindScriptStackValue
            {
                IntValue = disabled != 0 ? 1 : 0,
                FloatValue = disabled != 0 ? 1f : 0f,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void RequestSetDisabled(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveRefTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity))
                return;

            if (targetPlacedRefId == 0u || context->RefStateRuntimeEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->RefStateRuntimeEntity, new MorrowindScriptRefStateRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                Disabled = (byte)(instruction->Operand1 != 0 ? 1 : 0),
            });
        }

        [BurstCompile]
        static void GetLocked(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveRefTarget(context, instruction, out uint targetPlacedRefId, out _))
                return;

            if (targetPlacedRefId == 0u
                || (context->LockStates == null && context->LockStateCount > 0)
                || context->LockStateCount < 0)
            {
                context->Faulted = 1;
                return;
            }

            if (!IsLoadedPlacedRef(context, targetPlacedRefId))
            {
                context->Faulted = 1;
                return;
            }

            int matchCount = 0;
            byte locked = 0;
            for (int i = 0; i < context->LockStateCount; i++)
            {
                var snapshot = context->LockStates[i];
                if (snapshot.PlacedRefId != targetPlacedRefId)
                    continue;

                matchCount++;
                locked = snapshot.Locked;
            }

            if (matchCount > 1)
            {
                context->Faulted = 1;
                return;
            }

            int value = locked != 0 ? 1 : 0;
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = value,
                FloatValue = value,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void GetStandingPC(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveRefTarget(context, instruction, out uint targetPlacedRefId, out _))
                return;

            if (targetPlacedRefId == 0u)
            {
                context->Faulted = 1;
                return;
            }

            if (!IsLoadedPlacedRef(context, targetPlacedRefId))
            {
                context->Faulted = 1;
                return;
            }

            int value = targetPlacedRefId == context->PlayerStandingOnPlacedRefId ? 1 : 0;
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = value,
                FloatValue = value,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void GetLOS(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
            => PushLineOfSightResult(context, instruction);

        [BurstCompile]
        static void GetDetected(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
            => PushLineOfSightResult(context, instruction);

        [BurstCompile]
        static void GetAttacked(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryFindActorEventSnapshot(context, instruction, out var snapshot, out _))
                return;

            int value = snapshot.Attacked != 0 ? 1 : 0;
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = value,
                FloatValue = value,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void OnMurder(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryFindActorEventSnapshot(context, instruction, out var snapshot, out uint targetPlacedRefId))
                return;

            int value = snapshot.Murdered != 0 ? 1 : 0;
            if (value != 0)
                AppendActorEventConsumeRequest(context, snapshot.Entity, targetPlacedRefId, MorrowindScriptActorEventConsumeKind.Murdered);

            Push(context, new MorrowindScriptStackValue
            {
                IntValue = value,
                FloatValue = value,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void OnKnockout(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryFindActorEventSnapshot(context, instruction, out var snapshot, out _))
                return;

            int value = snapshot.KnockedDownOneFrame != 0 ? 1 : 0;
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = value,
                FloatValue = value,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void HitOnMe(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryFindActorEventSnapshot(context, instruction, out var snapshot, out uint targetPlacedRefId))
                return;

            var content = new ContentReference
            {
                Kind = (ContentReferenceKind)instruction->Operand1,
                HandleValue = instruction->Int1,
            };
            if (!content.IsValid)
            {
                context->Faulted = 1;
                return;
            }

            int value = SameContent(snapshot.LastHitObject, content) ? 1 : 0;
            if (value != 0)
                AppendActorEventConsumeRequest(context, snapshot.Entity, targetPlacedRefId, MorrowindScriptActorEventConsumeKind.LastHitObject);

            Push(context, new MorrowindScriptStackValue
            {
                IntValue = value,
                FloatValue = value,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        static bool TryFindActorEventSnapshot(
            MorrowindScriptExecutionContext* context,
            MorrowindScriptInstructionRuntime* instruction,
            out MorrowindScriptActorEventSnapshot snapshot,
            out uint targetPlacedRefId)
        {
            snapshot = default;
            if (!TryResolveActorTarget(context, instruction->Operand0, instruction->Int0, out targetPlacedRefId, out _))
                return false;

            if ((targetPlacedRefId != 0u && !IsLoadedPlacedRef(context, targetPlacedRefId))
                || (context->ActorEvents == null && context->ActorEventCount > 0)
                || context->ActorEventCount < 0)
            {
                context->Faulted = 1;
                return false;
            }

            int matchCount = 0;
            for (int i = 0; i < context->ActorEventCount; i++)
            {
                var candidate = context->ActorEvents[i];
                if (candidate.PlacedRefId != targetPlacedRefId)
                    continue;

                matchCount++;
                snapshot = candidate;
            }

            if (matchCount != 1)
            {
                context->Faulted = 1;
                return false;
            }

            return true;
        }

        static void AppendActorEventConsumeRequest(
            MorrowindScriptExecutionContext* context,
            Entity targetEntity,
            uint targetPlacedRefId,
            MorrowindScriptActorEventConsumeKind kind)
        {
            if (context->ActorEventRuntimeEntity == Entity.Null || targetEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->ActorEventRuntimeEntity, new MorrowindScriptActorEventConsumeRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                Kind = (byte)kind,
            });
        }

        static bool SameContent(ContentReference left, ContentReference right)
            => left.Kind == right.Kind && left.HandleValue == right.HandleValue;

        static void PushLineOfSightResult(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveActorTarget(context, instruction->Operand0, instruction->Int0, out uint sourcePlacedRefId, out _)
                || !TryResolveActorTarget(context, (byte)instruction->Operand1, instruction->Int1, out uint targetPlacedRefId, out _))
            {
                return;
            }

            if ((sourcePlacedRefId != 0u && !IsLoadedPlacedRef(context, sourcePlacedRefId))
                || (targetPlacedRefId != 0u && !IsLoadedPlacedRef(context, targetPlacedRefId))
                || (context->ActorLineOfSight == null && context->ActorLineOfSightCount > 0)
                || context->ActorLineOfSightCount < 0)
            {
                context->Faulted = 1;
                return;
            }

            int value = sourcePlacedRefId == targetPlacedRefId
                ? 1
                : FindLineOfSightSnapshot(context, sourcePlacedRefId, targetPlacedRefId);
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = value,
                FloatValue = value,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        static int FindLineOfSightSnapshot(MorrowindScriptExecutionContext* context, uint sourcePlacedRefId, uint targetPlacedRefId)
        {
            for (int i = 0; i < context->ActorLineOfSightCount; i++)
            {
                var snapshot = context->ActorLineOfSight[i];
                if (snapshot.SourcePlacedRefId == sourcePlacedRefId
                    && snapshot.TargetPlacedRefId == targetPlacedRefId)
                {
                    return snapshot.HasLineOfSight != 0 ? 1 : 0;
                }
            }

            return 0;
        }

        [BurstCompile]
        static void RequestLockState(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!TryResolveRefTarget(context, instruction, out uint targetPlacedRefId, out Entity targetEntity))
                return;

            if (targetPlacedRefId == 0u || context->RefStateRuntimeEntity == Entity.Null)
            {
                context->Faulted = 1;
                return;
            }

            context->Ecb.AppendToBuffer(context->SortKey, context->RefStateRuntimeEntity, new PlacedRefLockRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                Operation = (byte)instruction->Operand1,
                LockLevel = instruction->Int1,
            });
        }

        static bool TryResolveRefTarget(
            MorrowindScriptExecutionContext* context,
            MorrowindScriptInstructionRuntime* instruction,
            out uint targetPlacedRefId,
            out Entity targetEntity)
            => TryResolveRefTarget(context, instruction->Operand0, instruction->Int0, out targetPlacedRefId, out targetEntity);

        static bool IsResolvedTargetDisabled(MorrowindScriptExecutionContext* context, byte targetMode, uint targetPlacedRefId)
        {
            if (targetMode == (byte)MorrowindScriptRefTargetMode.Self)
                return context->SelfDisabled != 0;

            if (targetPlacedRefId == 0u)
                return false;

            if (!context->RefDisabledStates.IsCreated)
            {
                context->Faulted = 1;
                return true;
            }

            return context->RefDisabledStates.TryGetValue(targetPlacedRefId, out byte disabled) && disabled != 0;
        }

        static void SetMessageBoxArg(ref ShellMessageBoxRequest request, int index, in MorrowindScriptStackValue value)
        {
            switch (index)
            {
                case 0:
                    request.Arg0Kind = value.ValueKind;
                    request.Arg0Int = value.ValueKind == (byte)MorrowindScriptValueKind.Float ? (int)value.FloatValue : value.IntValue;
                    request.Arg0Float = value.ValueKind == (byte)MorrowindScriptValueKind.Float ? value.FloatValue : value.IntValue;
                    break;
                case 1:
                    request.Arg1Kind = value.ValueKind;
                    request.Arg1Int = value.ValueKind == (byte)MorrowindScriptValueKind.Float ? (int)value.FloatValue : value.IntValue;
                    request.Arg1Float = value.ValueKind == (byte)MorrowindScriptValueKind.Float ? value.FloatValue : value.IntValue;
                    break;
                case 2:
                    request.Arg2Kind = value.ValueKind;
                    request.Arg2Int = value.ValueKind == (byte)MorrowindScriptValueKind.Float ? (int)value.FloatValue : value.IntValue;
                    request.Arg2Float = value.ValueKind == (byte)MorrowindScriptValueKind.Float ? value.FloatValue : value.IntValue;
                    break;
                case 3:
                    request.Arg3Kind = value.ValueKind;
                    request.Arg3Int = value.ValueKind == (byte)MorrowindScriptValueKind.Float ? (int)value.FloatValue : value.IntValue;
                    request.Arg3Float = value.ValueKind == (byte)MorrowindScriptValueKind.Float ? value.FloatValue : value.IntValue;
                    break;
                case 4:
                    request.Arg4Kind = value.ValueKind;
                    request.Arg4Int = value.ValueKind == (byte)MorrowindScriptValueKind.Float ? (int)value.FloatValue : value.IntValue;
                    request.Arg4Float = value.ValueKind == (byte)MorrowindScriptValueKind.Float ? value.FloatValue : value.IntValue;
                    break;
                case 5:
                    request.Arg5Kind = value.ValueKind;
                    request.Arg5Int = value.ValueKind == (byte)MorrowindScriptValueKind.Float ? (int)value.FloatValue : value.IntValue;
                    request.Arg5Float = value.ValueKind == (byte)MorrowindScriptValueKind.Float ? value.FloatValue : value.IntValue;
                    break;
                case 6:
                    request.Arg6Kind = value.ValueKind;
                    request.Arg6Int = value.ValueKind == (byte)MorrowindScriptValueKind.Float ? (int)value.FloatValue : value.IntValue;
                    request.Arg6Float = value.ValueKind == (byte)MorrowindScriptValueKind.Float ? value.FloatValue : value.IntValue;
                    break;
                case 7:
                    request.Arg7Kind = value.ValueKind;
                    request.Arg7Int = value.ValueKind == (byte)MorrowindScriptValueKind.Float ? (int)value.FloatValue : value.IntValue;
                    request.Arg7Float = value.ValueKind == (byte)MorrowindScriptValueKind.Float ? value.FloatValue : value.IntValue;
                    break;
            }
        }

        static void SetMessageBoxButton(ref ShellMessageBoxRequest request, int index, FixedString512Bytes value)
        {
            FixedString128Bytes text = default;
            text.Append(value);
            switch (index)
            {
                case 0:
                    request.Button0 = text;
                    break;
                case 1:
                    request.Button1 = text;
                    break;
                case 2:
                    request.Button2 = text;
                    break;
                case 3:
                    request.Button3 = text;
                    break;
                case 4:
                    request.Button4 = text;
                    break;
                case 5:
                    request.Button5 = text;
                    break;
                case 6:
                    request.Button6 = text;
                    break;
                case 7:
                    request.Button7 = text;
                    break;
                case 8:
                    request.Button8 = text;
                    break;
                case 9:
                    request.Button9 = text;
                    break;
            }
        }

        static bool TryResolveRefTarget(
            MorrowindScriptExecutionContext* context,
            byte targetMode,
            int targetRefKey,
            out uint targetPlacedRefId,
            out Entity targetEntity)
        {
            if (targetMode == (byte)MorrowindScriptRefTargetMode.Self)
            {
                targetPlacedRefId = context->PlacedRefId;
                targetEntity = context->Entity;
                return true;
            }

            if (targetMode == (byte)MorrowindScriptRefTargetMode.Player)
            {
                targetPlacedRefId = 0u;
                targetEntity = context->PlayerEntity;
                if (targetEntity == Entity.Null)
                {
                    context->Faulted = 1;
                    return false;
                }

                return true;
            }

            if (targetMode == (byte)MorrowindScriptRefTargetMode.PlacedRef)
            {
                targetPlacedRefId = unchecked((uint)targetRefKey);
                targetEntity = Entity.Null;
                if (!context->RefDisabledStates.IsCreated)
                {
                    context->Faulted = 1;
                    return false;
                }

                return true;
            }

            if (targetMode == (byte)MorrowindScriptRefTargetMode.ActiveContentRef)
            {
                targetPlacedRefId = 0u;
                targetEntity = Entity.Null;
                if (!context->ActiveExplicitRefs.IsCreated
                    || !context->ActiveExplicitRefs.TryGetValue(targetRefKey, out var target)
                    || target.Ambiguous != 0
                    || target.PlacedRefId == 0u
                    || target.Entity == Entity.Null)
                {
                    context->Faulted = 1;
                    return false;
                }

                targetPlacedRefId = target.PlacedRefId;
                targetEntity = target.Entity;
                return true;
            }

            targetPlacedRefId = 0u;
            targetEntity = Entity.Null;
            context->Faulted = 1;
            return false;
        }

        static bool TryResolveActorTarget(
            MorrowindScriptExecutionContext* context,
            MorrowindScriptInstructionRuntime* instruction,
            out uint targetPlacedRefId,
            out Entity targetEntity)
            => TryResolveActorTarget(context, instruction->Operand0, instruction->Int0, out targetPlacedRefId, out targetEntity);

        static bool TryResolveActorTarget(
            MorrowindScriptExecutionContext* context,
            byte targetMode,
            int targetRefKey,
            out uint targetPlacedRefId,
            out Entity targetEntity)
        {
            if (targetMode == (byte)MorrowindScriptRefTargetMode.Player)
            {
                targetPlacedRefId = 0u;
                targetEntity = context->PlayerEntity;
                if (targetEntity == Entity.Null)
                {
                    context->Faulted = 1;
                    return false;
                }

                return true;
            }

            return TryResolveRefTarget(context, targetMode, targetRefKey, out targetPlacedRefId, out targetEntity);
        }

        static bool TryResolveDistancePosition(
            MorrowindScriptExecutionContext* context,
            byte targetMode,
            int targetRefKey,
            out float3 position)
        {
            position = default;
            if (targetMode == (byte)MorrowindScriptRefTargetMode.Player)
            {
                if (context->HasPlayerPosition == 0)
                {
                    context->Faulted = 1;
                    return false;
                }

                position = context->PlayerPosition;
                return true;
            }

            if (targetMode == (byte)MorrowindScriptRefTargetMode.Self)
            {
                position = context->Position;
                return true;
            }

            uint placedRefId = 0u;
            if (targetMode == (byte)MorrowindScriptRefTargetMode.PlacedRef)
            {
                placedRefId = unchecked((uint)targetRefKey);
            }
            else if (targetMode == (byte)MorrowindScriptRefTargetMode.ActiveContentRef)
            {
                if (!context->ActiveExplicitRefs.IsCreated
                    || !context->ActiveExplicitRefs.TryGetValue(targetRefKey, out var target)
                    || target.Ambiguous != 0
                    || target.PlacedRefId == 0u)
                {
                    context->Faulted = 1;
                    return false;
                }

                placedRefId = target.PlacedRefId;
            }
            else
            {
                context->Faulted = 1;
                return false;
            }

            if (placedRefId == 0u
                || (context->RefTransforms == null && context->RefTransformCount > 0)
                || context->RefTransformCount < 0)
            {
                context->Faulted = 1;
                return false;
            }

            int matchCount = 0;
            float3 matchedPosition = default;
            for (int i = 0; i < context->RefTransformCount; i++)
            {
                var snapshot = context->RefTransforms[i];
                if (snapshot.PlacedRefId != placedRefId)
                    continue;

                matchCount++;
                matchedPosition = snapshot.Position;
            }

            if (matchCount != 1)
            {
                context->Faulted = 1;
                return false;
            }

            position = matchedPosition;
            return true;
        }

        static bool TryResolveTargetTransform(
            MorrowindScriptExecutionContext* context,
            byte targetMode,
            int targetRefKey,
            out float3 position,
            out quaternion rotation)
        {
            position = default;
            rotation = quaternion.identity;
            if (targetMode == (byte)MorrowindScriptRefTargetMode.Self)
            {
                position = context->Position;
                rotation = context->Rotation;
                return true;
            }

            uint placedRefId = 0u;
            if (targetMode == (byte)MorrowindScriptRefTargetMode.PlacedRef)
            {
                placedRefId = unchecked((uint)targetRefKey);
            }
            else if (targetMode == (byte)MorrowindScriptRefTargetMode.ActiveContentRef)
            {
                if (!context->ActiveExplicitRefs.IsCreated
                    || !context->ActiveExplicitRefs.TryGetValue(targetRefKey, out var target)
                    || target.Ambiguous != 0
                    || target.PlacedRefId == 0u)
                {
                    context->Faulted = 1;
                    return false;
                }

                placedRefId = target.PlacedRefId;
            }
            else
            {
                context->Faulted = 1;
                return false;
            }

            if (placedRefId == 0u
                || (context->RefTransforms == null && context->RefTransformCount > 0)
                || context->RefTransformCount < 0)
            {
                context->Faulted = 1;
                return false;
            }

            int matchCount = 0;
            for (int i = 0; i < context->RefTransformCount; i++)
            {
                var snapshot = context->RefTransforms[i];
                if (snapshot.PlacedRefId != placedRefId)
                    continue;

                matchCount++;
                position = snapshot.Position;
                rotation = snapshot.Rotation;
            }

            if (matchCount == 1)
                return true;

            context->Faulted = 1;
            return false;
        }

        static float3 AxisDelta(byte axis, float value)
        {
            return axis switch
            {
                1 => new float3(0f, 0f, value),
                2 => new float3(0f, value, 0f),
                _ => new float3(value, 0f, 0f),
            };
        }

        static bool IsLoadedPlacedRef(MorrowindScriptExecutionContext* context, uint placedRefId)
        {
            if (placedRefId == context->PlacedRefId && context->Entity != Entity.Null)
                return true;

            if ((context->RefTransforms == null && context->RefTransformCount > 0) || context->RefTransformCount < 0)
            {
                context->Faulted = 1;
                return false;
            }

            int matchCount = 0;
            for (int i = 0; i < context->RefTransformCount; i++)
            {
                if (context->RefTransforms[i].PlacedRefId == placedRefId)
                    matchCount++;
            }

            return matchCount == 1;
        }

        public static ulong BuildScriptLoopSourceKey(uint placedRefId, Entity sourceEntity)
        {
            return placedRefId != 0u
                ? placedRefId
                : (uint)sourceEntity.Index;
        }

        public static ulong BuildScriptLoopKey(uint placedRefId, Entity sourceEntity, int soundHandleValue)
        {
            ulong source = BuildScriptLoopSourceKey(placedRefId, sourceEntity);
            return (source << 32) ^ (uint)soundHandleValue;
        }

        public static ulong HashStringPrefix(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0UL;

            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c >= 'A' && c <= 'Z')
                    c = (char)(c + 32);
                hash ^= c;
                hash *= 1099511628211UL;
            }

            return hash == 0UL ? 1UL : hash;
        }

        static ulong HashPrefix(FixedString128Bytes value, int length)
        {
            if (length <= 0)
                return 0UL;

            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < length; i++)
            {
                byte c = value[i];
                if (c >= (byte)'A' && c <= (byte)'Z')
                    c = (byte)(c + 32);
                hash ^= c;
                hash *= 1099511628211UL;
            }

            return hash == 0UL ? 1UL : hash;
        }

        static ulong HashFixedString64(FixedString64Bytes value)
        {
            if (value.Length <= 0)
                return 0UL;

            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < value.Length; i++)
            {
                byte c = value[i];
                if (c >= (byte)'A' && c <= (byte)'Z')
                    c = (byte)(c + 32);
                hash ^= c;
                hash *= 1099511628211UL;
            }

            return hash == 0UL ? 1UL : hash;
        }

        static bool TryGetLocal(MorrowindScriptExecutionContext* context, int index, out MorrowindScriptLocalValue local)
        {
            local = default;
            if ((uint)index >= (uint)context->LocalCount)
            {
                context->Faulted = 1;
                return false;
            }

            local = context->Locals[index];
            return true;
        }

        static bool TryGetGlobal(MorrowindScriptExecutionContext* context, int index, out MorrowindScriptGlobalValue global)
        {
            global = default;
            if ((uint)index >= (uint)context->GlobalCount)
            {
                context->Faulted = 1;
                return false;
            }

            global = context->Globals[index];
            return true;
        }

        static void Push(MorrowindScriptExecutionContext* context, in MorrowindScriptStackValue value)
        {
            if (context->StackLength >= context->StackCapacity)
            {
                context->Faulted = 1;
                return;
            }

            context->Stack[context->StackLength++] = value;
        }

        static bool Pop(MorrowindScriptExecutionContext* context, out MorrowindScriptStackValue value)
        {
            value = default;
            if (context->StackLength <= 0)
            {
                context->Faulted = 1;
                return false;
            }

            value = context->Stack[--context->StackLength];
            return true;
        }

        static void BinaryMath(MorrowindScriptExecutionContext* context, byte operation)
        {
            if (!Pop(context, out var rhs) || !Pop(context, out var lhs))
                return;

            bool integerMath = lhs.ValueKind != (byte)MorrowindScriptValueKind.Float
                && rhs.ValueKind != (byte)MorrowindScriptValueKind.Float;
            if (integerMath)
            {
                int leftInt = lhs.IntValue;
                int rightInt = rhs.IntValue;
                if (operation == 3 && rightInt == 0)
                {
                    context->Faulted = 1;
                    return;
                }

                int intResult = operation switch
                {
                    0 => leftInt + rightInt,
                    1 => leftInt - rightInt,
                    2 => leftInt * rightInt,
                    _ => leftInt / rightInt,
                };
                Push(context, new MorrowindScriptStackValue
                {
                    IntValue = intResult,
                    FloatValue = intResult,
                    ValueKind = (byte)MorrowindScriptValueKind.Integer,
                });
                return;
            }

            float left = ToFloat(lhs);
            float right = ToFloat(rhs);
            if (operation == 3 && math.abs(right) <= 0.000001f)
            {
                context->Faulted = 1;
                return;
            }

            float result = operation switch
            {
                0 => left + right,
                1 => left - right,
                2 => left * right,
                _ => left / right,
            };
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = (int)result,
                FloatValue = result,
                ValueKind = (byte)MorrowindScriptValueKind.Float,
            });
        }

        static void Compare(MorrowindScriptExecutionContext* context, byte operation)
        {
            if (!Pop(context, out var rhs) || !Pop(context, out var lhs))
                return;

            float left = ToFloat(lhs);
            float right = ToFloat(rhs);
            bool result = operation switch
            {
                0 => math.abs(left - right) <= 0.000001f,
                1 => math.abs(left - right) > 0.000001f,
                2 => left < right,
                3 => left <= right,
                4 => left > right,
                _ => left >= right,
            };
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = result ? 1 : 0,
                FloatValue = result ? 1f : 0f,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        static float ToFloat(in MorrowindScriptStackValue value)
        {
            return value.ValueKind == (byte)MorrowindScriptValueKind.Float ? value.FloatValue : value.IntValue;
        }

        static int RollScriptRandom(uint* state, int limit)
        {
            if (limit <= 0)
                return 0;

            const ulong minStdRange = 2147483646UL;
            ulong range = (uint)limit;
            if (range <= minStdRange)
            {
                ulong usable = minStdRange - minStdRange % range;
                ulong sample;
                do
                {
                    sample = NextMinStd(state) - 1UL;
                }
                while (sample >= usable);
                return (int)(sample % range);
            }

            ulong combinedRange = minStdRange * minStdRange;
            ulong combinedUsable = combinedRange - combinedRange % range;
            ulong combinedSample;
            do
            {
                combinedSample = (NextMinStd(state) - 1UL) * minStdRange + (NextMinStd(state) - 1UL);
            }
            while (combinedSample >= combinedUsable);
            return (int)(combinedSample % range);
        }

        static uint NextMinStd(uint* state)
        {
            const uint modulus = 2147483647u;
            const uint multiplier = 48271u;
            uint current = *state % modulus;
            if (current == 0u)
                current = 1u;

            uint next = (uint)(((ulong)current * multiplier) % modulus);
            *state = next == 0u ? 1u : next;
            return *state;
        }

        static bool IsCurrentActivationTarget(MorrowindScriptExecutionContext* context)
        {
            if (context->ActivationEvents == null || context->ActivationEventCount <= 0)
                return false;

            for (int i = 0; i < context->ActivationEventCount; i++)
            {
                var activationEvent = context->ActivationEvents[i];
                bool matches = activationEvent.TargetPlacedRefId != 0u
                    ? activationEvent.TargetPlacedRefId == context->PlacedRefId
                    : context->Entity.Index == activationEvent.TargetEntity.Index
                        && context->Entity.Version == activationEvent.TargetEntity.Version;
                if (!matches)
                    continue;

                context->MatchedActivationEventIndex = i;
                return true;
            }

            return false;
        }
    }
}
