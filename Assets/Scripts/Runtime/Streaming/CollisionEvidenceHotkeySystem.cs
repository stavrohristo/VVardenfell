using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VVardenfell.Runtime.Streaming
{
    [UpdateInGroup(typeof(CellStreamingSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(TerrainGateToggleSystem))]
    public partial class CollisionEvidenceHotkeySystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (WasHotkeyPressed())
            {
                Debug.Log("[VVardenfell][CollisionEvidence] F9 pressed; dumping exterior/interior collision comparison.");
                ExteriorInteriorCollisionDebug.LogCurrentComparison(World);
            }
        }

        static bool WasHotkeyPressed()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f9Key.wasPressedThisFrame)
                return true;

            try
            {
                return Input.GetKeyDown(KeyCode.F9);
            }
            catch (System.InvalidOperationException)
            {
                return false;
            }
        }
    }
}
