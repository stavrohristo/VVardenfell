using System;
using System.Collections.Generic;
using System.IO;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.UI.Shell;
using VVardenfell.Runtime.WorldState;
using Object = UnityEngine.Object;

namespace VVardenfell.Runtime.Shell
{
    static class GlobalMapPresentationCache
    {
        const int DefaultCellPixelSize = 18;
        const int PaddingCells = 4;
        const int PaletteWidth = 256;
        const int DdsHeaderSize = 128;

        static Texture2D s_BaseTexture;
        static Texture2D s_OverlayTexture;
        static Color32[] s_OverlayPixels;
        static byte[] s_MapAlphaMask;
        static int2 s_MinCell;
        static int2 s_MaxCell;
        static int s_CellPixelSize;
        static int s_Width;
        static int s_Height;
        static bool s_Dirty;
        static GlobalMapOverlayPayload s_PendingRestore;
        static bool s_HasPendingRestore;
        static readonly HashSet<int2> s_VisitedLocationCells = new();
        static World s_WorldCellQueryWorld;
        static EntityQuery s_WorldCellQuery;
        static bool s_WorldCellQueryCreated;

        public static void Dispose()
        {
            if (s_BaseTexture != null)
                Object.Destroy(s_BaseTexture);
            if (s_OverlayTexture != null)
                Object.Destroy(s_OverlayTexture);
            s_BaseTexture = null;
            s_OverlayTexture = null;
            s_OverlayPixels = null;
            s_MapAlphaMask = null;
            s_Dirty = false;
            s_VisitedLocationCells.Clear();
        }

        public static void ClearOverlay()
        {
            EnsureBuilt();
            if (s_OverlayPixels == null)
                return;
            Array.Clear(s_OverlayPixels, 0, s_OverlayPixels.Length);
            s_OverlayTexture.SetPixels32(s_OverlayPixels);
            s_OverlayTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            s_Dirty = false;
            s_HasPendingRestore = false;
            s_VisitedLocationCells.Clear();
        }

        public static GlobalMapViewModel BuildViewModel(in PlayerPresentationStats playerStats, float panX, float panY, float zoom)
        {
            EnsureBuilt();
            if (s_BaseTexture == null || s_OverlayTexture == null || !playerStats.HasPlayer)
                return new GlobalMapViewModel();

            WorldToImage(playerStats.Position.x, playerStats.Position.z, out float imageX, out float imageY);
            return new GlobalMapViewModel
            {
                Ready = true,
                BaseTexture = s_BaseTexture,
                OverlayTexture = s_OverlayTexture,
                Width = s_Width,
                Height = s_Height,
                PlayerX = imageX,
                PlayerY = imageY,
                PlayerHeadingDegrees = playerStats.HeadingDegrees,
                PanX = panX,
                PanY = panY,
                Zoom = math.clamp(zoom <= 0f ? 1f : zoom, 0.125f, 4f),
                Markers = BuildMarkerViewModels(zoom <= 0f ? 1f : zoom),
            };
        }

        public static void ExploreNeighborhood(int2 centerCell, RenderTexture texture, int renderResolution)
        {
            if (texture == null || renderResolution <= 0)
                return;
            EnsureBuilt();
            if (s_OverlayTexture == null || s_OverlayPixels == null)
                return;

            AddVisitedLocation(centerCell);
            var previous = RenderTexture.active;
            var sourcePixels = new Texture2D(renderResolution, renderResolution, TextureFormat.RGBA32, mipChain: false, linear: false);
            try
            {
                RenderTexture.active = texture;
                if (!ContainsCell(centerCell))
                    return;

                sourcePixels.ReadPixels(
                    new Rect(renderResolution, renderResolution, renderResolution, renderResolution),
                    0,
                    0,
                    recalculateMipMaps: false);
                sourcePixels.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                CopyScaledCell(centerCell, sourcePixels.GetPixels32(), renderResolution);

                s_OverlayTexture.SetPixels32(s_OverlayPixels);
                s_OverlayTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                s_Dirty = false;
            }
            finally
            {
                RenderTexture.active = previous;
                Object.Destroy(sourcePixels);
            }
        }

