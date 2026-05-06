using Unity.Collections;
using Unity.Entities;
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
    public partial struct MorrowindScriptRuntimeBootstrapSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindScriptRuntimeBootstrapRequest>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            if (SystemAPI.HasSingleton<MorrowindScriptRuntimeState>())
            {
                Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
                EnsureSessionTeardown(ref systemState, runtimeEntity);
                CreateOrRepairScriptCatalog(ref systemState, runtimeEntity);
                CreateOrRepairInterpreterScratch(ref systemState, runtimeEntity);
                RuntimeBootstrapUtility.EnsureComponent(systemState.EntityManager, runtimeEntity, new MorrowindMagicRuntimeState
                {
                    RandomState = 0xA5C38F2Du,
                    NextActiveSpellId = 1,
                });
                RuntimeBootstrapUtility.EnsureBuffer<ActorSpellCastRequest>(systemState.EntityManager, runtimeEntity);
                RuntimeBootstrapUtility.EnsureBuffer<ActorAiPassiveGreetingSayRequest>(systemState.EntityManager, runtimeEntity);
                RuntimeBootstrapUtility.EnsureBuffer<MorrowindCombatHitVoiceResolveRequest>(systemState.EntityManager, runtimeEntity);
                RuntimeBootstrapUtility.EnsureBuffer<MorrowindCombatHitVoiceSayRequest>(systemState.EntityManager, runtimeEntity);
                ActiveExplicitRefLookupLifecycleUtility.CreateOrRepairForBootstrap(systemState.EntityManager);
                RuntimeBootstrapRequestUtility.Consume<MorrowindScriptRuntimeBootstrapRequest>(systemState.EntityManager);
                return;
            }

            if (!SystemAPI.HasSingleton<RuntimeContentBlobReference>())
                return;

            BlobAssetReference<RuntimeContentBlob> contentBlob = SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob;
            if (!contentBlob.IsCreated)
                return;

            ref RuntimeContentBlob content = ref contentBlob.Value;

            Entity runtime = systemState.EntityManager.CreateEntity(
                typeof(MorrowindScriptRuntimeState),
                typeof(MorrowindScriptRuntimeCatalog),
                typeof(SessionTeardown));
            systemState.EntityManager.SetName(runtime, "VVardenfell.MorrowindScriptRuntime");
            systemState.EntityManager.SetComponentData(runtime, new MorrowindScriptRuntimeState
            {
                NextAudioRequestSequence = 1u,
                RandomState = 0x6E624EB7u,
            });
            systemState.EntityManager.SetComponentData(runtime, MorrowindScriptRuntimeCatalog.Create(contentBlob));
            systemState.EntityManager.SetComponentEnabled<SessionTeardown>(runtime, false);
            CreateOrRepairInterpreterScratch(ref systemState, runtime);

            var globals = systemState.EntityManager.AddBuffer<MorrowindScriptGlobalValue>(runtime);
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

            var deathCounts = systemState.EntityManager.AddBuffer<MorrowindActorDeathCount>(runtime);
            deathCounts.ResizeUninitialized(content.Actors.Length);
            for (int i = 0; i < deathCounts.Length; i++)
                deathCounts[i] = default;

            systemState.EntityManager.AddComponentData(runtime, new MorrowindQuestJournalState
            {
                QuestCount = content.Dialogues.Length,
                NextEntrySequence = 1u,
            });
            var journal = systemState.EntityManager.AddBuffer<MorrowindQuestJournalIndex>(runtime);
            journal.ResizeUninitialized(content.Dialogues.Length);
            for (int i = 0; i < journal.Length; i++)
                journal[i] = default;
            systemState.EntityManager.AddBuffer<MorrowindQuestJournalEntry>(runtime);
            systemState.EntityManager.AddBuffer<MorrowindQuestJournalRequest>(runtime);

            systemState.EntityManager.AddComponentData(runtime, new MorrowindDialogueState
            {
                DialogueCount = content.Dialogues.Length,
                NextTopicEntrySequence = 1u,
                NextSessionSequence = 1u,
            });
            var topics = systemState.EntityManager.AddBuffer<MorrowindKnownDialogueTopic>(runtime);
            topics.ResizeUninitialized(content.Dialogues.Length);
            for (int i = 0; i < topics.Length; i++)
                topics[i] = default;
            systemState.EntityManager.AddBuffer<MorrowindTopicJournalEntry>(runtime);
            systemState.EntityManager.AddBuffer<MorrowindFactionReactionOverride>(runtime);
            systemState.EntityManager.AddBuffer<MorrowindDialogueRequest>(runtime);

            systemState.EntityManager.AddBuffer<MorrowindScriptActiveSource>(runtime);
            systemState.EntityManager.AddBuffer<MorrowindScriptPlayingSound>(runtime);
            systemState.EntityManager.AddBuffer<MorrowindScriptActiveSay>(runtime);
            systemState.EntityManager.AddBuffer<MorrowindScriptRefStateRequest>(runtime);
            systemState.EntityManager.AddBuffer<PlacedRefLockRequest>(runtime);
            systemState.EntityManager.AddBuffer<MorrowindScriptTransformRequest>(runtime);
            systemState.EntityManager.AddBuffer<MorrowindScriptAiPackageRequest>(runtime);
            systemState.EntityManager.AddBuffer<MorrowindScriptActorAiSettingRequest>(runtime);
            systemState.EntityManager.AddBuffer<MorrowindScriptDispositionRequest>(runtime);
            systemState.EntityManager.AddBuffer<MorrowindScriptActorVitalRequest>(runtime);
            systemState.EntityManager.AddBuffer<MorrowindScriptHurtStandingActorRequest>(runtime);
            systemState.EntityManager.AddBuffer<MorrowindScriptAnimationGroupRequest>(runtime);
            systemState.EntityManager.AddBuffer<MorrowindScriptInventoryMutationRequest>(runtime);
            systemState.EntityManager.AddBuffer<ActorInventoryDropRequest>(runtime);
            systemState.EntityManager.AddBuffer<ScriptedCastRequest>(runtime);
            systemState.EntityManager.AddBuffer<ActorSpellCastRequest>(runtime);
            systemState.EntityManager.AddBuffer<ActorSpellMutationRequest>(runtime);
            systemState.EntityManager.AddBuffer<ActorForceGreetingRequest>(runtime);
            systemState.EntityManager.AddBuffer<PlayerReputationMutationRequest>(runtime);
            systemState.EntityManager.AddBuffer<ActorAttributeMutationRequest>(runtime);
            systemState.EntityManager.AddBuffer<PlayerSkillMutationRequest>(runtime);
            systemState.EntityManager.AddBuffer<PlayerFactionMutationRequest>(runtime);
            systemState.EntityManager.AddBuffer<ActorFactionRankMutationRequest>(runtime);
            systemState.EntityManager.AddBuffer<MorrowindScriptSayRequest>(runtime);
            systemState.EntityManager.AddBuffer<ActorAiPassiveGreetingSayRequest>(runtime);
            systemState.EntityManager.AddBuffer<MorrowindCombatHitVoiceResolveRequest>(runtime);
            systemState.EntityManager.AddBuffer<MorrowindCombatHitVoiceSayRequest>(runtime);
            systemState.EntityManager.AddBuffer<MorrowindScriptActorLocalSetRequest>(runtime);
            systemState.EntityManager.AddBuffer<MorrowindScriptFactionReactionRequest>(runtime);
            systemState.EntityManager.AddBuffer<ShellMessageBoxRequest>(runtime);
            systemState.EntityManager.AddBuffer<GlobalMapRevealRequest>(runtime);
            systemState.EntityManager.AddBuffer<MorrowindScriptShellRequest>(runtime);
            systemState.EntityManager.AddBuffer<MorrowindScriptJailRequest>(runtime);
            systemState.EntityManager.AddBuffer<MorrowindScriptMovementFlagRequest>(runtime);
            systemState.EntityManager.AddBuffer<MorrowindScriptPlaceAtRequest>(runtime);
            systemState.EntityManager.AddBuffer<MorrowindScriptOnDeathConsumeRequest>(runtime);
            systemState.EntityManager.AddBuffer<MorrowindScriptActorEventConsumeRequest>(runtime);
            systemState.EntityManager.AddBuffer<MorrowindScriptStartRequest>(runtime);
            systemState.EntityManager.AddBuffer<MorrowindScriptStopRequest>(runtime);
            systemState.EntityManager.AddComponentData(runtime, new MorrowindMagicRuntimeState
            {
                RandomState = 0xA5C38F2Du,
                NextActiveSpellId = 1,
            });
            ActiveExplicitRefLookupLifecycleUtility.CreateOrRepairForBootstrap(systemState.EntityManager);
            RuntimeBootstrapRequestUtility.Consume<MorrowindScriptRuntimeBootstrapRequest>(systemState.EntityManager);
        }

        void CreateOrRepairScriptCatalog(ref SystemState systemState, Entity runtimeEntity)
        {
            if (systemState.EntityManager.HasComponent<MorrowindScriptRuntimeCatalog>(runtimeEntity))
            {
                var catalog = systemState.EntityManager.GetComponentData<MorrowindScriptRuntimeCatalog>(runtimeEntity);
                if (catalog.IsCreated)
                    return;

                catalog.Dispose();
                systemState.EntityManager.SetComponentData(runtimeEntity, default(MorrowindScriptRuntimeCatalog));
            }

            if (!SystemAPI.HasSingleton<RuntimeContentBlobReference>())
                throw new System.InvalidOperationException("[VVardenfell][MWScript] Script runtime catalog repair requires runtime content blob.");

            BlobAssetReference<RuntimeContentBlob> contentBlob = SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob;
            if (!contentBlob.IsCreated)
                throw new System.InvalidOperationException("[VVardenfell][MWScript] Script runtime catalog repair requires created runtime content blob.");

            var repairedCatalog = MorrowindScriptRuntimeCatalog.Create(contentBlob);
            if (systemState.EntityManager.HasComponent<MorrowindScriptRuntimeCatalog>(runtimeEntity))
            {
                systemState.EntityManager.SetComponentData(runtimeEntity, repairedCatalog);
                return;
            }

            systemState.EntityManager.AddComponentData(runtimeEntity, repairedCatalog);
        }

        void EnsureSessionTeardown(ref SystemState systemState, Entity runtimeEntity)
        {
            if (!systemState.EntityManager.HasComponent<SessionTeardown>(runtimeEntity))
                systemState.EntityManager.AddComponent<SessionTeardown>(runtimeEntity);
            systemState.EntityManager.SetComponentEnabled<SessionTeardown>(runtimeEntity, false);
        }

        void CreateOrRepairInterpreterScratch(ref SystemState systemState, Entity runtimeEntity)
        {
            if (!systemState.EntityManager.HasComponent<MorrowindScriptInterpreterScratch>(runtimeEntity))
            {
                systemState.EntityManager.AddComponentData(runtimeEntity, MorrowindScriptInterpreterScratch.Create(Allocator.Persistent));
                return;
            }

            var scratch = systemState.EntityManager.GetComponentData<MorrowindScriptInterpreterScratch>(runtimeEntity);
            if (scratch.IsCreated)
                return;

            scratch.Dispose();
            systemState.EntityManager.SetComponentData(runtimeEntity, MorrowindScriptInterpreterScratch.Create(Allocator.Persistent));
        }

        static byte ResolveGlobalKind(ref RuntimeGenericRecordDefBlob global)
        {
            FixedString64Bytes name = RuntimeFixedStringUtility.ToFixed64OrDefault(ref global.Name);
            if (!name.IsEmpty && name[0] == (byte)'f')
                return (byte)MorrowindScriptValueKind.Float;

            return (byte)MorrowindScriptValueKind.Integer;
        }
    }
}
