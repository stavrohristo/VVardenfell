using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Rendering;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Streaming
{
    [UpdateInGroup(typeof(MorrowindPresentationSystemGroup))]
    [UpdateAfter(typeof(PlayerCameraSyncSystem))]
    public partial struct TerrainCameraFrustumSnapshotSystem : ISystem
    {
        Entity _snapshotEntity;

        public void OnCreate(ref SystemState systemState)
        {
            _snapshotEntity = systemState.EntityManager.CreateEntity(typeof(TerrainCameraFrustumSnapshot));
            systemState.RequireForUpdate<MainCameraSingleton>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Camera camera = SystemAPI.GetSingleton<MainCameraSingleton>().GetRequiredCamera();
            Transform transform = camera.transform;

            float nearClip = math.max(0.001f, camera.nearClipPlane);
            float farClip = math.max(nearClip + 1f, camera.farClipPlane);
            systemState.EntityManager.SetComponentData(_snapshotEntity, new TerrainCameraFrustumSnapshot
            {
                Position = transform.position,
                Forward = math.normalizesafe(ToFloat3(transform.forward), new float3(0f, 0f, 1f)),
                Right = math.normalizesafe(ToFloat3(transform.right), new float3(1f, 0f, 0f)),
                Up = math.normalizesafe(ToFloat3(transform.up), new float3(0f, 1f, 0f)),
                VerticalFovRadians = math.radians(math.clamp(camera.fieldOfView, 1f, 179f)),
                Aspect = math.max(0.01f, camera.aspect),
                NearClip = nearClip,
                FarClip = farClip,
                Valid = 1,
            });
        }

        static float3 ToFloat3(Vector3 value) => new(value.x, value.y, value.z);
    }
}
