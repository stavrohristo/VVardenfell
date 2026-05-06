using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.UI.Shell;

namespace VVardenfell.Runtime.Shell
{
    static class LocalMapPresentationCache
    {
        const int GridRadius = 1;
        const int DefaultRenderResolution = 256;
        const int DefaultMaskResolution = 64;
        const int RenderPathVersion = 3;
        const float MapCameraHeight = 512f;
        const float MapCameraDepthRange = 1024f;

        sealed class TileResources
        {
            public Texture2D ShroudTexture;
            public uint ShroudRevision;
            public int MaskResolution;
            public bool HasDiscovery;
            public byte[] ShroudAlpha;
        }

        sealed class NeighborhoodResources
        {
            public RenderTexture MapTexture;
            public int2 CenterCell;
            public int RenderResolution;
            public int RenderPathVersion;
            public bool MapRendered;
        }

        static readonly Dictionary<int2, TileResources> s_Tiles = new();
        static readonly int s_LocalMapRenderId = Shader.PropertyToID("_VV_LocalMapRender");
        static NeighborhoodResources s_Neighborhood;
        static Texture2D s_FullHiddenShroud;
        static Camera s_Camera;
        static GameObject s_CameraRoot;
        static World s_WorldCellQueryWorld;
        static EntityQuery s_WorldCellQuery;
        static bool s_WorldCellQueryCreated;

        public static void Dispose()
        {
            foreach (var tile in s_Tiles.Values)
            {
                if (tile.ShroudTexture != null && tile.ShroudTexture != s_FullHiddenShroud)
                    Object.Destroy(tile.ShroudTexture);
            }

            s_Tiles.Clear();
            if (s_Neighborhood?.MapTexture != null)
            {
                s_Neighborhood.MapTexture.Release();
                Object.Destroy(s_Neighborhood.MapTexture);
            }
            s_Neighborhood = null;
            if (s_FullHiddenShroud != null)
                Object.Destroy(s_FullHiddenShroud);
            s_FullHiddenShroud = null;
            if (s_CameraRoot != null)
                Object.Destroy(s_CameraRoot);
            s_CameraRoot = null;
            s_Camera = null;
            GlobalMapPresentationCache.Dispose();
        }

        public static void PrepareVisibleTiles(int2 centerCell, int renderResolution, int maskResolution)
        {
            renderResolution = math.max(1, renderResolution <= 0 ? DefaultRenderResolution : renderResolution);
            maskResolution = math.max(1, maskResolution <= 0 ? DefaultMaskResolution : maskResolution);
            EnsureFullHiddenShroud(maskResolution);

            for (int y = -GridRadius; y <= GridRadius; y++)
            {
                for (int x = -GridRadius; x <= GridRadius; x++)
                {
                    var coord = centerCell + new int2(x, y);
                    var tile = EnsureTile(coord);
                    tile.HasDiscovery = false;
                }
            }

            EnsureNeighborhoodTexture(centerCell, renderResolution);
        }

