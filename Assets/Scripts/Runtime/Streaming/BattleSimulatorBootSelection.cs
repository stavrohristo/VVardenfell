using Unity.Mathematics;

namespace VVardenfell.Runtime.Streaming
{
    public readonly struct BattleSimulatorBootSelection
    {
        public readonly int2 ExteriorCell;

        public BattleSimulatorBootSelection(int2 exteriorCell)
        {
            ExteriorCell = exteriorCell;
        }
    }
}