        public static int AddVisitedLocationsByCellNamePrefix(string cellNamePrefix)
        {
            if (string.IsNullOrWhiteSpace(cellNamePrefix))
                throw new ArgumentException("ShowMap substring must not be empty.", nameof(cellNamePrefix));

            var worldCellBlob = RequireWorldCellBlob();
            ref RuntimeWorldCellBlob worldCells = ref worldCellBlob.Value;
            int count = 0;
            for (int i = 0; i < worldCells.ExteriorCellLookup.Length; i++)
            {
                int cellIndex = worldCells.ExteriorCellLookup[i].CellIndex;
                ref RuntimeWorldCellDefBlob cell = ref RuntimeWorldCellBlobUtility.RequireCell(ref worldCells, cellIndex);
                string cellId = cell.CellId.ToString();
                if (string.IsNullOrWhiteSpace(cellId)
                    || !cellId.StartsWith(cellNamePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int before = s_VisitedLocationCells.Count;
                AddVisitedLocation(cell.ExteriorCoord);
                if (s_VisitedLocationCells.Count != before)
                    count++;
            }

            return count;
        }

        public static GlobalMapOverlayPayload CaptureOverlayPayload()
        {
            EnsureBuilt();
            if (s_OverlayTexture == null)
                return default;

            if (s_Dirty)
            {
                s_OverlayTexture.SetPixels32(s_OverlayPixels);
                s_OverlayTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                s_Dirty = false;
            }

            return new GlobalMapOverlayPayload
            {
                MinCell = s_MinCell,
                MaxCell = s_MaxCell,
                CellPixelSize = s_CellPixelSize,
                Width = s_Width,
                Height = s_Height,
                VisitedCells = CopyVisitedLocationCells(),
                PngBytes = ImageConversion.EncodeToPNG(s_OverlayTexture),
            };
        }

        public static void RestoreOverlayPayload(in GlobalMapOverlayPayload payload)
        {
            if (payload.PngBytes == null || payload.PngBytes.Length == 0)
            {
                s_HasPendingRestore = false;
                return;
            }

            s_PendingRestore = payload;
            s_HasPendingRestore = true;
            s_VisitedLocationCells.Clear();
            if (payload.VisitedCells != null)
            {
                for (int i = 0; i < payload.VisitedCells.Length; i++)
                    AddVisitedLocation(payload.VisitedCells[i]);
            }

            if (s_OverlayTexture != null)
                ApplyPendingRestore();
        }

        static void EnsureBuilt()
        {
            if (s_BaseTexture != null && s_OverlayTexture != null)
                return;

            Dispose();
            var worldCellBlob = RequireWorldCellBlob();
            ref RuntimeWorldCellBlob worldCells = ref worldCellBlob.Value;
            if (worldCells.ExteriorCellLookup.Length == 0)
                return;

            bool any = false;
            for (int i = 0; i < worldCells.ExteriorCellLookup.Length; i++)
            {
                var coord = worldCells.ExteriorCellLookup[i].Coord;
                if (!any)
                {
                    s_MinCell = coord;
                    s_MaxCell = coord;
                    any = true;
                }
                else
                {
                    s_MinCell = math.min(s_MinCell, coord);
                    s_MaxCell = math.max(s_MaxCell, coord);
                }
            }

            if (!any)
                return;

            s_MinCell -= new int2(PaddingCells, PaddingCells);
            s_MaxCell += new int2(PaddingCells, PaddingCells);

            s_CellPixelSize = DefaultCellPixelSize;
            s_Width = (s_MaxCell.x - s_MinCell.x + 1) * s_CellPixelSize;
            s_Height = (s_MaxCell.y - s_MinCell.y + 1) * s_CellPixelSize;
            var palette = LoadPalette();
            var basePixels = new Color32[s_Width * s_Height];
            s_MapAlphaMask = new byte[s_Width * s_Height];
            Color32 oceanBackground = palette[0];
            oceanBackground.a = 255;
            for (int i = 0; i < basePixels.Length; i++)
                basePixels[i] = oceanBackground;

            for (int i = 0; i < worldCells.ExteriorCellLookup.Length; i++)
            {
                var lookup = worldCells.ExteriorCellLookup[i];
                ref RuntimeWorldCellDefBlob cell = ref RuntimeWorldCellBlobUtility.RequireCell(ref worldCells, lookup.CellIndex);
                PaintBaseCell(ref worldCells, ref cell, palette, basePixels, s_MapAlphaMask);
            }

            s_BaseTexture = new Texture2D(s_Width, s_Height, TextureFormat.RGBA32, mipChain: false, linear: false)
            {
                name = "GlobalMap.Base",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            s_BaseTexture.SetPixels32(basePixels);
            s_BaseTexture.Apply(updateMipmaps: false, makeNoLongerReadable: true);

            s_OverlayPixels = new Color32[s_Width * s_Height];
            s_OverlayTexture = new Texture2D(s_Width, s_Height, TextureFormat.RGBA32, mipChain: false, linear: false)
            {
                name = "GlobalMap.Overlay",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            s_OverlayTexture.SetPixels32(s_OverlayPixels);
            s_OverlayTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            ApplyPendingRestore();
        }

        static void PaintBaseCell(ref RuntimeWorldCellBlob worldCells, ref RuntimeWorldCellDefBlob cell, Color32[] palette, Color32[] pixels, byte[] alphaMask)
        {
            int2 coord = cell.ExteriorCoord;
            ref BlobArray<sbyte> worldMap = ref RuntimeWorldCellBlobUtility.GetWorldMapSamples(ref worldCells, ref cell, out int firstSample, out int sampleCount);
            if (!ContainsCell(coord) || sampleCount < 81)
                return;

            int cellX0 = (coord.x - s_MinCell.x) * s_CellPixelSize;
            int cellY0 = (coord.y - s_MinCell.y) * s_CellPixelSize;
            for (int y = 0; y < s_CellPixelSize; y++)
            {
                int vertexY = math.clamp((y * 9) / s_CellPixelSize, 0, 8);
                int dstY = cellY0 + y;
                for (int x = 0; x < s_CellPixelSize; x++)
                {
                    int vertexX = math.clamp((x * 9) / s_CellPixelSize, 0, 8);
                    int lutIndex = math.clamp(worldMap[firstSample + vertexY * 9 + vertexX] + 128, 0, 255);
                    Color32 color = palette[lutIndex];
                    byte alpha = (byte)(lutIndex < 128 ? 0 : 255);
                    color.a = 255;
                    int dstIndex = dstY * s_Width + cellX0 + x;
                    pixels[dstIndex] = color;
                    alphaMask[dstIndex] = alpha;
                }
            }
        }

        static void CopyScaledCell(int2 cell, Color32[] sourcePixels, int sourceSize)
        {
            int dstX0 = (cell.x - s_MinCell.x) * s_CellPixelSize;
            int dstY0 = (cell.y - s_MinCell.y) * s_CellPixelSize;
            for (int y = 0; y < s_CellPixelSize; y++)
            {
                int sy = math.clamp((int)((y + 0.5f) * sourceSize / s_CellPixelSize), 0, sourceSize - 1);
                for (int x = 0; x < s_CellPixelSize; x++)
                {
                    int sx = math.clamp((int)((x + 0.5f) * sourceSize / s_CellPixelSize), 0, sourceSize - 1);
                    var color = sourcePixels[sy * sourceSize + sx];
                    int dstIndex = (dstY0 + y) * s_Width + dstX0 + x;
                    byte mask = s_MapAlphaMask != null ? s_MapAlphaMask[dstIndex] : byte.MaxValue;
                    color.a = (byte)(color.a * mask / byte.MaxValue);
                    s_OverlayPixels[dstIndex] = color;
                }
            }

            s_Dirty = true;
        }

        static bool ContainsCell(int2 cell)
            => cell.x >= s_MinCell.x && cell.x <= s_MaxCell.x && cell.y >= s_MinCell.y && cell.y <= s_MaxCell.y;

        static void AddVisitedLocation(int2 cell)
        {
            var worldCellBlob = RequireWorldCellBlob();
            ref RuntimeWorldCellBlob worldCells = ref worldCellBlob.Value;
            if (!RuntimeWorldCellBlobUtility.TryGetExteriorCellIndex(ref worldCells, cell, out int cellIndex))
                return;
            ref RuntimeWorldCellDefBlob cellData = ref RuntimeWorldCellBlobUtility.RequireCell(ref worldCells, cellIndex);
            if (cellData.CellId.IsEmpty)
                return;

            s_VisitedLocationCells.Add(cell);
        }

        static int2[] CopyVisitedLocationCells()
        {
            if (s_VisitedLocationCells.Count == 0)
                return Array.Empty<int2>();

            var result = new int2[s_VisitedLocationCells.Count];
            s_VisitedLocationCells.CopyTo(result);
            Array.Sort(result, CompareCells);
            return result;
        }

        static GlobalMapMarkerViewModel[] BuildMarkerViewModels(float zoom)
        {
            if (s_VisitedLocationCells.Count == 0)
                return Array.Empty<GlobalMapMarkerViewModel>();

            bool aggregate = zoom < 1f;
            if (!aggregate)
            {
                var markers = new List<GlobalMapMarkerViewModel>(s_VisitedLocationCells.Count);
                foreach (var cell in s_VisitedLocationCells)
                {
                    if (!TryGetLocationLabel(cell, out string label))
                        continue;

                    CellCenterToImage(cell, out float x, out float y);
                    markers.Add(new GlobalMapMarkerViewModel
                    {
                        X = x,
                        Y = y,
                        Label = label,
                        AggregateWeight = 0,
                    });
                }

                markers.Sort(CompareMarkers);
                return markers.Count == 0 ? Array.Empty<GlobalMapMarkerViewModel>() : markers.ToArray();
            }

            var aggregates = new Dictionary<string, AggregateMarker>(StringComparer.OrdinalIgnoreCase);
            foreach (var cell in s_VisitedLocationCells)
            {
                if (!TryGetLocationLabel(cell, out string label))
                    continue;

                string aggregateLabel = TrimAggregateLocationName(label);
                if (string.IsNullOrWhiteSpace(aggregateLabel))
                    continue;

                CellCenterToImage(cell, out float x, out float y);
                if (!aggregates.TryGetValue(aggregateLabel, out var marker))
                    marker = new AggregateMarker { Label = aggregateLabel };

                marker.X += x;
                marker.Y += y;
                marker.Count++;
                aggregates[aggregateLabel] = marker;
            }

            var result = new List<GlobalMapMarkerViewModel>(aggregates.Count);
            foreach (var pair in aggregates)
            {
                var marker = pair.Value;
                if (marker.Count <= 0)
                    continue;

                result.Add(new GlobalMapMarkerViewModel
                {
                    X = marker.X / marker.Count,
                    Y = marker.Y / marker.Count,
                    Label = marker.Label,
                    AggregateWeight = marker.Count,
                });
            }

            result.Sort(CompareMarkers);
            return result.Count == 0 ? Array.Empty<GlobalMapMarkerViewModel>() : result.ToArray();
        }

        static bool TryGetLocationLabel(int2 cell, out string label)
        {
            label = null;
            var worldCellBlob = RequireWorldCellBlob();
            ref RuntimeWorldCellBlob worldCells = ref worldCellBlob.Value;
            if (!RuntimeWorldCellBlobUtility.TryGetExteriorCellIndex(ref worldCells, cell, out int cellIndex))
                return false;

            ref RuntimeWorldCellDefBlob cellData = ref RuntimeWorldCellBlobUtility.RequireCell(ref worldCells, cellIndex);
            label = cellData.CellId.ToString().Trim();
            return !string.IsNullOrWhiteSpace(label);
        }

        static string TrimAggregateLocationName(string label)
        {
            int comma = label.IndexOf(',');
            return (comma >= 0 ? label[..comma] : label).Trim();
        }

        static void CellCenterToImage(int2 cell, out float imageX, out float imageY)
        {
            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            WorldToImage((cell.x + 0.5f) * cellMeters, (cell.y + 0.5f) * cellMeters, out imageX, out imageY);
        }

        static int CompareCells(int2 a, int2 b)
        {
            int y = a.y.CompareTo(b.y);
            return y != 0 ? y : a.x.CompareTo(b.x);
        }

        static int CompareMarkers(GlobalMapMarkerViewModel a, GlobalMapMarkerViewModel b)
            => string.Compare(a?.Label, b?.Label, StringComparison.OrdinalIgnoreCase);

        struct AggregateMarker
        {
            public string Label;
            public float X;
            public float Y;
            public int Count;
        }

        static void WorldToImage(float worldX, float worldZ, out float imageX, out float imageY)
        {
            float cellMeters = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            imageX = ((worldX / cellMeters) - s_MinCell.x) * s_CellPixelSize;
            imageY = ((worldZ / cellMeters) - s_MinCell.y) * s_CellPixelSize;
        }

        static Color32[] LoadPalette()
        {
            var text = Resources.Load<TextAsset>("omw_map_color_palette.dds");
            if (text == null || text.bytes == null || text.bytes.Length < DdsHeaderSize + PaletteWidth * 3)
                return BuildFallbackPalette();

            try
            {
                return DecodeRgbDdsPalette(text.bytes);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VVardenfell][Map] Failed to decode OpenMW map palette; using fallback. {ex.Message}");
                return BuildFallbackPalette();
            }
        }

        static Color32[] DecodeRgbDdsPalette(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes, writable: false);
            using var r = new BinaryReader(ms);
            if (r.ReadUInt32() != 0x20534444u)
                throw new InvalidDataException("palette is not DDS");
            r.BaseStream.Position = 12;
            int height = (int)r.ReadUInt32();
            int width = (int)r.ReadUInt32();
            r.BaseStream.Position = 80;
            uint flags = r.ReadUInt32();
            r.ReadUInt32();
            uint rgbBits = r.ReadUInt32();
            uint rMask = r.ReadUInt32();
            uint gMask = r.ReadUInt32();
            uint bMask = r.ReadUInt32();
            if (width != PaletteWidth || height != 1 || (flags & 0x40u) == 0 || rgbBits != 24)
                throw new InvalidDataException("unexpected palette DDS format");

            var colors = new Color32[PaletteWidth];
            int offset = DdsHeaderSize;
            for (int i = 0; i < PaletteWidth; i++)
            {
                uint value = (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16));
                offset += 3;
                colors[i] = new Color32(
                    ExtractMask(value, rMask),
                    ExtractMask(value, gMask),
                    ExtractMask(value, bMask),
                    255);
            }

            return colors;
        }

        static byte ExtractMask(uint value, uint mask)
        {
            int shift = 0;
            while (((mask >> shift) & 1u) == 0u && shift < 32)
                shift++;
            return (byte)((value & mask) >> shift);
        }

        static Color32[] BuildFallbackPalette()
        {
            var colors = new Color32[PaletteWidth];
            for (int i = 0; i < colors.Length; i++)
            {
                byte v = (byte)i;
                colors[i] = new Color32(v, v, v, 255);
            }

            return colors;
        }

        static BlobAssetReference<RuntimeWorldCellBlob> RequireWorldCellBlob()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                throw new InvalidOperationException("[VVardenfell][WorldCellBlob] global map presentation requires a default world.");

            EntityQuery query = GetWorldCellQuery(world);
            if (query.CalculateEntityCount() != 1)
                throw new InvalidOperationException("[VVardenfell][WorldCellBlob] global map presentation requires exactly one RuntimeWorldCellBlobReference singleton.");

            var reference = query.GetSingleton<RuntimeWorldCellBlobReference>();
            if (!reference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][WorldCellBlob] global map presentation requires runtime world cell blob.");
            return reference.Blob;
        }

