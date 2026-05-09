using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Combat
{
    public enum BattleSimulatorPhase : byte
    {
        Setup = 0,
        Spawning = 1,
        Running = 2,
        Complete = 3,
    }

    public enum BattleSimulatorTeamId : byte
    {
        None = 0,
        GroupA = 1,
        GroupB = 2,
    }

    public struct BattleSimulatorBootState : IComponentData
    {
        public int2 BattlegroundCell;
    }

    public struct BattleSimulatorState : IComponentData
    {
        public int2 BattlegroundCell;
        public byte Phase;
        public byte WinningTeam;
        public float StartedAt;
        public float CompletedAt;
        public int GroupATotal;
        public int GroupBTotal;
        public int GroupAAlive;
        public int GroupBAlive;
        public FixedString128Bytes Status;
    }

    public struct BattleSimulatorSpawnRequest : IBufferElementData
    {
        public byte Team;
        public ActorDefHandle Actor;
        public int Count;
    }

    public struct BattleSimulatorResetRequest : IComponentData, IEnableableComponent
    {
    }

    public struct BattleSimulatorSetupUiActive : IComponentData
    {
    }

    public struct BattleSimulatorTeam : IComponentData
    {
        public byte Value;
    }

    public struct BattleSimulatorUnitTag : IComponentData
    {
    }
}
