using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
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
        public ulong RequirementMask;
    }

    [Flags]
    public enum MorrowindScriptRequirementMask : ulong
    {
        None = 0,
        PlayingSounds = 1UL << 0,
        ActiveSays = 1UL << 1,
        ActivationEvents = 1UL << 2,
        PlayerInventory = 1UL << 3,
        PlayerKnownSpells = 1UL << 4,
        PlayerFactions = 1UL << 5,
        PlayerSkills = 1UL << 6,
        PlayerCrime = 1UL << 7,
        ExternalActorLocals = 1UL << 8,
        ActorAiStatuses = 1UL << 9,
        ActorCombatTargets = 1UL << 10,
        LockStates = 1UL << 13,
        ActorEvents = 1UL << 16,
        ActorVitals = 1UL << 17,
        ActorAttributes = 1UL << 18,
        ActorActiveEffects = 1UL << 19,
        ActorDiseases = 1UL << 20,
        ActorIdentities = 1UL << 21,
        ActorAiSettings = 1UL << 22,
        ActorDispositions = 1UL << 23,
        ActorLineOfSight = 1UL << 24,
        ActorKnownSpellSnapshots = 1UL << 25,
        RunningPrograms = 1UL << 26,
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
        public NativeArray<FixedString128Bytes> ProgramIds;
        public NativeArray<MorrowindScriptInstructionRuntime> Instructions;
        public NativeArray<MorrowindScriptLocalRuntime> Locals;
        public NativeArray<FixedString512Bytes> Messages;
        public NativeArray<FunctionPointer<MorrowindScriptOpcodeDelegate>> OpcodeHandlers;

        public bool IsCreated => Programs.IsCreated && ProgramIds.IsCreated && Instructions.IsCreated && Locals.IsCreated && Messages.IsCreated && OpcodeHandlers.IsCreated;

        public static MorrowindScriptRuntimeCatalog Create(BlobAssetReference<RuntimeContentBlob> contentBlob)
        {
            if (!contentBlob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][MWScript][Validation] Runtime content blob is not loaded.");

            ref RuntimeContentBlob data = ref contentBlob.Value;
            var catalog = new MorrowindScriptRuntimeCatalog
            {
                Programs = new NativeArray<MorrowindScriptProgramRuntime>(data.MorrowindScriptPrograms.Length, Allocator.Persistent),
                ProgramIds = new NativeArray<FixedString128Bytes>(data.MorrowindScriptPrograms.Length, Allocator.Persistent),
                Instructions = new NativeArray<MorrowindScriptInstructionRuntime>(data.MorrowindScriptInstructions.Length, Allocator.Persistent),
                Locals = new NativeArray<MorrowindScriptLocalRuntime>(data.MorrowindScriptLocals.Length, Allocator.Persistent),
                Messages = new NativeArray<FixedString512Bytes>(data.MorrowindScriptMessages.Length, Allocator.Persistent),
                OpcodeHandlers = MorrowindScriptOpcodeTable.CreateHandlers(Allocator.Persistent),
            };

            for (int i = 0; i < catalog.Programs.Length; i++)
            {
                ref RuntimeMorrowindScriptProgramDefBlob source = ref data.MorrowindScriptPrograms[i];
                ValidateProgram(ref data, i, ref source);
                catalog.ProgramIds[i] = RuntimeFixedStringUtility.ToFixed128OrDefault(ref source.Id);
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
                ValidateInstruction(i, source);
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
                catalog.Messages[i] = RuntimeFixedStringUtility.ToFixed512OrDefault(ref data.MorrowindScriptMessages[i].Text);

            for (int i = 0; i < catalog.Programs.Length; i++)
            {
                var program = catalog.Programs[i];
                program.RequirementMask = (ulong)CalculateRequirementMask(catalog.Instructions, program);
                catalog.Programs[i] = program;
            }

            return catalog;
        }

        static MorrowindScriptRequirementMask CalculateRequirementMask(
            NativeArray<MorrowindScriptInstructionRuntime> instructions,
            in MorrowindScriptProgramRuntime program)
        {
            if (program.Status != (byte)MorrowindScriptProgramStatus.Compiled || program.InstructionCount <= 0)
                return MorrowindScriptRequirementMask.None;

            var mask = MorrowindScriptRequirementMask.None;
            int end = program.FirstInstructionIndex + program.InstructionCount;
            for (int i = program.FirstInstructionIndex; i < end; i++)
                mask |= GetInstructionRequirementMask((MorrowindScriptOpcode)instructions[i].Opcode);

            return mask;
        }

        public static MorrowindScriptRequirementMask GetInstructionRequirementMask(MorrowindScriptOpcode opcode)
        {
            switch (opcode)
            {
                case MorrowindScriptOpcode.GetSoundPlaying:
                    return MorrowindScriptRequirementMask.PlayingSounds;
                case MorrowindScriptOpcode.SayDone:
                    return MorrowindScriptRequirementMask.ActiveSays;
                case MorrowindScriptOpcode.GetOnActivate:
                case MorrowindScriptOpcode.OnActivateStatement:
                case MorrowindScriptOpcode.Activate:
                    return MorrowindScriptRequirementMask.ActivationEvents;
                case MorrowindScriptOpcode.GetPlayerItemCount:
                case MorrowindScriptOpcode.HasSoulGem:
                    return MorrowindScriptRequirementMask.PlayerInventory;
                case MorrowindScriptOpcode.GetPlayerSpell:
                    return MorrowindScriptRequirementMask.PlayerKnownSpells;
                case MorrowindScriptOpcode.GetPCRank:
                case MorrowindScriptOpcode.PCExpelled:
                    return MorrowindScriptRequirementMask.PlayerFactions;
                case MorrowindScriptOpcode.GetPlayerSkill:
                    return MorrowindScriptRequirementMask.PlayerSkills;
                case MorrowindScriptOpcode.GetPCCrimeLevel:
                case MorrowindScriptOpcode.SetPCCrimeLevel:
                case MorrowindScriptOpcode.PayFine:
                    return MorrowindScriptRequirementMask.PlayerCrime;
                case MorrowindScriptOpcode.GetActorLocal:
                case MorrowindScriptOpcode.SetActorLocalInt:
                case MorrowindScriptOpcode.SetActorLocalFloat:
                    return MorrowindScriptRequirementMask.ExternalActorLocals;
                case MorrowindScriptOpcode.GetAiPackageDone:
                case MorrowindScriptOpcode.GetCurrentAiPackage:
                    return MorrowindScriptRequirementMask.ActorAiStatuses;
                case MorrowindScriptOpcode.GetTarget:
                    return MorrowindScriptRequirementMask.ActorCombatTargets;
                case MorrowindScriptOpcode.GetDistance:
                case MorrowindScriptOpcode.GetPos:
                case MorrowindScriptOpcode.GetAngle:
                case MorrowindScriptOpcode.SetPos:
                case MorrowindScriptOpcode.MoveWorld:
                case MorrowindScriptOpcode.Move:
                case MorrowindScriptOpcode.GetStartingAngle:
                    return MorrowindScriptRequirementMask.None;
                case MorrowindScriptOpcode.GetLocked:
                    return MorrowindScriptRequirementMask.LockStates;
                case MorrowindScriptOpcode.GetItemCount:
                case MorrowindScriptOpcode.GetOnDeath:
                    return MorrowindScriptRequirementMask.None;
                case MorrowindScriptOpcode.GetAttacked:
                case MorrowindScriptOpcode.OnMurder:
                case MorrowindScriptOpcode.OnKnockout:
                case MorrowindScriptOpcode.HitOnMe:
                    return MorrowindScriptRequirementMask.ActorEvents;
                case MorrowindScriptOpcode.GetHealth:
                    return MorrowindScriptRequirementMask.ActorVitals;
                case MorrowindScriptOpcode.GetActorAttribute:
                    return MorrowindScriptRequirementMask.ActorAttributes;
                case MorrowindScriptOpcode.GetEffect:
                case MorrowindScriptOpcode.GetSpellEffects:
                    return MorrowindScriptRequirementMask.ActorActiveEffects;
                case MorrowindScriptOpcode.GetCommonDisease:
                case MorrowindScriptOpcode.GetBlightDisease:
                    return MorrowindScriptRequirementMask.ActorDiseases;
                case MorrowindScriptOpcode.GetRace:
                    return MorrowindScriptRequirementMask.ActorIdentities;
                case MorrowindScriptOpcode.GetActorAiSetting:
                    return MorrowindScriptRequirementMask.ActorAiSettings;
                case MorrowindScriptOpcode.GetDisposition:
                    return MorrowindScriptRequirementMask.ActorDispositions;
                case MorrowindScriptOpcode.GetLOS:
                case MorrowindScriptOpcode.GetDetected:
                    return MorrowindScriptRequirementMask.ActorLineOfSight;
                case MorrowindScriptOpcode.GetSpell:
                    return MorrowindScriptRequirementMask.ActorKnownSpellSnapshots;
                case MorrowindScriptOpcode.ScriptRunning:
                    return MorrowindScriptRequirementMask.RunningPrograms;
                default:
                    return MorrowindScriptRequirementMask.None;
            }
        }

        static void ValidateProgram(ref RuntimeContentBlob data, int programIndex, ref RuntimeMorrowindScriptProgramDefBlob program)
        {
            if (program.IdHash == 0UL)
                throw new InvalidOperationException($"[VVardenfell][MWScript][Validation] script program {programIndex} has no id.");

            if (program.Status != (byte)MorrowindScriptProgramStatus.Compiled
                && program.Status != (byte)MorrowindScriptProgramStatus.DisabledUnsupported
                && program.Status != (byte)MorrowindScriptProgramStatus.FailedInvalid)
            {
                throw new InvalidOperationException(
                    $"[VVardenfell][MWScript][Validation] script hash {program.IdHash} has invalid status {program.Status}.");
            }

            if (program.MaxStack < 0)
                throw new InvalidOperationException($"[VVardenfell][MWScript][Validation] script hash {program.IdHash} has negative max stack {program.MaxStack}.");

            int localCount = data.MorrowindScriptLocals.Length;
            if (program.LocalCount < 0
                || program.FirstLocalIndex < -1
                || (program.LocalCount == 0 && program.FirstLocalIndex != -1 && program.Status == (byte)MorrowindScriptProgramStatus.Compiled)
                || (program.LocalCount > 0 && (program.FirstLocalIndex < 0 || program.FirstLocalIndex + program.LocalCount > localCount)))
            {
                throw new InvalidOperationException(
                    $"[VVardenfell][MWScript][Validation] script hash {program.IdHash} has invalid local range first={program.FirstLocalIndex} count={program.LocalCount} total={localCount}.");
            }

            if (program.Status != (byte)MorrowindScriptProgramStatus.Compiled)
                return;

            int instructionCount = data.MorrowindScriptInstructions.Length;
            if (program.InstructionCount == 0)
            {
                if (program.FirstInstructionIndex == -1)
                    return;

                throw new InvalidOperationException(
                    $"[VVardenfell][MWScript][Validation] script hash {program.IdHash} has invalid empty instruction range first={program.FirstInstructionIndex} count=0.");
            }

            if (program.FirstInstructionIndex < 0
                || program.InstructionCount < 0
                || program.FirstInstructionIndex + program.InstructionCount > instructionCount)
            {
                throw new InvalidOperationException(
                    $"[VVardenfell][MWScript][Validation] script hash {program.IdHash} has invalid instruction range first={program.FirstInstructionIndex} count={program.InstructionCount} total={instructionCount}.");
            }
        }

        static void ValidateInstruction(int instructionIndex, in MorrowindScriptInstructionDef instruction)
        {
            if (instruction.Opcode >= MorrowindScriptOpcodeTable.OpcodeCount)
            {
                throw new InvalidOperationException(
                    $"[VVardenfell][MWScript][Validation] instruction {instructionIndex} has unsupported opcode {instruction.Opcode}.");
            }
        }

        public void Dispose()
        {
            if (Programs.IsCreated)
                Programs.Dispose();
            if (ProgramIds.IsCreated)
                ProgramIds.Dispose();
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
