using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ScanPro704
{
    [Serializable]
    public sealed class LevelMaskFrame
    {
        public Vector2Int offset;
        public int width;
        public int height;
        public string packedPixels = "";
        [NonSerialized] bool[] decoded;
        [NonSerialized] int decodedWidth, decodedHeight;
        [NonSerialized] string decodedSource;
        [NonSerialized] bool contentRectComputed;
        [NonSerialized] RectInt contentRect;
        [NonSerialized] bool contentSquareComputed;
        [NonSerialized] RectInt contentSquare;

        public bool[] Pixels
        {
            get
            {
                if (decoded != null && decodedWidth == width && decodedHeight == height && decodedSource == packedPixels) return decoded;
                decodedWidth = width; decodedHeight = height; decodedSource = packedPixels;
                contentRectComputed = false;
                contentSquareComputed = false;
                var count = Math.Max(0, width * height);
                decoded = new bool[count];
                if (count == 0 || string.IsNullOrEmpty(packedPixels)) return decoded;
                try
                {
                    var bits = new BitArray(Convert.FromBase64String(packedPixels));
                    for (var i = 0; i < count && i < bits.Length; i++) decoded[i] = bits[i];
                }
                catch (FormatException) { }
                return decoded;
            }
        }

        public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < width && y < height && Pixels[y * width + x];

        // Largest filled rectangle. Cache against the serialized mask, never the animation clock.
        public bool TryGetContentRect(out RectInt rect)
        {
            var mask = Pixels;
            if (!contentRectComputed)
            {
                contentRect = default;
                var heights = new int[Math.Max(0, width)];
                long bestDistance = long.MaxValue;
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++) heights[x] = mask[y * width + x] ? heights[x] + 1 : 0;
                    for (var right = 0; right < width; right++)
                    {
                        var minHeight = heights[right];
                        for (var left = right; left >= 0 && minHeight > 0; left--)
                        {
                            minHeight = Math.Min(minHeight, heights[left]);
                            if (minHeight == 0) break;
                            var candidate = new RectInt(left, y - minHeight + 1, right - left + 1, minHeight);
                            var area = candidate.width * candidate.height;
                            var bestArea = contentRect.width * contentRect.height;
                            long dx = 2 * candidate.x + candidate.width - width;
                            long dy = 2 * candidate.y + candidate.height - height;
                            var distance = dx * dx + dy * dy;
                            if (area > bestArea || (area == bestArea && (distance < bestDistance ||
                                (distance == bestDistance && (candidate.y < contentRect.y ||
                                (candidate.y == contentRect.y && candidate.x < contentRect.x))))))
                            { contentRect = candidate; bestDistance = distance; }
                        }
                    }
                }
                contentRectComputed = true;
            }
            rect = contentRect;
            return rect.width > 0 && rect.height > 0;
        }

        // Largest full square for QR placement; a wide filled rectangle cannot hold a QR.
        public bool TryGetContentSquare(out RectInt rect)
        {
            var mask = Pixels;
            if (!contentSquareComputed)
            {
                contentSquare = default;
                var previous = new int[Math.Max(0, width)];
                var current = new int[Math.Max(0, width)];
                var largest = 0;
                long bestDistance = long.MaxValue;
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        current[x] = !mask[y * width + x] ? 0 : x == 0 || y == 0
                            ? 1 : 1 + Math.Min(previous[x], Math.Min(current[x - 1], previous[x - 1]));
                        var side = current[x];
                        if (side == 0) continue;
                        var candidate = new RectInt(x - side + 1, y - side + 1, side, side);
                        long dx = 2 * candidate.x + side - width;
                        long dy = 2 * candidate.y + side - height;
                        var distance = dx * dx + dy * dy;
                        if (side > largest || (side == largest && (distance < bestDistance ||
                            (distance == bestDistance && (candidate.y < contentSquare.y ||
                            (candidate.y == contentSquare.y && candidate.x < contentSquare.x))))))
                        {
                            largest = side; contentSquare = candidate; bestDistance = distance;
                        }
                    }
                    var swap = previous; previous = current; current = swap;
                    Array.Clear(current, 0, current.Length);
                }
                contentSquareComputed = true;
            }
            rect = contentSquare;
            return rect.width > 0;
        }
    }

    public enum TileKind { TouchBlue, ScanBlue, SafeGreen, HazardRed, HoldPurple, SwitchCyan }
    public enum ProMechanic { None, Numbers7, Numbers15, Math7, Math15, Range7, Range15, Pairs7, MathPairs7, RangePairs7, Countdown }
    public enum TouchContentMode { Manual, Automatic }
    public enum AutoTouchContentStyle { GeneralNumber, Arithmetic }
    public enum AutoNumberOrder { Sequential, Random }
    public enum OrderedTouchMode { Free, Ascending }
    public enum TileShape { Rectangle, Square, Circle }
    public enum TileLayer { Blue, SafeGreen, HazardRed, HoldPurple, SwitchCyan }

    [Serializable]
    public sealed class LevelItem
    {
        public string id = Guid.NewGuid().ToString("N");
        public TileKind kind;
        public TileShape shape;
        public Vector2Int position;
        public Vector2Int size = Vector2Int.one;
        public string label = "";
        public bool useGeneratedContent;
        public int generatedAnswer;
        public string generatedText = "";
        public string group = "";
        public bool proTarget;
        public List<string> targetGroups = new List<string>();
        public bool allowSafeGreenOutsideFloor;
        public List<Vector2Int> waypoints = new List<Vector2Int>();
        [Min(0), Tooltip("停留於路徑起點的秒數；支援小數。")]
        public float startHoldSeconds;
        [Min(0), Tooltip("各經由點的停留秒數；索引須與路徑點一致。")]
        public List<float> waypointHoldSeconds = new List<float>();
        [Tooltip("匯入關卡使用的逐影格格子遮罩；區域外的像素不會被填滿。")]
        public List<LevelMaskFrame> animationFrames = new List<LevelMaskFrame>();
        [Tooltip("紫色長按匯入的 clearwrong 範圍；只清除目標群組與此遮罩重疊的部分。")]
        public LevelMaskFrame purpleClearMask;
        public bool IsScan => kind == TileKind.ScanBlue || kind == TileKind.SwitchCyan;
        public bool CanMove => kind == TileKind.TouchBlue || kind == TileKind.ScanBlue || kind == TileKind.SwitchCyan;
        public bool HasAnimation => animationFrames != null && animationFrames.Count > 0;

        public bool TryGetContentBounds(Vector2 currentPosition, int frameIndex, out Rect bounds)
        {
            if (HasAnimation)
            {
                var frame = animationFrames[Mathf.Clamp(frameIndex, 0, animationFrames.Count - 1)];
                if (frame == null || !frame.TryGetContentRect(out var inner)) { bounds = default; return false; }
                bounds = new Rect(currentPosition + (Vector2)frame.offset + (Vector2)inner.position, inner.size);
                return true;
            }
            bounds = new Rect(currentPosition, size);
            return size.x > 0 && size.y > 0;
        }

        public bool TryGetContentBounds(double seconds, int segmentMilliseconds, int frameMilliseconds, out Rect bounds)
            => TryGetContentBounds(PositionAt(seconds, segmentMilliseconds), AnimationFrameIndex(seconds, frameMilliseconds), out bounds);

        public bool TryGetQrContentBounds(Vector2 currentPosition, int frameIndex, out Rect bounds)
        {
            if (HasAnimation)
            {
                var frame = animationFrames[Mathf.Clamp(frameIndex, 0, animationFrames.Count - 1)];
                if (frame == null || !frame.TryGetContentSquare(out var square)) { bounds = default; return false; }
                bounds = new Rect(currentPosition + (Vector2)frame.offset + (Vector2)square.position, square.size);
                return true;
            }
            if (shape == TileShape.Circle)
            {
                var side = Mathf.Min(size.x, size.y) * TileShapeUtility.InscribedSquareFactor;
                bounds = new Rect(currentPosition + ((Vector2)size - Vector2.one * side) * .5f, Vector2.one * side);
                return side > 0;
            }
            bounds = new Rect(currentPosition, size);
            return size.x > 0 && size.y > 0;
        }

        public Vector2 PositionAt(double seconds, int segmentMilliseconds)
        {
            if (!CanMove || waypoints.Count == 0) return position;
            var count = waypoints.Count + 1;
            var moveSeconds = Math.Max(.001, segmentMilliseconds / 1000.0);
            var cycleSeconds = count * moveSeconds + Math.Max(0, startHoldSeconds);
            for (var i = 0; i < waypoints.Count; i++) cycleSeconds += Math.Max(0, HoldAtWaypoint(i));
            var t = Math.Max(0, seconds) % cycleSeconds;
            for (var i = 0; i < count; i++)
            {
                var start = i == 0 ? (Vector2)position : waypoints[i - 1];
                var hold = Math.Max(0, i == 0 ? startHoldSeconds : HoldAtWaypoint(i - 1));
                if (t < hold) return start;
                t -= hold;
                var end = i == waypoints.Count ? (Vector2)position : waypoints[i];
                if (t < moveSeconds) return Vector2.Lerp(start, end, (float)(t / moveSeconds));
                t -= moveSeconds;
            }
            return position;
        }

        public float HoldAtWaypoint(int index) => waypointHoldSeconds != null && index >= 0 && index < waypointHoldSeconds.Count
            ? waypointHoldSeconds[index] : 0;

        public int AnimationFrameIndex(double seconds, int frameMilliseconds)
        {
            if (animationFrames == null || animationFrames.Count <= 1) return 0;
            var frameDuration = Math.Max(1, frameMilliseconds);
            var index = (long)Math.Floor(Math.Max(0, seconds) * 1000.0 / frameDuration);
            return (int)(index % animationFrames.Count);
        }
    }

    public static class TileShapeUtility
    {
        public const int MinimumScanSizeCells = 2;
        public const int MinimumCircleScanDiameterCells = MinimumScanSizeCells;
        public const float InscribedSquareFactor = .70710678f;
        public static bool HasMinimumScanBounds(Vector2Int size) =>
            size.x >= MinimumScanSizeCells && size.y >= MinimumScanSizeCells;
        public static bool HasSquareBounds(LevelItem item) => item != null && item.size.x == item.size.y;
        public static int InscribedSquarePixels(int width, int height) =>
            Mathf.FloorToInt(Mathf.Min(width, height) * InscribedSquareFactor);

        public static float BarcodeAreaCm2(VenueLayout venue, LevelItem item)
        {
            if (venue == null || item == null || !venue.HasPhysicalScale) return 0;
            if (item.shape != TileShape.Circle) return venue.AreaCm2(item.size);
            var cellPixels = Mathf.Max(1, venue.pixelsPerCell);
            var sidePixels = InscribedSquarePixels(item.size.x * cellPixels, item.size.y * cellPixels);
            return sidePixels * sidePixels * venue.cellWidthCm * venue.cellHeightCm / (cellPixels * cellPixels);
        }

        public static bool Contains(LevelItem item, Vector2 position, Vector2 point)
            => Contains(item, position, point, 0);

        public static bool Contains(LevelItem item, Vector2 position, Vector2 point, int frameIndex)
        {
            if (item == null) return false;
            if (item.animationFrames != null && item.animationFrames.Count > 0)
            {
                frameIndex = Mathf.Clamp(frameIndex, 0, item.animationFrames.Count - 1);
                var frame = item.animationFrames[frameIndex];
                var x = Mathf.FloorToInt(point.x - position.x) - frame.offset.x;
                var y = Mathf.FloorToInt(point.y - position.y) - frame.offset.y;
                return frame.Contains(x, y);
            }
            if (item.shape != TileShape.Circle) return new Rect(position, item.size).Contains(point);
            var diameter = Mathf.Min(item.size.x, item.size.y);
            if (diameter <= 0) return false;
            var center = position + (Vector2)item.size * .5f;
            var radius = diameter * .5f;
            return (point - center).sqrMagnitude < radius * radius;
        }
    }

    [CreateAssetMenu(menuName = "704/Scan Pro Level")]
    public sealed class ScanLevel : ScriptableObject
    {
        [Range(1, 50)] public int levelNumber = 1;
        public string displayName = "新關卡";
        public string LevelIdentifier => FormatLevelIdentifier(levelNumber);
        public string DisplayTitle => string.IsNullOrWhiteSpace(displayName) ? LevelIdentifier : displayName;
        public static bool TryParsePackageLevelNumber(string packageName, out int number)
        {
            number = 0;
            var match = Regex.Match(packageName ?? "", @"^(?<group>[1-9]|10)-(?<stage>[1-5])(?=\s|\(|\.zip$|$)", RegexOptions.IgnoreCase);
            if (!match.Success) return false;
            number = (int.Parse(match.Groups["group"].Value) - 1) * 5 + int.Parse(match.Groups["stage"].Value);
            return true;
        }
        public static string FormatLevelIdentifier(int number)
        {
            if (number < 1) return "0-0";
            return $"{((number - 1) / 5) + 1}-{((number - 1) % 5) + 1}";
        }
        public VenueLayout venue;
        [Min(1), Tooltip("每關倒數時間；目前原型預設為 60 秒。")]
        public int timeLimitSeconds = 60;
        [Min(1)] public int initialHearts = 15;
        [Min(1), Tooltip("原型定義：每段路徑的移動時間，含回到起點的路段。")]
        public int movementSegmentMs = 1500;
        [Min(1), Tooltip("Aseprite 關卡包全關共用的影格速度（毫秒）。")]
        public int animationSpeedMilliseconds = 350;
        public string importedPackageName = "";
        public string importedAtLocal = "";
        public string importSummary = "";
        public string importWarnings = "";
        [HideInInspector] public bool excludeFromOutput;
        [Min(1), Tooltip("秒數與影格換算用的 FPS。")]
        public int timingFramesPerSecond = 60;
        public ProMechanic proMechanic;
        [Min(.1f)] public float purpleHoldSeconds = .1f;
        [Min(100)] public int switchQuietIntervalMs = 500;
        [Header("工作台對稱與觸摸數字")]
        public bool mirrorPainting;
        [Min(0)] public float symmetryAxisX = 26f;
        public bool mirrorAcrossHorizontalAxis;
        [Min(0)] public float symmetryAxisY = 21f;
        public TouchContentMode newTouchContentMode;
        public AutoTouchContentStyle autoTouchStyle;
        public AutoNumberOrder autoNumberOrder;
        public int autoAnswerMinimum = 1;
        public int autoAnswerMaximum = 7;
        public int arithmeticOperandMinimum = 1;
        public int arithmeticOperandMaximum = 20;
        public OrderedTouchMode orderedTouchMode;
        // Retained only to read older assets; display and hit testing use the fixed template.
        [HideInInspector]
        public List<TileLayer> layerOrder = TileLayerOrder.CreateDefaultOrder();
        public List<LevelItem> items = new List<LevelItem>();

        public IEnumerable<LevelItem> OrderedItems()
        {
            return TileLayerOrder.OrderedItems(items, layerOrder);
        }
    }

    public static class TileLayerOrder
    {
        static readonly TileLayer[] DefaultOrder =
            { TileLayer.Blue, TileLayer.SwitchCyan, TileLayer.HoldPurple, TileLayer.HazardRed, TileLayer.SafeGreen };

        public static List<TileLayer> CreateDefaultOrder() => DefaultOrder.ToList();

        public static List<TileLayer> CompleteOrder(IReadOnlyList<TileLayer> order)
        {
            var result = new List<TileLayer>(DefaultOrder.Length);
            if (order != null)
                foreach (var layer in order)
                    if (Enum.IsDefined(typeof(TileLayer), layer) && !result.Contains(layer)) result.Add(layer);
            foreach (var layer in DefaultOrder) if (!result.Contains(layer)) result.Add(layer);
            return result;
        }

        public static TileLayer LayerOf(TileKind kind)
        {
            switch (kind)
            {
                case TileKind.TouchBlue:
                case TileKind.ScanBlue: return TileLayer.Blue;
                case TileKind.SafeGreen: return TileLayer.SafeGreen;
                case TileKind.HazardRed: return TileLayer.HazardRed;
                case TileKind.HoldPurple: return TileLayer.HoldPurple;
                default: return TileLayer.SwitchCyan;
            }
        }

        public static string Name(TileLayer layer)
        {
            switch (layer)
            {
                case TileLayer.Blue: return "藍色";
                case TileLayer.SafeGreen: return "綠色";
                case TileLayer.HazardRed: return "紅色";
                case TileLayer.HoldPurple: return "紫色";
                default: return "淺藍色";
            }
        }

        public static List<LevelItem> OrderedItems(IReadOnlyList<LevelItem> items, IReadOnlyList<TileLayer> order)
        {
            if (items == null) return new List<LevelItem>();
            var priorities = DefaultOrder.Select((layer, index) => new { layer, index })
                .ToDictionary(entry => entry.layer, entry => entry.index);
            return items.Select((item, index) => new { item, index })
                .OrderBy(x => priorities[LayerOf(x.item.kind)])
                .ThenBy(x => x.index).Select(x => x.item).ToList();
        }
    }

    public static class TilePalette
    {
        public static readonly string[] Names = { "藍色｜觸摸", "藍色｜掃碼", "綠色｜安全區", "紅色｜危險區", "紫色｜長按", "淺藍｜切換安全區" };
        public static Color ColorOf(TileKind kind)
        {
            switch (kind)
            {
                case TileKind.TouchBlue:
                case TileKind.ScanBlue: return new Color32(0, 68, 255, 255);
                case TileKind.SafeGreen: return new Color32(25, 235, 52, 255);
                case TileKind.HazardRed: return new Color32(255, 30, 40, 255);
                case TileKind.HoldPurple: return new Color32(177, 35, 255, 255);
                default: return new Color32(30, 220, 240, 255);
            }
        }
    }
}
