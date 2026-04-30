using System.Text;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindWorldMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial class MorrowindScriptAudioDiagnosticsSystem : SystemBase
    {
        const double LogIntervalSeconds = 5.0;
        const int DetailLimit = 12;

        EntityQuery _scriptQuery;
        EntityQuery _requestQuery;
        double _nextLogTime;
        int _lastRunningAudioScripts = -1;
        int _lastPendingRequests = -1;
        int _lastDisabledAudioScripts = -1;
        int _lastFaultedAudioScripts = -1;

        protected override void OnCreate()
        {
            _scriptQuery = GetEntityQuery(ComponentType.ReadOnly<MorrowindScriptInstance>());
            _requestQuery = GetEntityQuery(ComponentType.ReadOnly<MorrowindScriptAudioRequest>());
        }

        protected override void OnUpdate()
        {
            var contentDb = RuntimeContentDatabase.Active;
            if (contentDb == null)
                return;

            int totalScripts = 0;
            int audioScripts = 0;
            int runningAudioScripts = 0;
            int disabledAudioScripts = 0;
            int faultedAudioScripts = 0;
            int pendingRequests = _requestQuery.CalculateEntityCount();
            int pendingLoopRequests = CountPendingLoopRequests();
            var detail = new StringBuilder(1024);

            using var entities = _scriptQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                if (!EntityManager.HasComponent<MorrowindScriptInstance>(entity))
                    continue;

                totalScripts++;
                var instance = EntityManager.GetComponentData<MorrowindScriptInstance>(entity);
                if (!TryGetProgram(contentDb, instance, out var program, out string programId))
                    continue;

                bool hasAudio = ProgramHasAudio(contentDb.Data, program) || LooksLikeAudioProgram(program);
                if (!hasAudio)
                    continue;

                audioScripts++;
                var status = (MorrowindScriptInstanceStatus)instance.Status;
                if (status == MorrowindScriptInstanceStatus.Running)
                    runningAudioScripts++;
                else if (status == MorrowindScriptInstanceStatus.Disabled)
                    disabledAudioScripts++;
                else if (status == MorrowindScriptInstanceStatus.Faulted)
                    faultedAudioScripts++;

                if (detail.Length < 1 || CountDetailRows(detail) < DetailLimit)
                    AppendScriptDetail(detail, entity, instance, program, programId);
            }

            bool changed = runningAudioScripts != _lastRunningAudioScripts
                || pendingRequests != _lastPendingRequests
                || disabledAudioScripts != _lastDisabledAudioScripts
                || faultedAudioScripts != _lastFaultedAudioScripts;
            if (!changed && SystemAPI.Time.ElapsedTime < _nextLogTime)
                return;

            _nextLogTime = SystemAPI.Time.ElapsedTime + LogIntervalSeconds;
            _lastRunningAudioScripts = runningAudioScripts;
            _lastPendingRequests = pendingRequests;
            _lastDisabledAudioScripts = disabledAudioScripts;
            _lastFaultedAudioScripts = faultedAudioScripts;

            string region = FormatRegionState();
            string weather = FormatWeatherState();
            Debug.Log(
                $"[VVardenfell][MWScript][AudioDiag] totalScriptInstances={totalScripts} audioScriptInstances={audioScripts} runningAudioScripts={runningAudioScripts} disabledAudioScripts={disabledAudioScripts} faultedAudioScripts={faultedAudioScripts} pendingAudioRequests={pendingRequests} pendingLoopRequests={pendingLoopRequests} {region} {weather}\n{detail}");
        }

        int CountPendingLoopRequests()
        {
            int count = 0;
            foreach (var request in SystemAPI.Query<RefRO<MorrowindScriptAudioRequest>>())
            {
                if (request.ValueRO.Looping != 0)
                    count++;
            }

            return count;
        }

        static bool TryGetProgram(
            RuntimeContentDatabase contentDb,
            in MorrowindScriptInstance instance,
            out MorrowindScriptProgramDef program,
            out string programId)
        {
            program = default;
            programId = string.Empty;
            var programs = contentDb.Data.MorrowindScriptPrograms;
            if (programs == null || (uint)instance.ProgramIndex >= (uint)programs.Length)
                return false;

            program = programs[instance.ProgramIndex];
            programId = program.Id ?? string.Empty;
            return true;
        }

        static bool ProgramHasAudio(GameplayContentData data, in MorrowindScriptProgramDef program)
        {
            if (program.FirstInstructionIndex < 0 || program.InstructionCount <= 0 || data.MorrowindScriptInstructions == null)
                return false;

            int end = program.FirstInstructionIndex + program.InstructionCount;
            end = end > data.MorrowindScriptInstructions.Length ? data.MorrowindScriptInstructions.Length : end;
            for (int i = program.FirstInstructionIndex; i < end; i++)
            {
                if (data.MorrowindScriptInstructions[i].Opcode == (byte)MorrowindScriptOpcode.EmitAudioRequest)
                    return true;
            }

            return false;
        }

        static bool LooksLikeAudioProgram(in MorrowindScriptProgramDef program)
        {
            string id = program.Id ?? string.Empty;
            string reason = program.DisabledReason ?? string.Empty;
            return id.IndexOf("sound", System.StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("amb", System.StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("lava", System.StringComparison.OrdinalIgnoreCase) >= 0
                || reason.IndexOf("sound", System.StringComparison.OrdinalIgnoreCase) >= 0
                || reason.IndexOf("play", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void AppendScriptDetail(
            StringBuilder builder,
            Entity entity,
            in MorrowindScriptInstance instance,
            in MorrowindScriptProgramDef program,
            string programId)
        {
            uint placedRef = EntityManager.HasComponent<PlacedRefIdentity>(entity)
                ? EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                : 0u;
            string location = EntityManager.HasComponent<LogicalRefLocation>(entity)
                ? FormatLocation(EntityManager.GetComponentData<LogicalRefLocation>(entity))
                : "location=unknown";
            builder.Append("  entity=").Append(entity)
                .Append(" placedRef=0x").Append(placedRef.ToString("X8"))
                .Append(' ').Append(location)
                .Append(" script=").Append(programId)
                .Append(" status=").Append((MorrowindScriptInstanceStatus)instance.Status)
                .Append(" instructions=").Append(program.InstructionCount)
                .Append(" disabledReason=").Append(instance.DisabledReason)
                .Append('\n');
        }

        static string FormatLocation(in LogicalRefLocation location)
        {
            return location.IsInterior != 0
                ? $"location=interior:{location.InteriorCellId}"
                : $"location=exterior:{location.ExteriorCell.x},{location.ExteriorCell.y}";
        }

        string FormatRegionState()
        {
            if (!SystemAPI.HasSingleton<RegionAmbientState>())
                return "regionAmbient=missing";

            var region = SystemAPI.GetSingleton<RegionAmbientState>();
            return $"regionAmbient region={region.Region.Value} pendingSound={region.PendingEventSound.Value} sequence={region.EventSequence}";
        }

        string FormatWeatherState()
        {
            if (!SystemAPI.HasSingleton<WeatherAudioState>())
                return "weatherAmbient=missing";

            var weather = SystemAPI.GetSingleton<WeatherAudioState>();
            return $"weatherAmbient loop={weather.ResolvedLoopSound.Value} next={weather.ResolvedNextLoopSound.Value} currentVol={weather.CurrentLoopVolume:0.###} nextVol={weather.NextLoopVolume:0.###}";
        }

        static int CountDetailRows(StringBuilder builder)
        {
            int count = 0;
            for (int i = 0; i < builder.Length; i++)
            {
                if (builder[i] == '\n')
                    count++;
            }

            return count;
        }
    }
}