        static EntityQuery GetWorldCellQuery(World world)
        {
            if (s_WorldCellQueryCreated && s_WorldCellQueryWorld == world)
                return s_WorldCellQuery;

            s_WorldCellQueryWorld = world;
            s_WorldCellQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<RuntimeWorldCellBlobReference>());
            s_WorldCellQueryCreated = true;
            return s_WorldCellQuery;
        }

        static void ApplyPendingRestore()
        {
            if (!s_HasPendingRestore || s_OverlayTexture == null)
                return;

            var payload = s_PendingRestore;
            s_VisitedLocationCells.Clear();
            if (payload.VisitedCells != null)
            {
                for (int i = 0; i < payload.VisitedCells.Length; i++)
                    AddVisitedLocation(payload.VisitedCells[i]);
            }

            var source = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: false);
            try
            {
                if (!ImageConversion.LoadImage(source, payload.PngBytes, markNonReadable: false))
                    return;
                var sourcePixels = source.GetPixels32();
                int srcWidth = source.width;
                int srcHeight = source.height;
                int srcCellSize = payload.CellPixelSize > 0 ? payload.CellPixelSize : DefaultCellPixelSize;
                for (int y = payload.MinCell.y; y <= payload.MaxCell.y; y++)
                {
                    for (int x = payload.MinCell.x; x <= payload.MaxCell.x; x++)
                    {
                        var cell = new int2(x, y);
                        if (!ContainsCell(cell))
                            continue;

                        int srcX0 = (x - payload.MinCell.x) * srcCellSize;
                        int srcY0 = (y - payload.MinCell.y) * srcCellSize;
                        int dstX0 = (x - s_MinCell.x) * s_CellPixelSize;
                        int dstY0 = (y - s_MinCell.y) * s_CellPixelSize;
                        for (int dy = 0; dy < s_CellPixelSize; dy++)
                        {
                            int sy = math.clamp(srcY0 + (int)((dy + 0.5f) * srcCellSize / s_CellPixelSize), 0, srcHeight - 1);
                            for (int dx = 0; dx < s_CellPixelSize; dx++)
                            {
                                int sx = math.clamp(srcX0 + (int)((dx + 0.5f) * srcCellSize / s_CellPixelSize), 0, srcWidth - 1);
                                int dstIndex = (dstY0 + dy) * s_Width + dstX0 + dx;
                                var color = sourcePixels[sy * srcWidth + sx];
                                byte mask = s_MapAlphaMask != null ? s_MapAlphaMask[dstIndex] : byte.MaxValue;
                                color.a = (byte)(color.a * mask / byte.MaxValue);
                                s_OverlayPixels[dstIndex] = color;
                            }
                        }
                    }
                }

                s_OverlayTexture.SetPixels32(s_OverlayPixels);
                s_OverlayTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                s_Dirty = false;
                s_HasPendingRestore = false;
            }
            finally
            {
                Object.Destroy(source);
            }
        }
    }
}
