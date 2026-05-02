using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    public partial class MorrowindScriptRuntimeBootstrapSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (SystemAPI.HasSingleton<MorrowindScriptRuntimeState>())
                return;

            var contentDb = RuntimeContentDatabase.Active;
            if (contentDb == null)
                return;

            WorldResources.MorrowindScriptCatalog?.Dispose();
            WorldResources.MorrowindScriptCatalog = MorrowindScriptRuntimeCatalog.Create(contentDb.Data);

            Entity runtime = EntityManager.CreateEntity(typeof(MorrowindScriptRuntimeState));
            EntityManager.SetName(runtime, "VVardenfell.MorrowindScriptRuntime");
            EntityManager.SetComponentData(runtime, new MorrowindScriptRuntimeState
            {
                NextAudioRequestSequence = 1u,
            });

            var globals = EntityManager.AddBuffer<MorrowindScriptGlobalValue>(runtime);
            globals.ResizeUninitialized(contentDb.GlobalCount);
            for (int i = 0; i < contentDb.GlobalCount; i++)
            {
                ref readonly var global = ref contentDb.GetGlobal(GenericRecordDefHandle.FromIndex(i));
                byte valueKind = ResolveGlobalKind(global);
                globals[i] = new MorrowindScriptGlobalValue
                {
                    IntValue = valueKind == (byte)MorrowindScriptValueKind.Float ? (int)global.Float0 : global.Int0,
                    FloatValue = valueKind == (byte)MorrowindScriptValueKind.Float ? global.Float0 : global.Int0,
                    ValueKind = valueKind,
                };
            }

            var deathCounts = EntityManager.AddBuffer<MorrowindActorDeathCount>(runtime);
            deathCounts.ResizeUninitialized(contentDb.ActorCount);
            for (int i = 0; i < deathCounts.Length; i++)
                deathCounts[i] = default;

            EntityManager.AddComponentData(runtime, new MorrowindQuestJournalState
            {
                QuestCount = contentDb.DialogueCount,
                NextEntrySequence = 1u,
            });
            var journal = EntityManager.AddBuffer<MorrowindQuestJournalIndex>(runtime);
            journal.ResizeUninitialized(contentDb.DialogueCount);
            for (int i = 0; i < journal.Length; i++)
                journal[i] = default;
            EntityManager.AddBuffer<MorrowindQuestJournalEntry>(runtime);
            EntityManager.AddBuffer<MorrowindQuestJournalRequest>(runtime);

            EntityManager.AddComponentData(runtime, new MorrowindDialogueState
            {
                DialogueCount = contentDb.DialogueCount,
                NextTopicEntrySequence = 1u,
                NextSessionSequence = 1u,
            });
            var topics = EntityManager.AddBuffer<MorrowindKnownDialogueTopic>(runtime);
            topics.ResizeUninitialized(contentDb.DialogueCount);
            for (int i = 0; i < topics.Length; i++)
                topics[i] = default;
            EntityManager.AddBuffer<MorrowindTopicJournalEntry>(runtime);
            EntityManager.AddBuffer<MorrowindFactionReactionOverride>(runtime);
            EntityManager.AddBuffer<MorrowindDialogueRequest>(runtime);

            EntityManager.AddBuffer<MorrowindScriptActiveSource>(runtime);
            EntityManager.AddBuffer<MorrowindScriptPlayingSound>(runtime);
            EntityManager.AddBuffer<MorrowindScriptRefStateRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptTransformRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptAiPackageRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptActorAiSettingRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptDispositionRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptActorVitalRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptInventoryMutationRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptCastRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptActorLocalSetRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptMessageBoxRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptShellRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptJailRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptMovementFlagRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptPlaceAtRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptOnDeathConsumeRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptStartRequest>(runtime);
        }

        static byte ResolveGlobalKind(in GenericRecordDef global)
        {
            if (!string.IsNullOrWhiteSpace(global.Name) && global.Name[0] == 'f')
                return (byte)MorrowindScriptValueKind.Float;

            return (byte)MorrowindScriptValueKind.Integer;
        }
    }
}
