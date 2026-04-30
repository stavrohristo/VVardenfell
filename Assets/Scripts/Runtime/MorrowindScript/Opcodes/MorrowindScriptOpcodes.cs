using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.MorrowindScript
{
    public unsafe delegate void MorrowindScriptOpcodeDelegate(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction);

    public unsafe struct MorrowindScriptExecutionContext
    {
        public Entity Entity;
        public EntityCommandBuffer.ParallelWriter Ecb;
        public int SortKey;
        public int ProgramCounter;
        public int StackLength;
        public int StackCapacity;
        public MorrowindScriptStackValue* Stack;
        public MorrowindScriptLocalValue* Locals;
        public int LocalCount;
        public MorrowindScriptGlobalValue* Globals;
        public int GlobalCount;
        public float3 Position;
        public uint PlacedRefId;
        public uint AudioSequenceBase;
        public byte Halted;
        public byte Faulted;
    }

    [BurstCompile]
    public static unsafe class MorrowindScriptOpcodeTable
    {
        const int OpcodeCount = 23;

        public static NativeArray<FunctionPointer<MorrowindScriptOpcodeDelegate>> CreateHandlers(Allocator allocator)
        {
            var handlers = new NativeArray<FunctionPointer<MorrowindScriptOpcodeDelegate>>(OpcodeCount, allocator);
            var unsupported = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(Unsupported);
            for (int i = 0; i < handlers.Length; i++)
                handlers[i] = unsupported;

            handlers[(int)MorrowindScriptOpcode.Nop] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(Nop);
            handlers[(int)MorrowindScriptOpcode.Return] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(Return);
            handlers[(int)MorrowindScriptOpcode.PushInt] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(PushInt);
            handlers[(int)MorrowindScriptOpcode.PushFloat] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(PushFloat);
            handlers[(int)MorrowindScriptOpcode.GetLocal] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetLocal);
            handlers[(int)MorrowindScriptOpcode.SetLocalInt] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(SetLocalInt);
            handlers[(int)MorrowindScriptOpcode.SetLocalFloat] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(SetLocalFloat);
            handlers[(int)MorrowindScriptOpcode.GetGlobal] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(GetGlobal);
            handlers[(int)MorrowindScriptOpcode.SetGlobalInt] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(SetGlobalInt);
            handlers[(int)MorrowindScriptOpcode.SetGlobalFloat] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(SetGlobalFloat);
            handlers[(int)MorrowindScriptOpcode.Add] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(Add);
            handlers[(int)MorrowindScriptOpcode.Subtract] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(Subtract);
            handlers[(int)MorrowindScriptOpcode.Multiply] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(Multiply);
            handlers[(int)MorrowindScriptOpcode.Divide] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(Divide);
            handlers[(int)MorrowindScriptOpcode.CompareEqual] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(CompareEqual);
            handlers[(int)MorrowindScriptOpcode.CompareNotEqual] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(CompareNotEqual);
            handlers[(int)MorrowindScriptOpcode.CompareLess] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(CompareLess);
            handlers[(int)MorrowindScriptOpcode.CompareLessOrEqual] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(CompareLessOrEqual);
            handlers[(int)MorrowindScriptOpcode.CompareGreater] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(CompareGreater);
            handlers[(int)MorrowindScriptOpcode.CompareGreaterOrEqual] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(CompareGreaterOrEqual);
            handlers[(int)MorrowindScriptOpcode.Jump] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(Jump);
            handlers[(int)MorrowindScriptOpcode.JumpIfZero] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(JumpIfZero);
            handlers[(int)MorrowindScriptOpcode.EmitAudioRequest] = BurstCompiler.CompileFunctionPointer<MorrowindScriptOpcodeDelegate>(EmitAudioRequest);
            return handlers;
        }

        [BurstCompile]
        static void Unsupported(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction) => context->Faulted = 1;

        [BurstCompile]
        static void Nop(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction) { }

        [BurstCompile]
        static void Return(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction) => context->Halted = 1;

        [BurstCompile]
        static void PushInt(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = instruction->Int0,
                FloatValue = instruction->Int0,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        [BurstCompile]
        static void PushFloat(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = (int)instruction->Float0,
                FloatValue = instruction->Float0,
                ValueKind = (byte)MorrowindScriptValueKind.Float,
            });
        }

        [BurstCompile]
        static void GetLocal(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            int index = instruction->Int0;
            if ((uint)index >= (uint)context->LocalCount)
            {
                context->Faulted = 1;
                return;
            }

            var local = context->Locals[index];
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = local.IntValue,
                FloatValue = local.FloatValue,
                ValueKind = local.ValueKind,
            });
        }

        [BurstCompile]
        static void SetLocalInt(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!Pop(context, out var value) || !TryGetLocal(context, instruction->Int0, out var local))
                return;

            local.IntValue = value.ValueKind == (byte)MorrowindScriptValueKind.Float ? (int)value.FloatValue : value.IntValue;
            local.FloatValue = local.IntValue;
            local.ValueKind = (byte)MorrowindScriptValueKind.Integer;
            context->Locals[instruction->Int0] = local;
        }

        [BurstCompile]
        static void SetLocalFloat(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!Pop(context, out var value) || !TryGetLocal(context, instruction->Int0, out var local))
                return;

            local.FloatValue = ToFloat(value);
            local.IntValue = (int)local.FloatValue;
            local.ValueKind = (byte)MorrowindScriptValueKind.Float;
            context->Locals[instruction->Int0] = local;
        }

        [BurstCompile]
        static void GetGlobal(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            int index = instruction->Int0;
            if ((uint)index >= (uint)context->GlobalCount)
            {
                context->Faulted = 1;
                return;
            }

            var global = context->Globals[index];
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = global.IntValue,
                FloatValue = global.FloatValue,
                ValueKind = global.ValueKind,
            });
        }

        [BurstCompile]
        static void SetGlobalInt(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!Pop(context, out var value) || !TryGetGlobal(context, instruction->Int0, out var global))
                return;

            global.IntValue = value.ValueKind == (byte)MorrowindScriptValueKind.Float ? (int)value.FloatValue : value.IntValue;
            global.FloatValue = global.IntValue;
            global.ValueKind = (byte)MorrowindScriptValueKind.Integer;
            context->Globals[instruction->Int0] = global;
        }

        [BurstCompile]
        static void SetGlobalFloat(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!Pop(context, out var value) || !TryGetGlobal(context, instruction->Int0, out var global))
                return;

            global.FloatValue = ToFloat(value);
            global.IntValue = (int)global.FloatValue;
            global.ValueKind = (byte)MorrowindScriptValueKind.Float;
            context->Globals[instruction->Int0] = global;
        }

        [BurstCompile]
        static void Add(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction) => BinaryMath(context, 0);

        [BurstCompile]
        static void Subtract(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction) => BinaryMath(context, 1);

        [BurstCompile]
        static void Multiply(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction) => BinaryMath(context, 2);

        [BurstCompile]
        static void Divide(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction) => BinaryMath(context, 3);

        [BurstCompile]
        static void CompareEqual(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction) => Compare(context, 0);

        [BurstCompile]
        static void CompareNotEqual(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction) => Compare(context, 1);

        [BurstCompile]
        static void CompareLess(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction) => Compare(context, 2);

        [BurstCompile]
        static void CompareLessOrEqual(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction) => Compare(context, 3);

        [BurstCompile]
        static void CompareGreater(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction) => Compare(context, 4);

        [BurstCompile]
        static void CompareGreaterOrEqual(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction) => Compare(context, 5);

        [BurstCompile]
        static void Jump(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction) => context->ProgramCounter += instruction->Int0;

        [BurstCompile]
        static void JumpIfZero(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            if (!Pop(context, out var value))
                return;

            if (math.abs(ToFloat(value)) <= 0.000001f)
                context->ProgramCounter += instruction->Int0;
        }

        [BurstCompile]
        static void EmitAudioRequest(MorrowindScriptExecutionContext* context, MorrowindScriptInstructionRuntime* instruction)
        {
            var kind = (MorrowindScriptAudioKind)instruction->Operand0;
            Entity requestEntity = context->Ecb.CreateEntity(context->SortKey);
            context->Ecb.AddComponent(context->SortKey, requestEntity, new MorrowindScriptAudioRequest
            {
                Sequence = context->AudioSequenceBase + (uint)context->SortKey + 1u,
                Sound = new SoundDefHandle { Value = instruction->Int0 },
                SourceEntity = context->Entity,
                SourcePlacedRefId = context->PlacedRefId,
                Position = context->Position,
                Volume = instruction->Float0 <= 0f ? 1f : instruction->Float0,
                Pitch = instruction->Float1 <= 0f ? 1f : instruction->Float1,
                Kind = instruction->Operand0,
                Spatial = (byte)(kind == MorrowindScriptAudioKind.PlaySound ? 0 : 1),
                Looping = (byte)(kind == MorrowindScriptAudioKind.PlayLoopSound3D || kind == MorrowindScriptAudioKind.PlayLoopSound3DVP ? 1 : 0),
            });
        }

        static bool TryGetLocal(MorrowindScriptExecutionContext* context, int index, out MorrowindScriptLocalValue local)
        {
            local = default;
            if ((uint)index >= (uint)context->LocalCount)
            {
                context->Faulted = 1;
                return false;
            }

            local = context->Locals[index];
            return true;
        }

        static bool TryGetGlobal(MorrowindScriptExecutionContext* context, int index, out MorrowindScriptGlobalValue global)
        {
            global = default;
            if ((uint)index >= (uint)context->GlobalCount)
            {
                context->Faulted = 1;
                return false;
            }

            global = context->Globals[index];
            return true;
        }

        static void Push(MorrowindScriptExecutionContext* context, in MorrowindScriptStackValue value)
        {
            if (context->StackLength >= context->StackCapacity)
            {
                context->Faulted = 1;
                return;
            }

            context->Stack[context->StackLength++] = value;
        }

        static bool Pop(MorrowindScriptExecutionContext* context, out MorrowindScriptStackValue value)
        {
            value = default;
            if (context->StackLength <= 0)
            {
                context->Faulted = 1;
                return false;
            }

            value = context->Stack[--context->StackLength];
            return true;
        }

        static void BinaryMath(MorrowindScriptExecutionContext* context, byte operation)
        {
            if (!Pop(context, out var rhs) || !Pop(context, out var lhs))
                return;

            float left = ToFloat(lhs);
            float right = ToFloat(rhs);
            if (operation == 3 && math.abs(right) <= 0.000001f)
            {
                context->Faulted = 1;
                return;
            }

            float result = operation switch
            {
                0 => left + right,
                1 => left - right,
                2 => left * right,
                _ => left / right,
            };
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = (int)result,
                FloatValue = result,
                ValueKind = (byte)MorrowindScriptValueKind.Float,
            });
        }

        static void Compare(MorrowindScriptExecutionContext* context, byte operation)
        {
            if (!Pop(context, out var rhs) || !Pop(context, out var lhs))
                return;

            float left = ToFloat(lhs);
            float right = ToFloat(rhs);
            bool result = operation switch
            {
                0 => math.abs(left - right) <= 0.000001f,
                1 => math.abs(left - right) > 0.000001f,
                2 => left < right,
                3 => left <= right,
                4 => left > right,
                _ => left >= right,
            };
            Push(context, new MorrowindScriptStackValue
            {
                IntValue = result ? 1 : 0,
                FloatValue = result ? 1f : 0f,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            });
        }

        static float ToFloat(in MorrowindScriptStackValue value)
        {
            return value.ValueKind == (byte)MorrowindScriptValueKind.Float ? value.FloatValue : value.IntValue;
        }
    }
}
