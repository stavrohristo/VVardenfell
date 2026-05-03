using Unity.Mathematics;

namespace VVardenfell.Runtime.Inventory
{
    public static class InventoryAvatarPreviewRuntimeUtility
    {
        public static readonly float3 Position = new(50000f, 50000f, 50000f);
        public static readonly quaternion Rotation = quaternion.identity;
        public static readonly float3 LookAt = Position + new float3(0f, 1.05f, 0f);
        public static readonly float3 CameraPosition = Position + new float3(0f, 1.05f, 3.25f);
    }
}
