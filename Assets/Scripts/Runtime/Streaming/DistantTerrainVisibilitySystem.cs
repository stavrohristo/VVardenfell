using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Streaming
{
    [BurstCompile]
    [UpdateInGroup(typeof(MorrowindPresentationSystemGroup))]
    [UpdateAfter(typeof(TerrainFrustumVisibilitySystem))]
    public partial struct DistantTerrainVisibilitySystem : ISystem
    {
        const float TerrainFrustumPaddingDegrees = 2f;
        const float TerrainBoundsPaddingMeters = 2f;

        EntityQuery _terrainQuery;

        public void OnCreate(ref SystemState state)
        {
            _terrainQuery = state.GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<RuntimeDistantTerrainTag>(),
                    ComponentType.ReadOnly<CellCoord>(),
                    ComponentType.ReadOnly<RenderBounds>(),
                    ComponentType.ReadWrite<MaterialMeshInfo>(),
                },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });
            state.RequireForUpdate<TerrainCameraFrustumSnapshot>();
            state.RequireForUpdate<StreamingConfig>();
            state.RequireForUpdate<LoadedCellsMap>();
            state.RequireForUpdate(_terrainQuery);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var config = SystemAPI.GetSingleton<StreamingConfig>();
            var loaded = SystemAPI.GetSingleton<LoadedCellsMap>();
            var snapshot = SystemAPI.GetSingleton<TerrainCameraFrustumSnapshot>();
            bool forceHidden = config.ExteriorStreamingPaused || snapshot.Valid == 0 || config.DistantTerrainRadius <= 0;

            state.Dependency = new SyncDistantTerrainVisibilityJob
            {
                CellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters,
                CameraCell = config.CameraCell,
                Radius = math.max(0, config.DistantTerrainRadius),
                Frustum = TerrainFrustum.FromSnapshot(snapshot, TerrainFrustumPaddingDegrees),
                ForceHidden = forceHidden,
                ActiveNearCells = loaded.Active,
            }.ScheduleParallel(state.Dependency);
            state.Dependency.Complete();
        }

        struct TerrainFrustum
        {
            public float4 Near;
            public float4 Far;
            public float4 Left;
            public float4 Right;
            public float4 Bottom;
            public float4 Top;

            public static TerrainFrustum FromSnapshot(in TerrainCameraFrustumSnapshot snapshot, float paddingDegrees)
            {
                float verticalFov = math.radians(math.degrees(snapshot.VerticalFovRadians) + paddingDegrees);
                verticalFov = math.clamp(verticalFov, math.radians(1f), math.radians(179f));
                float halfVertical = math.tan(verticalFov * 0.5f);
                float halfHorizontal = halfVertical * snapshot.Aspect;

                float3 position = snapshot.Position;
                float3 forward = math.normalizesafe(snapshot.Forward, new float3(0f, 0f, 1f));
                float3 right = math.normalizesafe(snapshot.Right, new float3(1f, 0f, 0f));
                float3 up = math.normalizesafe(snapshot.Up, new float3(0f, 1f, 0f));

                float3 leftDirection = math.normalizesafe(forward - right * halfHorizontal, forward);
                float3 rightDirection = math.normalizesafe(forward + right * halfHorizontal, forward);
                float3 bottomDirection = math.normalizesafe(forward - up * halfVertical, forward);
                float3 topDirection = math.normalizesafe(forward + up * halfVertical, forward);

                return new TerrainFrustum
                {
                    Near = Plane(forward, position + forward * snapshot.NearClip),
                    Far = Plane(-forward, position + forward * snapshot.FarClip),
                    Left = Plane(math.normalizesafe(math.cross(up, leftDirection), right), position),
                    Right = Plane(math.normalizesafe(math.cross(rightDirection, up), -right), position),
                    Bottom = Plane(math.normalizesafe(math.cross(bottomDirection, right), up), position),
                    Top = Plane(math.normalizesafe(math.cross(right, topDirection), -up), position),
                };
            }

            public bool IntersectsAabb(float3 center, float3 extents)
            {
                return IsInsideOrIntersecting(Near, center, extents)
                    && IsInsideOrIntersecting(Far, center, extents)
                    && IsInsideOrIntersecting(Left, center, extents)
                    && IsInsideOrIntersecting(Right, center, extents)
                    && IsInsideOrIntersecting(Bottom, center, extents)
                    && IsInsideOrIntersecting(Top, center, extents);
            }

            static float4 Plane(float3 normal, float3 point)
                => new(normal, -math.dot(normal, point));

            static bool IsInsideOrIntersecting(float4 plane, float3 center, float3 extents)
            {
                float3 normal = plane.xyz;
                float distance = math.dot(normal, center) + plane.w;
                float radius = math.dot(math.abs(normal), extents);
                return distance + radius >= 0f;
            }
        }

        [BurstCompile]
        [WithAll(typeof(RuntimeDistantTerrainTag))]
        [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
        partial struct SyncDistantTerrainVisibilityJob : IJobEntity
        {
            public float CellMeters;
            public int2 CameraCell;
            public int Radius;
            public TerrainFrustum Frustum;
            public bool ForceHidden;
            [ReadOnly] public NativeHashSet<int2> ActiveNearCells;

            void Execute(in CellCoord cell, in RenderBounds renderBounds, EnabledRefRW<MaterialMeshInfo> materialMesh)
            {
                bool visible = false;
                if (!ForceHidden && !ActiveNearCells.Contains(cell.Value))
                {
                    int dx = math.abs(cell.Value.x - CameraCell.x);
                    int dy = math.abs(cell.Value.y - CameraCell.y);
                    if (dx <= Radius && dy <= Radius)
                    {
                        float3 cellOrigin = new(cell.Value.x * CellMeters, 0f, cell.Value.y * CellMeters);
                        float3 center = cellOrigin + renderBounds.Value.Center;
                        float3 extents = renderBounds.Value.Extents + new float3(TerrainBoundsPaddingMeters);
                        visible = Frustum.IntersectsAabb(center, extents);
                    }
                }

                if (materialMesh.ValueRO != visible)
                    materialMesh.ValueRW = visible;
            }
        }
    }
}
