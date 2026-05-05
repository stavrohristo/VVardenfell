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
        public NativeList<MorrowindScriptRefTransformSnapshot> RefTransforms;
        public NativeList<MorrowindScriptInitialTransformSnapshot> InitialTransforms;
        public NativeList<MorrowindScriptLockStateSnapshot> LockStates;
        public NativeList<MorrowindScriptInventoryCountSnapshot> InventoryCounts;
        public NativeList<MorrowindScriptActorDeathSnapshot> ActorDeaths;
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
        public NativeList<MorrowindScriptRunningProgramSnapshot> RunningPrograms;

        public bool IsCreated => RunningPrograms.IsCreated;

        public static MorrowindScriptInterpreterScratch Create(Allocator allocator)
            => new()
            {
                ExternalActorLocals = new NativeList<MorrowindScriptExternalActorLocalSnapshot>(64, allocator),
                ActorAiStatuses = new NativeList<MorrowindScriptActorAiStatusSnapshot>(64, allocator),
                ActorCombatTargets = new NativeList<MorrowindScriptActorCombatTargetSnapshot>(64, allocator),
                RefTransforms = new NativeList<MorrowindScriptRefTransformSnapshot>(256, allocator),
                InitialTransforms = new NativeList<MorrowindScriptInitialTransformSnapshot>(256, allocator),
                LockStates = new NativeList<MorrowindScriptLockStateSnapshot>(128, allocator),
                InventoryCounts = new NativeList<MorrowindScriptInventoryCountSnapshot>(256, allocator),
                ActorDeaths = new NativeList<MorrowindScriptActorDeathSnapshot>(64, allocator),
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
                RunningPrograms = new NativeList<MorrowindScriptRunningProgramSnapshot>(64, allocator),
            };

        public void Dispose()
        {
            if (ExternalActorLocals.IsCreated)
                ExternalActorLocals.Dispose();
            if (ActorAiStatuses.IsCreated)
                ActorAiStatuses.Dispose();
            if (ActorCombatTargets.IsCreated)
                ActorCombatTargets.Dispose();
            if (RefTransforms.IsCreated)
                RefTransforms.Dispose();
            if (InitialTransforms.IsCreated)
                InitialTransforms.Dispose();
            if (LockStates.IsCreated)
                LockStates.Dispose();
            if (InventoryCounts.IsCreated)
                InventoryCounts.Dispose();
            if (ActorDeaths.IsCreated)
                ActorDeaths.Dispose();
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
            if (RunningPrograms.IsCreated)
                RunningPrograms.Dispose();
        }
    }
}
