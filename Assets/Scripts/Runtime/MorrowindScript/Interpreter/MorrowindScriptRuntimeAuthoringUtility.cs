using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;

namespace VVardenfell.Runtime.MorrowindScript
{
    public static class MorrowindScriptRuntimeAuthoringUtility
    {
        public static bool TryQueueObjectScript(
            ref EntityCommandBuffer ecb,
            Entity logicalEntity,
            RuntimeContentDatabase contentDb,
            string scriptId)
        {
            if (contentDb == null || string.IsNullOrWhiteSpace(scriptId))
                return false;

            if (!contentDb.TryGetMorrowindScriptProgramHandle(scriptId, out var programHandle) || !programHandle.IsValid)
                return false;

            ref readonly var program = ref contentDb.Get(programHandle);
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
                    : new FixedString128Bytes(program.DisabledReason ?? string.Empty),
            };

            ecb.AddComponent(logicalEntity, instance);

            var locals = contentDb.GetMorrowindScriptLocals(programHandle);
            if (locals.Length > 0)
            {
                var localBuffer = ecb.AddBuffer<MorrowindScriptLocalValue>(logicalEntity);
                for (int i = 0; i < locals.Length; i++)
                {
                    byte valueKind = locals[i].ValueKind;
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
    }
}
