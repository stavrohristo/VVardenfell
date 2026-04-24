using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Streaming
{
    public static class CollisionEvidenceDebug
    {
        public static void LogCurrentComparison(World world = null)
        {
            ExteriorInteriorCollisionDebug.LogCurrentComparison(world);
        }
    }

    [UpdateInGroup(typeof(CellStreamingSystemGroup), OrderLast = true)]
    public partial class CollisionEvidenceConsoleLogSystem : SystemBase
    {
        int2 _lastCameraCell;
        byte _lastInteriorActive;
        FixedString128Bytes _lastInteriorCellId;
        bool _initialized;

        protected override void OnCreate()
        {
            RequireForUpdate<StreamingConfig>();
            RequireForUpdate<InteriorTransitionState>();
        }

        protected override void OnUpdate()
        {
            var streaming = SystemAPI.GetSingleton<StreamingConfig>();
            var transition = SystemAPI.GetSingleton<InteriorTransitionState>();

            bool shouldLog = !_initialized
                || !streaming.CameraCell.Equals(_lastCameraCell)
                || transition.InteriorActive != _lastInteriorActive
                || !transition.ActiveInteriorCellId.Equals(_lastInteriorCellId);

            _lastCameraCell = streaming.CameraCell;
            _lastInteriorActive = transition.InteriorActive;
            _lastInteriorCellId = transition.ActiveInteriorCellId;
            _initialized = true;

            if (!shouldLog)
                return;

            CollisionEvidenceDebug.LogCurrentComparison(World);
        }
    }
}
