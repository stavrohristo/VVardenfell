using System;
using Unity.Burst;
using Unity.Collections;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime;

namespace VVardenfell.Runtime.MorrowindScript
{
    public struct MorrowindScriptProgramRuntime
    {
        public byte Status;
        public int FirstInstructionIndex;
        public int InstructionCount;
        public int FirstLocalIndex;
        public int LocalCount;
        public int MaxStack;
    }

    public struct MorrowindScriptInstructionRuntime
    {
        public byte Opcode;
        public byte Operand0;
        public short Operand1;
        public int Int0;
        public int Int1;
        public int Int2;
        public float Float0;
        public float Float1;
        public float Float2;
        public float Float3;
    }

    public struct MorrowindScriptLocalRuntime
    {
        public byte ValueKind;
    }

    public sealed class MorrowindScriptRuntimeCatalog : IDisposable
    {
        public NativeArray<MorrowindScriptProgramRuntime> Programs;
        public NativeArray<MorrowindScriptInstructionRuntime> Instructions;
        public NativeArray<MorrowindScriptLocalRuntime> Locals;
        public NativeArray<FixedString512Bytes> Messages;
        public NativeArray<FunctionPointer<MorrowindScriptOpcodeDelegate>> OpcodeHandlers;

        public bool IsCreated => Programs.IsCreated && Instructions.IsCreated && Locals.IsCreated && Messages.IsCreated && OpcodeHandlers.IsCreated;

        public static MorrowindScriptRuntimeCatalog Create(GameplayContentData data)
        {
            data ??= new GameplayContentData();
            var catalog = new MorrowindScriptRuntimeCatalog
            {
                Programs = new NativeArray<MorrowindScriptProgramRuntime>(data.MorrowindScriptPrograms?.Length ?? 0, Allocator.Persistent),
                Instructions = new NativeArray<MorrowindScriptInstructionRuntime>(data.MorrowindScriptInstructions?.Length ?? 0, Allocator.Persistent),
                Locals = new NativeArray<MorrowindScriptLocalRuntime>(data.MorrowindScriptLocals?.Length ?? 0, Allocator.Persistent),
                Messages = new NativeArray<FixedString512Bytes>(data.MorrowindScriptMessages?.Length ?? 0, Allocator.Persistent),
                OpcodeHandlers = MorrowindScriptOpcodeTable.CreateHandlers(Allocator.Persistent),
            };

            for (int i = 0; i < catalog.Programs.Length; i++)
            {
                var source = data.MorrowindScriptPrograms[i];
                catalog.Programs[i] = new MorrowindScriptProgramRuntime
                {
                    Status = source.Status,
                    FirstInstructionIndex = source.FirstInstructionIndex,
                    InstructionCount = source.InstructionCount,
                    FirstLocalIndex = source.FirstLocalIndex,
                    LocalCount = source.LocalCount,
                    MaxStack = source.MaxStack,
                };
            }

            for (int i = 0; i < catalog.Instructions.Length; i++)
            {
                var source = data.MorrowindScriptInstructions[i];
                catalog.Instructions[i] = new MorrowindScriptInstructionRuntime
                {
                    Opcode = source.Opcode,
                    Operand0 = source.Operand0,
                    Operand1 = source.Operand1,
                    Int0 = source.Int0,
                    Int1 = source.Int1,
                    Int2 = source.Int2,
                    Float0 = source.Float0,
                    Float1 = source.Float1,
                    Float2 = source.Float2,
                    Float3 = source.Float3,
                };
            }

            for (int i = 0; i < catalog.Locals.Length; i++)
            {
                catalog.Locals[i] = new MorrowindScriptLocalRuntime
                {
                    ValueKind = data.MorrowindScriptLocals[i].ValueKind,
                };
            }

            for (int i = 0; i < catalog.Messages.Length; i++)
                catalog.Messages[i] = RuntimeFixedStringUtility.ToFixed512OrDefault(data.MorrowindScriptMessages[i].Text);

            return catalog;
        }

        public void Dispose()
        {
            if (Programs.IsCreated)
                Programs.Dispose();
            if (Instructions.IsCreated)
                Instructions.Dispose();
            if (Locals.IsCreated)
                Locals.Dispose();
            if (Messages.IsCreated)
                Messages.Dispose();
            if (OpcodeHandlers.IsCreated)
                OpcodeHandlers.Dispose();
        }
    }
}