        public static void SyncShroudTexture(int2 coord, uint revision, DynamicBufferReader samples, int maskResolution)
        {
            maskResolution = math.max(1, maskResolution <= 0 ? DefaultMaskResolution : maskResolution);
            var tile = EnsureTile(coord);
            tile.HasDiscovery = true;
            if (tile.ShroudTexture != null
                && tile.ShroudTexture != s_FullHiddenShroud
                && tile.ShroudRevision == revision
                && tile.MaskResolution == maskResolution)
            {
                return;
            }

            if (tile.ShroudTexture == null
                || tile.ShroudTexture == s_FullHiddenShroud
                || tile.MaskResolution != maskResolution)
            {
                if (tile.ShroudTexture != null && tile.ShroudTexture != s_FullHiddenShroud)
                    Object.Destroy(tile.ShroudTexture);

                tile.ShroudTexture = new Texture2D(maskResolution, maskResolution, TextureFormat.RGBA32, mipChain: false, linear: true)
                {
                    name = $"LocalMapShroud({coord.x},{coord.y})",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                tile.MaskResolution = maskResolution;
            }

            int expected = maskResolution * maskResolution;
            var pixels = new Color32[expected];
            if (tile.ShroudAlpha == null || tile.ShroudAlpha.Length != expected)
                tile.ShroudAlpha = new byte[expected];
            for (int i = 0; i < expected; i++)
            {
                byte alpha = i < samples.Length ? samples.GetAlpha(i) : byte.MaxValue;
                tile.ShroudAlpha[i] = alpha;
                pixels[i] = new Color32(0, 0, 0, alpha);
            }

            tile.ShroudTexture.SetPixels32(pixels);
            tile.ShroudTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            tile.ShroudRevision = revision;
        }

        public static void RenderOnePendingTile(int2 centerCell, int renderResolution, NativeHashSet<int2> activeCells)
        {
            renderResolution = math.max(1, renderResolution <= 0 ? DefaultRenderResolution : renderResolution);
            if (!IsNeighborhoodActive(centerCell, activeCells))
                return;

            var neighborhood = EnsureNeighborhoodTexture(centerCell, renderResolution);
            if (neighborhood.MapRendered)
                return;

            RenderNeighborhood(centerCell, neighborhood.MapTexture);
            neighborhood.MapRendered = true;
            GlobalMapPresentationCache.ExploreNeighborhood(centerCell, neighborhood.MapTexture, renderResolution);
        }

        public static LocalMapViewModel BuildViewModel(
            in PlayerPresentationStats playerStats,
            bool interiorActive,
            float zoom = 1f,
            float panCellX = 0f,
            float panCellY = 0f,
            bool showShroud = true,
            bool showMarkers = true)
            => FillViewModel(
                CreateReusableViewModel(),
                playerStats,
                interiorActive,
                zoom,
                panCellX,
                panCellY,
                showShroud,
                showMarkers);

        public static LocalMapViewModel CreateReusableViewModel()
        {
            var model = new LocalMapViewModel();
            EnsureTileEntries(model);
            return model;
        }

        public static LocalMapViewModel FillViewModel(
            LocalMapViewModel model,
            in PlayerPresentationStats playerStats,
            bool interiorActive,
            float zoom = 1f,
            float panCellX = 0f,
            float panCellY = 0f,
            bool showShroud = true,
            bool showMarkers = true)
        {
            model ??= CreateReusableViewModel();
            EnsureTileEntries(model);
            zoom = math.clamp(zoom <= 0f ? 1f : zoom, 1f / 3f, 8f);
            model.Ready = playerStats.HasPlayer && !interiorActive;
            model.InteriorActive = interiorActive;
            model.PlayerCellX = playerStats.CellNormalizedPosition.x;
            model.PlayerCellY = playerStats.CellNormalizedPosition.y;
            model.PlayerHeadingDegrees = playerStats.HeadingDegrees;
            model.ViewportCellSpan = 1f / zoom;
            model.PanCellX = panCellX;
            model.PanCellY = panCellY;
            model.Zoom = zoom;
            model.ShowShroud = showShroud;
            model.ShowMarkers = showMarkers;
            model.Markers = System.Array.Empty<LocalMapMarkerViewModel>();

            if (!model.Ready)
                return model;

            var entries = model.Tiles;
            int index = 0;
            EnsureFullHiddenShroud(DefaultMaskResolution);
            var neighborhood = s_Neighborhood;
            bool hasRenderedNeighborhood = neighborhood?.MapTexture != null
                && neighborhood.MapRendered
                && neighborhood.CenterCell.x == playerStats.ExteriorCell.x
                && neighborhood.CenterCell.y == playerStats.ExteriorCell.y;
            for (int y = -GridRadius; y <= GridRadius; y++)
            {
                for (int x = -GridRadius; x <= GridRadius; x++)
                {
                    var coord = playerStats.ExteriorCell + new int2(x, y);
                    s_Tiles.TryGetValue(coord, out var tile);
                    var entry = entries[index++];
                    entry.OffsetX = x;
                    entry.OffsetY = y;
                    entry.MapTexture = hasRenderedNeighborhood ? neighborhood.MapTexture : null;
                    entry.MapUvRect = new Rect((x + 1) / 3f, (y + 1) / 3f, 1f / 3f, 1f / 3f);
                    entry.ShroudTexture = tile?.HasDiscovery == true ? tile.ShroudTexture ?? s_FullHiddenShroud : s_FullHiddenShroud;
                    entry.HasMapTexture = hasRenderedNeighborhood;
                }
            }

            if (showMarkers && hasRenderedNeighborhood)
                model.Markers = BuildDoorMarkers(playerStats.ExteriorCell);
            return model;
        }

        static void EnsureTileEntries(LocalMapViewModel model)
        {
            if (model.Tiles == null || model.Tiles.Length != 9)
                model.Tiles = new LocalMapTileViewModel[9];

            for (int i = 0; i < model.Tiles.Length; i++)
                model.Tiles[i] ??= new LocalMapTileViewModel();
        }

        static LocalMapMarkerViewModel[] BuildDoorMarkers(int2 centerCell)
        {
            var worldCellBlob = RequireWorldCellBlob();
            ref RuntimeWorldCellBlob worldCells = ref worldCellBlob.Value;
            var markers = new List<LocalMapMarkerViewModel>();
            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            for (int y = -GridRadius; y <= GridRadius; y++)
            {
                for (int x = -GridRadius; x <= GridRadius; x++)
                {
                    var coord = centerCell + new int2(x, y);
                    if (!RuntimeWorldCellBlobUtility.TryGetExteriorCellIndex(ref worldCells, coord, out int cellIndex))
                        continue;

                    ref RuntimeWorldCellDefBlob cell = ref RuntimeWorldCellBlobUtility.RequireCell(ref worldCells, cellIndex);
                    ref BlobArray<RefEntry> refs = ref RuntimeWorldCellBlobUtility.GetRefs(ref worldCells, ref cell, out int firstRef, out int refCount);
                    for (int i = 0; i < refCount; i++)
                    {
                        RefEntry entry = refs[firstRef + i];
                        if (!RuntimeWorldCellBlobUtility.TryGetDoorForRef(ref worldCells, ref cell, entry, out var door))
                            continue;

                        if ((door.Flags & DoorRefEntry.FlagTeleport) == 0)
                            continue;

                        float localX = (entry.PosX - coord.x * cellMeters) / cellMeters;
                        float localY = (entry.PosZ - coord.y * cellMeters) / cellMeters;
                        if (localX < 0f || localX > 1f || localY < 0f || localY > 1f)
                            continue;
                        if (!IsMarkerPositionExplored(coord, localX, localY))
                            continue;

                        markers.Add(new LocalMapMarkerViewModel
                        {
                            OffsetX = x,
                            OffsetY = y,
                            CellX = localX,
                            CellY = localY,
                            Label = BuildDoorMarkerLabel(door),
                            Kind = 1,
                        });
                    }
                }
            }

            return markers.Count == 0 ? System.Array.Empty<LocalMapMarkerViewModel>() : markers.ToArray();
        }

        static bool IsMarkerPositionExplored(int2 coord, float localX, float localY)
        {
            if (!s_Tiles.TryGetValue(coord, out var tile)
                || tile?.HasDiscovery != true
                || tile.ShroudAlpha == null
                || tile.MaskResolution <= 0)
            {
                return false;
            }

            int x = math.clamp((int)math.floor(localX * tile.MaskResolution), 0, tile.MaskResolution - 1);
            int y = math.clamp((int)math.floor(localY * tile.MaskResolution), 0, tile.MaskResolution - 1);
            int index = y * tile.MaskResolution + x;
            return (uint)index < (uint)tile.ShroudAlpha.Length && tile.ShroudAlpha[index] < 200;
        }

        static string BuildDoorMarkerLabel(in DoorRefEntry door)
        {
            if (!string.IsNullOrWhiteSpace(door.DestinationCellId))
                return door.DestinationCellId.Trim();

            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            int cellX = (int)math.floor(door.DestPosX / cellMeters);
            int cellY = (int)math.floor(door.DestPosZ / cellMeters);
            return $"{cellX}, {cellY}";
        }

        static string BuildDoorMarkerLabel(in RuntimeWorldDoorRefDefBlob door)
        {
            string destinationCellId = door.DestinationCellId.ToString();
            if (!string.IsNullOrWhiteSpace(destinationCellId))
                return destinationCellId.Trim();

            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            int cellX = (int)math.floor(door.DestPosX / cellMeters);
            int cellY = (int)math.floor(door.DestPosZ / cellMeters);
            return $"{cellX}, {cellY}";
        }

        static BlobAssetReference<RuntimeWorldCellBlob> RequireWorldCellBlob()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                throw new System.InvalidOperationException("[VVardenfell][WorldCellBlob] local map presentation requires a default world.");

            EntityQuery query = GetWorldCellQuery(world);
            if (query.CalculateEntityCount() != 1)
                throw new System.InvalidOperationException("[VVardenfell][WorldCellBlob] local map presentation requires exactly one RuntimeWorldCellBlobReference singleton.");

            var reference = query.GetSingleton<RuntimeWorldCellBlobReference>();
            if (!reference.Blob.IsCreated)
                throw new System.InvalidOperationException("[VVardenfell][WorldCellBlob] local map presentation requires runtime world cell blob.");
            return reference.Blob;
        }

