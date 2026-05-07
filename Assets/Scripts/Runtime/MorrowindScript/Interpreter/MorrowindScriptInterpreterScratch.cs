using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.MorrowindScript
{
    public struct MorrowindScriptInterpreterScratch : IComponentData
    {
        public NativeList<MorrowindScriptExternalActorLocalSnapshot> ExternalActorLocals;
        public NativeList<MorrowindScriptActorAiStatusSnapshot> ActorAiStatuses;
        public NativeList<MorrowindScriptActorCombatTargetSnapshot> ActorCombatTargets;
        public NativeList<MorrowindScriptLockStateSnapshot> LockStates;
        public NativeList<MorrowindScriptActorEventSnapshot> ActorEvents;
        public NativeList<MorrowindScriptActorVitalSnapshot> ActorVitals;
        public NativeList<MorrowindScriptActorAttributeSnapshot> ActorAttributes;
        public NativeList<MorrowindScriptActorActiveEffectSnapshot> ActorActiveEffects;
        public NativeList<MorrowindScriptActorDiseaseSnapshot> ActorDiseases;
        public NativeList<MorrowindScriptActorIdentitySnapshot> ActorIdentities;
        public NativeList<MorrowindScriptActorAiSettingSnapshot> ActorAiSettings;
        public NativeList<MorrowindScriptActorKnownSpellSnapshot> ActorKnownSpells;
        public NativeList<MorrowindScriptActorDispositionSnapshot> ActorDispositions;
        public NativeList<MorrowindScriptActorLineOfSightSnapshot> ActorLineOfSight;
        public NativeParallelHashSet<ulong> ActorLineOfSightPairs;
        public NativeParallelHashSet<Entity> PendingLineOfSightScripts;
        public NativeList<MorrowindScriptRunningProgramSnapshot> RunningPrograms;
        public NativeList<MorrowindScriptActiveSource> ActiveSources;

        public bool IsCreated
            => RunningPrograms.IsCreated && ActiveSources.IsCreated;

        public static MorrowindScriptInterpreterScratch Create(Allocator allocator)
            => new()
            {
                ExternalActorLocals = new NativeList<MorrowindScriptExternalActorLocalSnapshot>(64, allocator),
                ActorAiStatuses = new NativeList<MorrowindScriptActorAiStatusSnapshot>(64, allocator),
                ActorCombatTargets = new NativeList<MorrowindScriptActorCombatTargetSnapshot>(64, allocator),
                LockStates = new NativeList<MorrowindScriptLockStateSnapshot>(128, allocator),
                ActorEvents = new NativeList<MorrowindScriptActorEventSnapshot>(64, allocator),
                ActorVitals = new NativeList<MorrowindScriptActorVitalSnapshot>(64, allocator),
                ActorAttributes = new NativeList<MorrowindScriptActorAttributeSnapshot>(64, allocator),
                ActorActiveEffects = new NativeList<MorrowindScriptActorActiveEffectSnapshot>(128, allocator),
                ActorDiseases = new NativeList<MorrowindScriptActorDiseaseSnapshot>(64, allocator),
                ActorIdentities = new NativeList<MorrowindScriptActorIdentitySnapshot>(64, allocator),
                ActorAiSettings = new NativeList<MorrowindScriptActorAiSettingSnapshot>(64, allocator),
                ActorKnownSpells = new NativeList<MorrowindScriptActorKnownSpellSnapshot>(128, allocator),
                ActorDispositions = new NativeList<MorrowindScriptActorDispositionSnapshot>(64, allocator),
                ActorLineOfSight = new NativeList<MorrowindScriptActorLineOfSightSnapshot>(128, allocator),
                ActorLineOfSightPairs = new NativeParallelHashSet<ulong>(128, allocator),
                PendingLineOfSightScripts = new NativeParallelHashSet<Entity>(64, allocator),
                RunningPrograms = new NativeList<MorrowindScriptRunningProgramSnapshot>(64, allocator),
                ActiveSources = new NativeList<MorrowindScriptActiveSource>(64, allocator),
            };

        public void Dispose()
        {
            if (ExternalActorLocals.IsCreated)
                ExternalActorLocals.Dispose();
            if (ActorAiStatuses.IsCreated)
                ActorAiStatuses.Dispose();
            if (ActorCombatTargets.IsCreated)
                ActorCombatTargets.Dispose();
            if (LockStates.IsCreated)
                LockStates.Dispose();
            if (ActorEvents.IsCreated)
                ActorEvents.Dispose();
            if (ActorVitals.IsCreated)
                ActorVitals.Dispose();
            if (ActorAttributes.IsCreated)
                ActorAttributes.Dispose();
            if (ActorActiveEffects.IsCreated)
                ActorActiveEffects.Dispose();
            if (ActorDiseases.IsCreated)
                ActorDiseases.Dispose();
            if (ActorIdentities.IsCreated)
                ActorIdentities.Dispose();
            if (ActorAiSettings.IsCreated)
                ActorAiSettings.Dispose();
            if (ActorKnownSpells.IsCreated)
                ActorKnownSpells.Dispose();
            if (ActorDispositions.IsCreated)
                ActorDispositions.Dispose();
            if (ActorLineOfSight.IsCreated)
                ActorLineOfSight.Dispose();
            if (ActorLineOfSightPairs.IsCreated)
                ActorLineOfSightPairs.Dispose();
            if (PendingLineOfSightScripts.IsCreated)
                PendingLineOfSightScripts.Dispose();
            if (RunningPrograms.IsCreated)
                RunningPrograms.Dispose();
            if (ActiveSources.IsCreated)
                ActiveSources.Dispose();
        }
    }
}
