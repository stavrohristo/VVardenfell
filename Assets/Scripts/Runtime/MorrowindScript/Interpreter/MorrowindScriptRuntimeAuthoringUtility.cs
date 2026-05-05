using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.MorrowindScript
{
    public static class MorrowindScriptRuntimeAuthoringUtility
    {
        public static bool TryQueueObjectScript(
            ref EntityCommandBuffer ecb,
            Entity logicalEntity,
            ref RuntimeContentBlob content,
            string scriptId)
        {
            if (string.IsNullOrWhiteSpace(scriptId))
                return false;

            return TryQueueObjectScriptByIdHash(ref ecb, logicalEntity, ref content, RuntimeContentStableHash.HashId(scriptId));
        }

        public static bool TryQueueObjectScriptByIdHash(
            ref EntityCommandBuffer ecb,
            Entity logicalEntity,
            ref RuntimeContentBlob content,
            ulong scriptIdHash)
        {
            if (scriptIdHash == 0UL)
                return false;

            if (!RuntimeContentBlobUtility.TryGetMorrowindScriptProgramHandleByIdHash(ref content, scriptIdHash, out var programHandle) || !programHandle.IsValid)
                return false;

            ref RuntimeMorrowindScriptProgramDefBlob program = ref RuntimeContentBlobUtility.Get(ref content, programHandle);
            var status = (MorrowindScriptProgramStatus)program.Status;
            var instance = new MorrowindScriptInstance
            {
                Program = programHandle,
                ProgramIndex = programHandle.Index,
                ProgramCounter = 0,
                Status = status == MorrowindScriptProgramStatus.Compiled
                    ? (byte)MorrowindScriptInstanceStatus.Running
                    : (byte)MorrowindScriptInstanceStatus.Disabled,
                DisabledReason = status == MorrowindScriptProgramStatus.Compiled
                    ? default
                    : RuntimeFixedStringUtility.ToFixed128OrDefault(ref program.DisabledReason),
            };

            ecb.AddComponent(logicalEntity, instance);
            RuntimeContentBlobUtility.RequireRange(program.FirstLocalIndex, program.LocalCount, content.MorrowindScriptLocals.Length, "script local");
            if (program.LocalCount > 0)
            {
                var localBuffer = ecb.AddBuffer<MorrowindScriptLocalValue>(logicalEntity);
                for (int i = 0; i < program.LocalCount; i++)
                {
                    byte valueKind = content.MorrowindScriptLocals[program.FirstLocalIndex + i].ValueKind;
                    localBuffer.Add(new MorrowindScriptLocalValue
                    {
                        ValueKind = valueKind,
                    });
                }
            }

            if (status != MorrowindScriptProgramStatus.Compiled)
                return true;

            ecb.AddBuffer<MorrowindScriptStackValue>(logicalEntity);
            return true;
        }

        public static void AddRuntimeScriptBuffers(
            EntityManager entityManager,
            Entity entity,
            ref RuntimeContentBlob content,
            MorrowindScriptProgramDefHandle programHandle)
        {
            ref RuntimeMorrowindScriptProgramDefBlob program = ref RuntimeContentBlobUtility.Get(ref content, programHandle);
            RuntimeContentBlobUtility.RequireRange(program.FirstLocalIndex, program.LocalCount, content.MorrowindScriptLocals.Length, "script local");
            var localBuffer = entityManager.AddBuffer<MorrowindScriptLocalValue>(entity);
            for (int i = 0; i < program.LocalCount; i++)
            {
                localBuffer.Add(new MorrowindScriptLocalValue
                {
                    ValueKind = content.MorrowindScriptLocals[program.FirstLocalIndex + i].ValueKind,
                });
            }

            entityManager.AddBuffer<MorrowindScriptStackValue>(entity);
        }

        public static void EnsureRuntimeScriptBuffers(
            EntityManager entityManager,
            Entity entity,
            ref RuntimeContentBlob content,
            MorrowindScriptProgramDefHandle programHandle)
        {
            ref RuntimeMorrowindScriptProgramDefBlob program = ref RuntimeContentBlobUtility.Get(ref content, programHandle);
            RuntimeContentBlobUtility.RequireRange(program.FirstLocalIndex, program.LocalCount, content.MorrowindScriptLocals.Length, "script local");
            if (!entityManager.HasBuffer<MorrowindScriptLocalValue>(entity))
            {
                var localBuffer = entityManager.AddBuffer<MorrowindScriptLocalValue>(entity);
                for (int i = 0; i < program.LocalCount; i++)
                {
                    localBuffer.Add(new MorrowindScriptLocalValue
                    {
                        ValueKind = content.MorrowindScriptLocals[program.FirstLocalIndex + i].ValueKind,
                    });
                }
            }

            if (!entityManager.HasBuffer<MorrowindScriptStackValue>(entity))
                entityManager.AddBuffer<MorrowindScriptStackValue>(entity);
        }
    }
}
