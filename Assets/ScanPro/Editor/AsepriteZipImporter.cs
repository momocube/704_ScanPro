using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace ScanPro704.Editor
{
    internal sealed class AsepriteImportResult
    {
        public ScanLevel Level;
        public string Error;
        public string Summary;
        public readonly List<string> Warnings = new List<string>();
        public bool Succeeded => Level != null && string.IsNullOrEmpty(Error);
    }

    internal static class AsepriteZipImporter
    {
        const int Width = 52, Height = 42, PixelsPerCell = 10;
        const long MaximumExpandedBytes = 256L * 1024 * 1024;
        static readonly HashSet<int> VenueColors = new HashSet<int>
        {
            Pack(0,0,0), Pack(50,50,50), Pack(100,100,100), Pack(162,161,160), Pack(255,255,255)
        };

        sealed class EntryData
        {
            public string Path;
            public byte[] Bytes;
            public string Directory => Path.LastIndexOf('/') < 0 ? "" : Path.Substring(0, Path.LastIndexOf('/'));
            public string Name => Path.Substring(Path.LastIndexOf('/') + 1);
        }

        sealed class MaskData { public bool[] Cells; }

        sealed class ImportBuilder
        {
            public readonly ScanLevel Level;
            public readonly List<string> Warnings;
            public readonly List<EntryData> Entries;
            public readonly Dictionary<string, string> HazardGroups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, List<MaskData>> MasksByGroup = new Dictionary<string, List<MaskData>>(StringComparer.OrdinalIgnoreCase);
            public int TouchCount, ScanCount, SafeCount, HazardCount, PurpleCount, SwitchCount;
            public ImportBuilder(ScanLevel level, List<string> warnings, List<EntryData> entries)
            { Level = level; Warnings = warnings; Entries = entries; }
        }

        sealed class MaskTrack
        {
            public readonly List<bool[]> Frames = new List<bool[]>();
            public bool[] Previous;
            public int LastFrame = -1;
        }

        readonly struct Match
        {
            public readonly int Track, Component, Overlap;
            public readonly float DistanceSquared;
            public Match(int track, int component, int overlap, float distanceSquared)
            { Track = track; Component = component; Overlap = overlap; DistanceSquared = distanceSquared; }
        }

        public static AsepriteImportResult Read(string zipPath, VenueLayout venue)
        {
            var result = new AsepriteImportResult();
            ScanLevel building = null;
            if (venue == null || venue.background == null)
            { result.Error = "找不到 704 場域底圖。"; return result; }
            if (!venue.background.isReadable)
            { result.Error = "704 場域底圖必須可讀取，才能排除底色。"; return result; }
            if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            { result.Error = "找不到指定的 ZIP 檔案。"; return result; }

            try
            {
                using (var stream = File.OpenRead(zipPath))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    var entries = ReadEntries(archive);
                    var packageLabel = Path.GetFileNameWithoutExtension(zipPath);
                    var speedMatch = Regex.Match(packageLabel, @"\((?<ms>\d+)\s*ms\)", RegexOptions.IgnoreCase);
                    if (!speedMatch.Success)
                    {
                        var root = CommonRoot(archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name))
                            .Select(e => e.FullName.Replace('\\', '/')));
                        speedMatch = Regex.Match(root, @"\((?<ms>\d+)\s*ms\)", RegexOptions.IgnoreCase);
                    }
                    if (!speedMatch.Success || !int.TryParse(speedMatch.Groups["ms"].Value, out var speed) || speed < 1 || speed > 60000)
                        throw new InvalidDataException("ZIP 檔名或根資料夾需包含共用速度，例如「Level (350ms).zip」；速度需介於 1～60000 ms。");

                    building = ScriptableObject.CreateInstance<ScanLevel>();
                    building.venue = venue;
                    var hasPackageNumber = ScanLevel.TryParsePackageLevelNumber(packageLabel, out var packageNumber);
                    building.levelNumber = hasPackageNumber ? packageNumber : FindNextLevelNumber();
                    building.displayName = packageLabel;
                    if (!hasPackageNumber)
                        result.Warnings.Add(building.levelNumber > 0
                            ? $"檔名未包含有效關號（1-1～10-5），保留完整名稱並自動配號為 {building.LevelIdentifier}。"
                            : "檔名未包含有效關號（1-1～10-5），且 1～50 關已用完；輸出前請先設定有效關號。");
                    building.timeLimitSeconds = 60;
                    building.initialHearts = GameSession.MaximumHearts;
                    building.animationSpeedMilliseconds = speed;
                    building.layerOrder = TileLayerOrder.CreateDefaultOrder();
                    building.importedPackageName = Path.GetFileName(zipPath);
                    building.importedAtLocal = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8))
                        .ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + " (Asia/Taipei)";

                    var builder = new ImportBuilder(building, result.Warnings, entries);
                    ParseBasic(builder);
                    ParseFrames(builder);
                    ParseBlueFolders(builder, "blue_count", TileKind.TouchBlue);
                    ParseBlueFolders(builder, "blue_scan", TileKind.ScanBlue);
                    ParseTriggers(builder);
                    ParseShutters(builder);
                    WarnUnsupportedRoots(builder);
                    if (builder.TouchCount > 0)
                    {
                        building.autoAnswerMinimum = 1;
                        building.autoAnswerMaximum = Math.Min(builder.TouchCount, 9999);
                        if (builder.TouchCount > building.autoAnswerMaximum)
                            result.Warnings.Add($"偵測到 {builder.TouchCount} 個觸摸格，但自動答案最大值為 9999；超過上限的目標需調整答案範圍或手動設定。");
                    }
                    foreach (var item in building.items) result.Warnings.AddRange(LevelValidation.ContentErrors(item));
                    if (building.items.Count == 0)
                        throw new InvalidDataException("ZIP 內沒有可轉換的掃碼 Pro 關卡影格資料夾。");

                    var answerRangeSummary = builder.TouchCount > 0
                        ? $"；自動答案範圍 1～{building.autoAnswerMaximum}"
                        : "；未偵測到數字 touch 格，保留預設答案範圍";
                    var purpleSummary = builder.PurpleCount > 0 ? $"{builder.PurpleCount}（已套用 ClearMask）" : "0";
                    result.Summary = $"匯入完成，可直接納入遊戲輸出：觸摸數字／算式 {builder.TouchCount}、掃碼 QR {builder.ScanCount}、安全區 {builder.SafeCount}、危險區 {builder.HazardCount}、紫色機關 {purpleSummary}、淺藍機關 {builder.SwitchCount}{answerRangeSummary}；全關共用影格速度 {speed} ms。";
                    building.importSummary = result.Summary;
                    if (builder.TouchCount + builder.ScanCount == 0)
                        result.Warnings.Add("目前來源範例沒有藍色目標；此關可用來檢查標記轉換，加入藍色數字或 QR 後才可完成試玩。");
                    building.importWarnings = string.Join("\n", result.Warnings);
                    result.Level = building;
                    building = null;
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }
            finally
            {
                if (building != null) UnityEngine.Object.DestroyImmediate(building);
            }
            return result;
        }

        static List<EntryData> ReadEntries(ZipArchive archive)
        {
            if (archive.Entries.Count > 10000) throw new InvalidDataException("ZIP 檔案數量超過 10000 個。");
            long expanded = 0;
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var files = new List<EntryData>();
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                var path = entry.FullName.Replace('\\', '/');
                if (path.StartsWith("/", StringComparison.Ordinal) || path.Split('/').Any(part => part == ".." || part == ".") || path.Contains(":"))
                    throw new InvalidDataException("ZIP 含有不安全路徑：" + entry.FullName);
                if (!paths.Add(path)) throw new InvalidDataException("ZIP 含有重複路徑：" + path);
                expanded += entry.Length;
                if (expanded > MaximumExpandedBytes) throw new InvalidDataException("ZIP 解壓資料超過 256 MB。");
                if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                using (var input = entry.Open())
                using (var output = new MemoryStream())
                {
                    input.CopyTo(output);
                    if (output.Length > MaximumExpandedBytes) throw new InvalidDataException("PNG 解壓資料超過 256 MB：" + path);
                    files.Add(new EntryData { Path = path, Bytes = output.ToArray() });
                }
            }
            if (files.Count == 0) throw new InvalidDataException("ZIP 內沒有 PNG 圖片。");
            var root = CommonRoot(files.Select(f => f.Path));
            if (!string.IsNullOrEmpty(root))
                foreach (var file in files) file.Path = file.Path.Substring(root.Length + 1);
            return files;
        }

        static string CommonRoot(IEnumerable<string> paths)
        {
            var all = paths.ToArray();
            if (all.Length == 0) return "";
            var first = all[0].Split('/')[0];
            return all.All(p => p.StartsWith(first + "/", StringComparison.OrdinalIgnoreCase)) ? first : "";
        }

        static void ParseBasic(ImportBuilder b)
        {
            var file = b.Entries.FirstOrDefault(e => e.Path.Equals("basic.png", StringComparison.OrdinalIgnoreCase));
            if (file == null) return;
            var created = AddSequence(b, "basic", TileKind.SafeGreen, new[] { file }, "", null);
            b.SafeCount += created.Count;
        }

        static void ParseFrames(ImportBuilder b)
        {
            var files = b.Entries.Where(e => e.Path.StartsWith("frames/", StringComparison.OrdinalIgnoreCase) && IsImageSequenceFile(e.Name))
                .OrderBy(e => e.Name, NaturalComparer.Instance).ToArray();
            if (files.Length == 0) return;
            var created = AddSequence(b, "frames", TileKind.HazardRed, files, "import:frames", null);
            b.HazardCount += created.Count;
        }

        static void ParseBlueFolders(ImportBuilder b, string root, TileKind kind)
        {
            var folders = b.Entries.Where(e => e.Path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase) && IsImageSequenceFile(e.Name))
                .GroupBy(e => e.Directory, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, NaturalComparer.Instance).ToArray();
            foreach (var folder in folders)
            {
                var files = folder.OrderBy(e => e.Name, NaturalComparer.Instance).ToArray();
                var sourceName = folder.Key.Substring(folder.Key.LastIndexOf('/') + 1);
                if (kind == TileKind.TouchBlue && !TouchNumberContent.TryEvaluate(sourceName, out _))
                {
                    b.Warnings.Add("blue_count 資料夾名稱必須是非負整數或有效的加減算式，已略過：" + folder.Key);
                    continue;
                }
                var created = AddSequence(b, folder.Key, kind, files, "", item =>
                {
                    if (kind == TileKind.TouchBlue) item.label = sourceName;
                });
                if (kind == TileKind.TouchBlue)
                {
                    // A single static image can contain several independent answer tiles.
                    // Number those tiles deterministically instead of repeating the folder label.
                    if (files.Length == 1 && created.Count > 1)
                    {
                        var ordered = created.OrderBy(item => item.position.y).ThenBy(item => item.position.x).ToArray();
                        for (var i = 0; i < ordered.Length; i++)
                            ordered[i].label = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                    b.TouchCount += created.Count;
                }
                else b.ScanCount += created.Count;
            }
        }

        static void ParseTriggers(ImportBuilder b)
        {
            var roots = b.Entries.Where(e => e.Path.StartsWith("trigger/", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Path.Split('/').Length > 1 ? e.Path.Split('/')[1] : "")
                .Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, NaturalComparer.Instance).ToArray();
            foreach (var bot in roots)
            {
                var prefix = "trigger/" + bot + "/";
                var files = b.Entries.Where(e => e.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
                var from = files.Where(e => e.Path.StartsWith(prefix + "from/", StringComparison.OrdinalIgnoreCase) && IsImageSequenceFile(e.Name))
                    .OrderBy(e => e.Name, NaturalComparer.Instance).ToArray();
                if (from.Length == 0)
                    from = files.Where(e => e.Path.Equals(prefix + "from.png", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (from.Length == 0) { b.Warnings.Add("紫色機關缺少 from 影格：trigger/" + bot); continue; }

                var target = files.FirstOrDefault(e => e.Name.Equals("to(clearwrong).png", StringComparison.OrdinalIgnoreCase));
                if (target == null)
                    throw new InvalidDataException("紫色機關需要 to(clearwrong).png 才能直接建立 ClearMask：trigger/" + bot);
                var groups = new List<string>();
                var clearMask = ReadMask(target);
                if (!clearMask.Cells.Any(cell => cell))
                    throw new InvalidDataException("紫色機關的 to(clearwrong).png 沒有有效 ClearMask 區域：" + target.Path);
                foreach (var pair in b.MasksByGroup)
                    if (pair.Value.Any(mask => Overlaps(clearMask.Cells, mask.Cells))) groups.Add(pair.Key);
                if (groups.Count == 0)
                    b.Warnings.Add("紫色機關的 to(clearwrong).png 沒有與任何已轉換紅色區域重疊：" + target.Path);

                var created = AddSequence(b, prefix + "from", TileKind.HoldPurple, from, "", item =>
                {
                    item.targetGroups.AddRange(groups);
                    item.purpleClearMask = EncodeFrame(clearMask.Cells, 0, 0, Vector2Int.zero);
                });
                b.PurpleCount += created.Count;
            }
        }

        static void ParseShutters(ImportBuilder b)
        {
            var bots = b.Entries.Where(e => e.Path.StartsWith("shutter/", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Path.Split('/').Length > 1 ? e.Path.Split('/')[1] : "")
                .Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, NaturalComparer.Instance).ToArray();
            foreach (var bot in bots)
            {
                var prefix = "shutter/" + bot + "/";
                var files = b.Entries.Where(e => e.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
                var isGreen = Regex.IsMatch(bot, "shiftGreen", RegexOptions.IgnoreCase);
                var isRed = Regex.IsMatch(bot, "shiftRed", RegexOptions.IgnoreCase);
                if (!isGreen && !isRed)
                { b.Warnings.Add("淺藍機關資料夾未標示 shiftRed／shiftGreen，保留為格式參考且不推測玩法類型：shutter/" + bot); continue; }

                var from = files.Where(e => e.Path.StartsWith(prefix + "from/", StringComparison.OrdinalIgnoreCase) && IsImageSequenceFile(e.Name))
                    .OrderBy(e => e.Name, NaturalComparer.Instance).ToArray();
                if (from.Length == 0)
                    from = files.Where(e => e.Path.Equals(prefix + "from.png", StringComparison.OrdinalIgnoreCase)).ToArray();
                var stateFiles = files.Where(e => e.Directory.Equals("shutter/" + bot, StringComparison.OrdinalIgnoreCase) &&
                        Regex.IsMatch(e.Name, @"^(?:to)?\d+\.png$", RegexOptions.IgnoreCase))
                    .OrderBy(e => e.Name, NaturalComparer.Instance).ToArray();
                if (from.Length == 0 || stateFiles.Length == 0)
                { b.Warnings.Add("淺藍機關缺少 from 或數字切換狀態圖：shutter/" + bot); continue; }

                var stateGroups = new List<string>();
                for (var stateIndex = 0; stateIndex < stateFiles.Length; stateIndex++)
                {
                    var group = "import:shutter:" + StableId(bot) + ":" + stateIndex;
                    var created = AddSequence(b, stateFiles[stateIndex].Path,
                        isGreen ? TileKind.SafeGreen : TileKind.HazardRed, new[] { stateFiles[stateIndex] }, group,
                        item => item.allowSafeGreenOutsideFloor = isGreen, true);
                    if (created.Count == 0)
                    { b.Warnings.Add("淺藍機關切換狀態沒有有效標記，已略過：" + stateFiles[stateIndex].Path); continue; }
                    stateGroups.Add(group);
                    if (isGreen) b.SafeCount += created.Count;
                    else b.HazardCount += created.Count;
                }
                if (stateGroups.Count == 0)
                { b.Warnings.Add("淺藍機關沒有可用的切換狀態：shutter/" + bot); continue; }
                var switchItems = AddSequence(b, prefix + "from", TileKind.SwitchCyan, from, "",
                    item => item.targetGroups.AddRange(stateGroups));
                b.SwitchCount += switchItems.Count;
            }
        }

        static List<LevelItem> AddSequence(ImportBuilder b, string sourceKey, TileKind kind, EntryData[] files,
            string groupPrefix, Action<LevelItem> configure, bool groupComponentsTogether = false)
        {
            var created = new List<LevelItem>();
            if (files == null || files.Length == 0) return created;
            var componentFrames = files.Select(file => ConnectedComponents(ReadMask(file))).ToArray();
            var tracks = TrackComponents(componentFrames);
            for (var componentIndex = 0; componentIndex < tracks.Count; componentIndex++)
            {
                var frames = tracks[componentIndex];
                var occupied = new List<int>();
                foreach (var frame in frames)
                    for (var i = 0; i < frame.Length; i++) if (frame[i]) occupied.Add(i);
                if (occupied.Count == 0) continue;
                var minX = occupied.Min(index => index % Width); var minY = occupied.Min(index => index / Width);
                var maxX = occupied.Max(index => index % Width); var maxY = occupied.Max(index => index / Width);
                var size = new Vector2Int(maxX - minX + 1, maxY - minY + 1);
                var group = string.IsNullOrEmpty(groupPrefix) ? "" : groupComponentsTogether
                    ? groupPrefix : groupPrefix + ":region:" + componentIndex.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
                var item = new LevelItem
                {
                    id = StableId(sourceKey) + "_" + componentIndex.ToString("D2", System.Globalization.CultureInfo.InvariantCulture),
                    kind = kind,
                    shape = TileShape.Rectangle,
                    position = new Vector2Int(minX, minY),
                    size = size,
                    group = group
                };
                foreach (var frame in frames) item.animationFrames.Add(EncodeFrame(frame, minX, minY, size));
                configure?.Invoke(item);
                b.Level.items.Add(item);
                created.Add(item);

                if (!string.IsNullOrEmpty(group))
                {
                    if (!b.MasksByGroup.TryGetValue(group, out var groupMasks))
                        b.MasksByGroup[group] = groupMasks = new List<MaskData>();
                    groupMasks.AddRange(frames.Select(frame => new MaskData { Cells = frame }));
                    if (kind == TileKind.HazardRed) b.HazardGroups[item.id] = group;
                }
            }
            return created;
        }

        static List<List<bool[]>> TrackComponents(IReadOnlyList<List<bool[]>> componentFrames)
        {
            var tracks = new List<MaskTrack>();
            for (var frameIndex = 0; frameIndex < componentFrames.Count; frameIndex++)
            {
                var components = componentFrames[frameIndex];
                if (frameIndex == 0)
                {
                    foreach (var component in components)
                    {
                        tracks.Add(new MaskTrack { Previous = component, LastFrame = frameIndex, Frames = { component } });
                    }
                    continue;
                }

                var candidates = new List<Match>();
                for (var trackIndex = 0; trackIndex < tracks.Count; trackIndex++)
                {
                    var track = tracks[trackIndex];
                    if (track.LastFrame != frameIndex - 1) continue;
                    var trackCenter = CenterOf(track.Previous);
                    for (var componentIndex = 0; componentIndex < components.Count; componentIndex++)
                    {
                        var overlap = IntersectionCount(track.Previous, components[componentIndex]);
                        var componentCenter = CenterOf(components[componentIndex]);
                        candidates.Add(new Match(trackIndex, componentIndex, overlap, (trackCenter - componentCenter).sqrMagnitude));
                    }
                }
                candidates = candidates.OrderByDescending(match => match.Overlap)
                    .ThenBy(match => match.DistanceSquared).ThenBy(match => match.Track).ThenBy(match => match.Component).ToList();
                var assignments = new Dictionary<int, int>();
                var usedComponents = new HashSet<int>();
                foreach (var match in candidates)
                    if (!assignments.ContainsKey(match.Track) && usedComponents.Add(match.Component))
                        assignments.Add(match.Track, match.Component);

                var priorTrackCount = tracks.Count;
                for (var trackIndex = 0; trackIndex < priorTrackCount; trackIndex++)
                {
                    var track = tracks[trackIndex];
                    if (assignments.TryGetValue(trackIndex, out var componentIndex))
                    {
                        track.Previous = components[componentIndex]; track.LastFrame = frameIndex;
                        track.Frames.Add(components[componentIndex]);
                    }
                    else track.Frames.Add(new bool[Width * Height]);
                }
                for (var componentIndex = 0; componentIndex < components.Count; componentIndex++)
                {
                    if (usedComponents.Contains(componentIndex)) continue;
                    var track = new MaskTrack();
                    for (var earlier = 0; earlier < frameIndex; earlier++) track.Frames.Add(new bool[Width * Height]);
                    track.Frames.Add(components[componentIndex]); track.Previous = components[componentIndex]; track.LastFrame = frameIndex;
                    tracks.Add(track);
                }
            }
            return tracks.Select(track => track.Frames).ToList();
        }

        static Vector2 CenterOf(bool[] mask)
        {
            long sumX = 0, sumY = 0, count = 0;
            for (var i = 0; i < mask.Length; i++) if (mask[i]) { sumX += i % Width; sumY += i / Width; count++; }
            return count == 0 ? Vector2.zero : new Vector2((float)sumX / count, (float)sumY / count);
        }

        static int IntersectionCount(bool[] a, bool[] b)
        {
            var count = 0;
            for (var i = 0; i < Math.Min(a.Length, b.Length); i++) if (a[i] && b[i]) count++;
            return count;
        }

        static LevelMaskFrame EncodeFrame(bool[] source, int minX, int minY, Vector2Int bounds)
        {
            var frameMinX = bounds.x; var frameMinY = bounds.y; var frameMaxX = -1; var frameMaxY = -1;
            for (var y = 0; y < Height; y++) for (var x = 0; x < Width; x++)
                if (source[y * Width + x])
                {
                    frameMinX = Math.Min(frameMinX, x - minX); frameMinY = Math.Min(frameMinY, y - minY);
                    frameMaxX = Math.Max(frameMaxX, x - minX); frameMaxY = Math.Max(frameMaxY, y - minY);
                }
            if (frameMaxX < 0) return new LevelMaskFrame { offset = Vector2Int.zero, width = 0, height = 0, packedPixels = "" };
            var width = frameMaxX - frameMinX + 1; var height = frameMaxY - frameMinY + 1;
            var local = new BitArray(width * height);
            for (var y = 0; y < Height; y++) for (var x = 0; x < Width; x++)
                if (source[y * Width + x]) local[(y - minY - frameMinY) * width + x - minX - frameMinX] = true;
            var bytes = new byte[(local.Length + 7) / 8]; local.CopyTo(bytes, 0);
            return new LevelMaskFrame
            {
                offset = new Vector2Int(frameMinX, frameMinY), width = width, height = height,
                packedPixels = Convert.ToBase64String(bytes)
            };
        }

        static MaskData ReadMask(EntryData entry)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(texture, entry.Bytes, false)) throw new InvalidDataException("PNG 無法解碼：" + entry.Path);
                if (texture.width < 1 || texture.height < 1 || texture.width > 8192 || texture.height > 8192)
                    throw new InvalidDataException("PNG 尺寸需介於 1×1 與 8192×8192：" + entry.Path);
                var pixels = texture.GetPixels32(); var width = texture.width; var height = texture.height;
                var data = new MaskData { Cells = new bool[Width * Height] };
                // Resize the full source non-proportionally to 520×420 using nearest-neighbour sampling,
                // then sample the center of each 10×10 authoring cell. Alpha is intentionally ignored.
                for (var cellY = 0; cellY < Height; cellY++) for (var cellX = 0; cellX < Width; cellX++)
                {
                    var dstX = cellX * PixelsPerCell + PixelsPerCell / 2;
                    var dstY = cellY * PixelsPerCell + PixelsPerCell / 2;
                    var srcX = Mathf.Clamp(Mathf.FloorToInt(dstX * width / 520f), 0, width - 1);
                    var srcYTop = Mathf.Clamp(Mathf.FloorToInt(dstY * height / 420f), 0, height - 1);
                    var pixel = pixels[(height - 1 - srcYTop) * width + srcX];
                    data.Cells[cellY * Width + cellX] = !VenueColors.Contains(Pack(pixel.r, pixel.g, pixel.b));
                }
                return data;
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        static List<bool[]> ConnectedComponents(MaskData mask)
        {
            var result = new List<bool[]>(); var seen = new bool[Width * Height];
            var queue = new Queue<int>();
            for (var start = 0; start < seen.Length; start++)
            {
                if (seen[start] || !mask.Cells[start]) continue;
                var component = new bool[seen.Length]; seen[start] = true; queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    var index = queue.Dequeue(); component[index] = true;
                    var x = index % Width; var y = index / Width;
                    Visit(x > 0 ? index - 1 : -1); Visit(x + 1 < Width ? index + 1 : -1);
                    Visit(y > 0 ? index - Width : -1); Visit(y + 1 < Height ? index + Width : -1);
                    void Visit(int next)
                    { if (next >= 0 && !seen[next] && mask.Cells[next]) { seen[next] = true; queue.Enqueue(next); } }
                }
                result.Add(component);
            }
            return result;
        }

        static void WarnUnsupportedRoots(ImportBuilder b)
        {
            var roots = b.Entries.Select(entry => entry.Path.Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase);
            var supported = new HashSet<string>(new[] { "basic.png", "frames", "blue_count", "blue_scan", "trigger", "shutter" }, StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots)
            {
                if (supported.Contains(root)) continue;
                b.Warnings.Add("未依資料夾顏色或雷射槍語意自動轉換此來源，僅保留原 ZIP：" + root);
            }
        }

        static bool Overlaps(bool[] a, bool[] b)
        { for (var i = 0; i < Math.Min(a.Length, b.Length); i++) if (a[i] && b[i]) return true; return false; }
        static int Pack(byte r, byte g, byte b) => (r << 16) | (g << 8) | b;
        static bool IsImageSequenceFile(string name) => name.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        static string StableId(string value)
        {
            using (var sha = SHA256.Create())
                return "zip_" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value.ToLowerInvariant()))).Replace("-", "").Substring(0, 16);
        }
        static int FindNextLevelNumber()
        {
            var used = AssetDatabase.FindAssets("t:ScanLevel", new[] { "Assets/ScanPro" })
                .Select(g => AssetDatabase.LoadAssetAtPath<ScanLevel>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(level => level != null).Select(level => level.levelNumber).ToHashSet();
            for (var i = 1; i <= 50; i++) if (!used.Contains(i)) return i;
            return 0;
        }

        sealed class NaturalComparer : IComparer<string>
        {
            public static readonly NaturalComparer Instance = new NaturalComparer();
            public int Compare(string a, string b)
            {
                var ma = Regex.Match(a ?? "", @"^(\D*)(\d+)(.*)$"); var mb = Regex.Match(b ?? "", @"^(\D*)(\d+)(.*)$");
                if (ma.Success && mb.Success)
                {
                    var prefix = StringComparer.OrdinalIgnoreCase.Compare(ma.Groups[1].Value, mb.Groups[1].Value);
                    if (prefix != 0) return prefix;
                    if (long.TryParse(ma.Groups[2].Value, out var na) && long.TryParse(mb.Groups[2].Value, out var nb))
                    { var numeric = na.CompareTo(nb); if (numeric != 0) return numeric; }
                    return StringComparer.OrdinalIgnoreCase.Compare(ma.Groups[3].Value, mb.Groups[3].Value);
                }
                return StringComparer.OrdinalIgnoreCase.Compare(a, b);
            }
        }
    }
}
