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

            EntityManager.AddComponentData(runtime, new MorrowindQuestJournalState
            {
                QuestCount = contentDb.DialogueCount,
            });
            var journal = EntityManager.AddBuffer<MorrowindQuestJournalIndex>(runtime);
            journal.ResizeUninitialized(contentDb.DialogueCount);
            for (int i = 0; i < journal.Length; i++)
                journal[i] = default;

            EntityManager.AddBuffer<MorrowindScriptActiveSource>(runtime);
            EntityManager.AddBuffer<MorrowindScriptPlayingSound>(runtime);
        }

        static byte ResolveGlobalKind(in GenericRecordDef global)
        {
            if (!string.IsNullOrWhiteSpace(global.Name) && global.Name[0] == 'f')
                return (byte)MorrowindScriptValueKind.Float;

            return (byte)MorrowindScriptValueKind.Integer;
        }
    }
}
