using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindGameplayMutationSystemGroup))]
    public partial struct BattleSimulatorCountSystem : ISystem
    {
        EntityQuery _unitQuery;
        ComponentTypeHandle<BattleSimulatorTeam> _teamHandle;
        ComponentTypeHandle<PlacedRefRuntimeState> _runtimeStateHandle;
        ComponentTypeHandle<ActorDead> _deadHandle;

        public void OnCreate(ref SystemState state)
        {
            _unitQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<BattleSimulatorUnitTag, BattleSimulatorTeam, PlacedRefRuntimeState, ActorDead>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .Build(ref state);

            _teamHandle = state.GetComponentTypeHandle<BattleSimulatorTeam>(isReadOnly: true);
            _runtimeStateHandle = state.GetComponentTypeHandle<PlacedRefRuntimeState>(isReadOnly: true);
            _deadHandle = state.GetComponentTypeHandle<ActorDead>(isReadOnly: true);
            state.RequireForUpdate<BattleSimulatorState>();
        }

        public void OnUpdate(ref SystemState state)
        {
            Entity stateEntity = SystemAPI.GetSingletonEntity<BattleSimulatorState>();
            var simulator = SystemAPI.GetComponentRW<BattleSimulatorState>(stateEntity);
            if (simulator.ValueRO.Phase != (byte)BattleSimulatorPhase.Running
                && simulator.ValueRO.Phase != (byte)BattleSimulatorPhase.Complete)
                return;

            _teamHandle.Update(ref state);
            _runtimeStateHandle.Update(ref state);
            _deadHandle.Update(ref state);

            var counts = new NativeArray<int>(2, Allocator.TempJob);
            var job = new CountBattleUnitsJob
            {
                TeamHandle = _teamHandle,
                RuntimeStateHandle = _runtimeStateHandle,
                DeadHandle = _deadHandle,
                Counts = counts,
            };
            state.Dependency = job.Schedule(_unitQuery, state.Dependency);
            state.Dependency.Complete();

            int aliveA = counts[0];
            int aliveB = counts[1];
            counts.Dispose();

            simulator.ValueRW.GroupAAlive = aliveA;
            simulator.ValueRW.GroupBAlive = aliveB;

            if (simulator.ValueRO.Phase != (byte)BattleSimulatorPhase.Running)
                return;

            if (aliveA > 0 && aliveB > 0)
                return;

            simulator.ValueRW.Phase = (byte)BattleSimulatorPhase.Complete;
            simulator.ValueRW.CompletedAt = (float)SystemAPI.Time.ElapsedTime;
            byte winner = aliveA > aliveB
                ? (byte)BattleSimulatorTeamId.GroupA
                : aliveB > aliveA
                    ? (byte)BattleSimulatorTeamId.GroupB
                    : (byte)BattleSimulatorTeamId.None;
            simulator.ValueRW.WinningTeam = winner;
            simulator.ValueRW.Status = winner switch
            {
                (byte)BattleSimulatorTeamId.GroupA => new FixedString128Bytes("Group A wins."),
                (byte)BattleSimulatorTeamId.GroupB => new FixedString128Bytes("Group B wins."),
                _ => new FixedString128Bytes("Draw."),
            };
        }

        [BurstCompile]
        struct CountBattleUnitsJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<BattleSimulatorTeam> TeamHandle;
            [ReadOnly] public ComponentTypeHandle<PlacedRefRuntimeState> RuntimeStateHandle;
            [ReadOnly] public ComponentTypeHandle<ActorDead> DeadHandle;
            public NativeArray<int> Counts;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<BattleSimulatorTeam> teams = chunk.GetNativeArray(ref TeamHandle);
                NativeArray<PlacedRefRuntimeState> runtimeStates = chunk.GetNativeArray(ref RuntimeStateHandle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (runtimeStates[i].Disabled != 0 || chunk.IsComponentEnabled(ref DeadHandle, i))
                        continue;

                    byte team = teams[i].Value;
                    if (team == (byte)BattleSimulatorTeamId.GroupA)
                        Counts[0] = Counts[0] + 1;
                    else if (team == (byte)BattleSimulatorTeamId.GroupB)
                        Counts[1] = Counts[1] + 1;
                }
            }
        }
    }
}