        static EntityQuery GetWorldCellQuery(World world)
        {
            if (s_WorldCellQueryCreated && s_WorldCellQueryWorld == world)
                return s_WorldCellQuery;

            if (s_WorldCellQueryCreated)
                s_WorldCellQuery.Dispose();

            s_WorldCellQueryWorld = world;
            s_WorldCellQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<RuntimeWorldCellBlobReference>());
            s_WorldCellQueryCreated = true;
            return s_WorldCellQuery;
        }

        static TileResources EnsureTile(int2 coord)
        {
            if (!s_Tiles.TryGetValue(coord, out var tile))
            {
                tile = new TileResources();
                s_Tiles.Add(coord, tile);
            }

            return tile;
        }

        static NeighborhoodResources EnsureNeighborhoodTexture(int2 centerCell, int tileResolution)
        {
            int neighborhoodResolution = math.max(1, tileResolution <= 0 ? DefaultRenderResolution : tileResolution) * 3;
            if (s_Neighborhood != null
                && s_Neighborhood.MapTexture != null
                && s_Neighborhood.RenderResolution == neighborhoodResolution)
            {
                if (s_Neighborhood.CenterCell.x != centerCell.x
                    || s_Neighborhood.CenterCell.y != centerCell.y
                    || s_Neighborhood.RenderPathVersion != RenderPathVersion)
                {
                    s_Neighborhood.CenterCell = centerCell;
                    s_Neighborhood.RenderPathVersion = RenderPathVersion;
                    s_Neighborhood.MapRendered = false;
                }

                return s_Neighborhood;
            }

            if (s_Neighborhood?.MapTexture != null)
            {
                s_Neighborhood.MapTexture.Release();
                Object.Destroy(s_Neighborhood.MapTexture);
            }

            s_Neighborhood = new NeighborhoodResources
            {
                CenterCell = centerCell,
                RenderResolution = neighborhoodResolution,
                RenderPathVersion = RenderPathVersion,
                MapTexture = new RenderTexture(neighborhoodResolution, neighborhoodResolution, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
                {
                    name = $"LocalMapNeighborhood({centerCell.x},{centerCell.y})",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    useMipMap = false,
                    autoGenerateMips = false,
                },
            };
            s_Neighborhood.MapTexture.Create();
            return s_Neighborhood;
        }

        static bool IsNeighborhoodActive(int2 centerCell, NativeHashSet<int2> activeCells)
        {
            if (!activeCells.IsCreated)
                return true;

            for (int y = -GridRadius; y <= GridRadius; y++)
            {
                for (int x = -GridRadius; x <= GridRadius; x++)
                {
                    if (!activeCells.Contains(centerCell + new int2(x, y)))
                        return false;
                }
            }

            return true;
        }

        static void RenderNeighborhood(int2 centerCell, RenderTexture target)
        {
            if (target == null)
                return;

            var camera = EnsureCamera();
            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            float half = cellMeters * 0.5f;
            camera.orthographicSize = cellMeters * 1.5f;
            camera.aspect = 1f;
            camera.nearClipPlane = 0.5f;
            camera.farClipPlane = MapCameraDepthRange;
            camera.transform.position = new Vector3(centerCell.x * cellMeters + half, MapCameraHeight, centerCell.y * cellMeters + half);
            camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            camera.targetTexture = target;
            camera.ResetProjectionMatrix();
            camera.ResetWorldToCameraMatrix();
            float previousLocalMapRender = Shader.GetGlobalFloat(s_LocalMapRenderId);
            Shader.SetGlobalFloat(s_LocalMapRenderId, 1f);
            try
            {
                camera.Render();
            }
            finally
            {
                Shader.SetGlobalFloat(s_LocalMapRenderId, previousLocalMapRender);
                camera.targetTexture = null;
            }
        }

        static Camera EnsureCamera()
        {
            if (s_Camera != null)
                return s_Camera;

            s_CameraRoot = new GameObject("VVardenfell.LocalMapCamera")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            Object.DontDestroyOnLoad(s_CameraRoot);
            s_Camera = s_CameraRoot.AddComponent<Camera>();
            s_Camera.enabled = false;
            s_Camera.orthographic = true;
            s_Camera.renderingPath = RenderingPath.Forward;
            s_Camera.depthTextureMode = DepthTextureMode.None;
            s_Camera.clearFlags = CameraClearFlags.SolidColor;
            s_Camera.backgroundColor = new Color(0.02f, 0.025f, 0.02f, 1f);
            s_Camera.allowHDR = false;
            s_Camera.allowMSAA = false;
            s_Camera.useOcclusionCulling = false;
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
                s_Camera.cullingMask &= ~(1 << uiLayer);
            return s_Camera;
        }

        static void EnsureFullHiddenShroud(int resolution)
        {
            resolution = math.max(1, resolution <= 0 ? DefaultMaskResolution : resolution);
            if (s_FullHiddenShroud != null && s_FullHiddenShroud.width == resolution)
                return;

            if (s_FullHiddenShroud != null)
                Object.Destroy(s_FullHiddenShroud);

            s_FullHiddenShroud = new Texture2D(resolution, resolution, TextureFormat.RGBA32, mipChain: false, linear: true)
            {
                name = "LocalMapShroud.Hidden",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color32[resolution * resolution];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(0, 0, 0, byte.MaxValue);
            s_FullHiddenShroud.SetPixels32(pixels);
            s_FullHiddenShroud.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        }

        public readonly struct DynamicBufferReader
        {
            readonly Unity.Entities.DynamicBuffer<VVardenfell.Runtime.Components.ExteriorMapDiscoverySample> _samples;

            public DynamicBufferReader(Unity.Entities.DynamicBuffer<VVardenfell.Runtime.Components.ExteriorMapDiscoverySample> samples)
            {
                _samples = samples;
            }

            public int Length => _samples.Length;
            public byte GetAlpha(int index) => _samples[index].Alpha;
        }
    }
}
