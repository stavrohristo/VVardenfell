using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.AI;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    public partial class MorrowindScriptRuntimeBootstrapSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindScriptRuntimeBootstrapRequest>();
        }

        protected override void OnUpdate()
        {
            if (SystemAPI.HasSingleton<MorrowindScriptRuntimeState>())
            {
                Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
                CreateOrRepairInterpreterScratch(runtimeEntity);
                RuntimeBootstrapUtility.EnsureComponent(EntityManager, runtimeEntity, new MorrowindMagicRuntimeState
                {
                    RandomState = 0xA5C38F2Du,
                    NextActiveSpellId = 1,
                });
                RuntimeBootstrapUtility.EnsureBuffer<ActorSpellCastRequest>(EntityManager, runtimeEntity);
                RuntimeBootstrapUtility.EnsureBuffer<ActorAiPassiveGreetingSayRequest>(EntityManager, runtimeEntity);
                RuntimeBootstrapUtility.EnsureBuffer<MorrowindCombatHitVoiceResolveRequest>(EntityManager, runtimeEntity);
                RuntimeBootstrapUtility.EnsureBuffer<MorrowindCombatHitVoiceSayRequest>(EntityManager, runtimeEntity);
                ActiveExplicitRefLookupLifecycleUtility.CreateOrRepairForBootstrap(EntityManager);
                RuntimeBootstrapRequestUtility.Consume<MorrowindScriptRuntimeBootstrapRequest>(EntityManager);
                return;
            }

            if (!SystemAPI.HasSingleton<RuntimeContentBlobReference>())
                return;

            BlobAssetReference<RuntimeContentBlob> contentBlob = SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob;
            if (!contentBlob.IsCreated)
                return;

            ref RuntimeContentBlob content = ref contentBlob.Value;

            WorldResources.MorrowindScriptCatalog?.Dispose();
            WorldResources.MorrowindScriptCatalog = MorrowindScriptRuntimeCatalog.Create(contentBlob);

            Entity runtime = EntityManager.CreateEntity(typeof(MorrowindScriptRuntimeState));
            EntityManager.SetName(runtime, "VVardenfell.MorrowindScriptRuntime");
            EntityManager.SetComponentData(runtime, new MorrowindScriptRuntimeState
            {
                NextAudioRequestSequence = 1u,
                RandomState = 0x6E624EB7u,
            });
            CreateOrRepairInterpreterScratch(runtime);

            var globals = EntityManager.AddBuffer<MorrowindScriptGlobalValue>(runtime);
            globals.ResizeUninitialized(content.Globals.Length);
            for (int i = 0; i < content.Globals.Length; i++)
            {
                ref RuntimeGenericRecordDefBlob global = ref content.Globals[i];
                byte valueKind = ResolveGlobalKind(ref global);
                globals[i] = new MorrowindScriptGlobalValue
                {
                    IntValue = valueKind == (byte)MorrowindScriptValueKind.Float ? (int)global.Float0 : global.Int0,
                    FloatValue = valueKind == (byte)MorrowindScriptValueKind.Float ? global.Float0 : global.Int0,
                    ValueKind = valueKind,
                };
            }

            var deathCounts = EntityManager.AddBuffer<MorrowindActorDeathCount>(runtime);
            deathCounts.ResizeUninitialized(content.Actors.Length);
            for (int i = 0; i < deathCounts.Length; i++)
                deathCounts[i] = default;

            EntityManager.AddComponentData(runtime, new MorrowindQuestJournalState
            {
                QuestCount = content.Dialogues.Length,
                NextEntrySequence = 1u,
            });
            var journal = EntityManager.AddBuffer<MorrowindQuestJournalIndex>(runtime);
            journal.ResizeUninitialized(content.Dialogues.Length);
            for (int i = 0; i < journal.Length; i++)
                journal[i] = default;
            EntityManager.AddBuffer<MorrowindQuestJournalEntry>(runtime);
            EntityManager.AddBuffer<MorrowindQuestJournalRequest>(runtime);

            EntityManager.AddComponentData(runtime, new MorrowindDialogueState
            {
                DialogueCount = content.Dialogues.Length,
                NextTopicEntrySequence = 1u,
                NextSessionSequence = 1u,
            });
            var topics = EntityManager.AddBuffer<MorrowindKnownDialogueTopic>(runtime);
            topics.ResizeUninitialized(content.Dialogues.Length);
            for (int i = 0; i < topics.Length; i++)
                topics[i] = default;
            EntityManager.AddBuffer<MorrowindTopicJournalEntry>(runtime);
            EntityManager.AddBuffer<MorrowindFactionReactionOverride>(runtime);
            EntityManager.AddBuffer<MorrowindDialogueRequest>(runtime);

            EntityManager.AddBuffer<MorrowindScriptActiveSource>(runtime);
            EntityManager.AddBuffer<MorrowindScriptPlayingSound>(runtime);
            EntityManager.AddBuffer<MorrowindScriptActiveSay>(runtime);
            EntityManager.AddBuffer<MorrowindScriptRefStateRequest>(runtime);
            EntityManager.AddBuffer<PlacedRefLockRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptTransformRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptAiPackageRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptActorAiSettingRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptDispositionRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptActorVitalRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptHurtStandingActorRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptAnimationGroupRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptInventoryMutationRequest>(runtime);
            EntityManager.AddBuffer<ActorInventoryDropRequest>(runtime);
            EntityManager.AddBuffer<ScriptedCastRequest>(runtime);
            EntityManager.AddBuffer<ActorSpellCastRequest>(runtime);
            EntityManager.AddBuffer<ActorSpellMutationRequest>(runtime);
            EntityManager.AddBuffer<ActorForceGreetingRequest>(runtime);
            EntityManager.AddBuffer<PlayerReputationMutationRequest>(runtime);
            EntityManager.AddBuffer<ActorAttributeMutationRequest>(runtime);
            EntityManager.AddBuffer<PlayerSkillMutationRequest>(runtime);
            EntityManager.AddBuffer<PlayerFactionMutationRequest>(runtime);
            EntityManager.AddBuffer<ActorFactionRankMutationRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptSayRequest>(runtime);
            EntityManager.AddBuffer<ActorAiPassiveGreetingSayRequest>(runtime);
            EntityManager.AddBuffer<MorrowindCombatHitVoiceResolveRequest>(runtime);
            EntityManager.AddBuffer<MorrowindCombatHitVoiceSayRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptActorLocalSetRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptFactionReactionRequest>(runtime);
            EntityManager.AddBuffer<ShellMessageBoxRequest>(runtime);
            EntityManager.AddBuffer<GlobalMapRevealRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptShellRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptJailRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptMovementFlagRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptPlaceAtRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptOnDeathConsumeRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptActorEventConsumeRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptStartRequest>(runtime);
            EntityManager.AddBuffer<MorrowindScriptStopRequest>(runtime);
            EntityManager.AddComponentData(runtime, new MorrowindMagicRuntimeState
            {
                RandomState = 0xA5C38F2Du,
                NextActiveSpellId = 1,
            });
            ActiveExplicitRefLookupLifecycleUtility.CreateOrRepairForBootstrap(EntityManager);
            Debug.LogWarning($"Script runtime initialized");
            RuntimeBootstrapRequestUtility.Consume<MorrowindScriptRuntimeBootstrapRequest>(EntityManager);
        }

        void CreateOrRepairInterpreterScratch(Entity runtimeEntity)
        {
            if (!EntityManager.HasComponent<MorrowindScriptInterpreterScratch>(runtimeEntity))
            {
                EntityManager.AddComponentData(runtimeEntity, MorrowindScriptInterpreterScratch.Create(Allocator.Persistent));
                return;
            }

            var scratch = EntityManager.GetComponentData<MorrowindScriptInterpreterScratch>(runtimeEntity);
            if (scratch.IsCreated)
                return;

            scratch.Dispose();
            EntityManager.SetComponentData(runtimeEntity, MorrowindScriptInterpreterScratch.Create(Allocator.Persistent));
        }

        static byte ResolveGlobalKind(ref RuntimeGenericRecordDefBlob global)
        {
            string name = global.Name.ToString();
            if (!string.IsNullOrWhiteSpace(name) && name[0] == 'f')
                return (byte)MorrowindScriptValueKind.Float;

            return (byte)MorrowindScriptValueKind.Integer;
        }
    }
}
