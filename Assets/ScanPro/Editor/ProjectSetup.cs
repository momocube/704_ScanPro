using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ScanPro704.Editor
{
    public static class ProjectSetup
    {
        const string Root = "Assets/ScanPro";
        [MenuItem("704/初始化新專案資料")]
        public static void Initialize()
        {
            foreach (var name in new[] { "Data", "Levels", "Scenes" })
                if (!AssetDatabase.IsValidFolder(Root + "/" + name)) AssetDatabase.CreateFolder(Root, name);
            var path = Root + "/Art/704_blank.png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new InvalidOperationException("Missing supplied 704 floor plan.");
            importer.textureType = TextureImporterType.Default;
            importer.isReadable = true; importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point; importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048; importer.SaveAndReimport();
            var venue = AssetDatabase.LoadAssetAtPath<VenueLayout>(Root + "/Data/704_Venue.asset");
            if (venue == null)
            {
                venue = ScriptableObject.CreateInstance<VenueLayout>();
                venue.background = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                AssetDatabase.CreateAsset(venue, Root + "/Data/704_Venue.asset");
            }
            if (AssetDatabase.LoadAssetAtPath<OutputProfile>(Root + "/Data/704_Output_UNCALIBRATED.asset") == null)
            {
                var profile = ScriptableObject.CreateInstance<OutputProfile>(); profile.editorLayout = venue;
                // Destination coordinates deliberately remain unset. These source
                // rectangles describe the supplied editor image, not a hardware atlas.
                profile.surfaces.Add(new SurfaceMapping { name = "Floor", sourcePixels = new RectInt(100, 100, 320, 220) });
                profile.surfaces.Add(new SurfaceMapping { name = "Wall Top", sourcePixels = new RectInt(100, 0, 320, 100) });
                profile.surfaces.Add(new SurfaceMapping { name = "Wall Left", sourcePixels = new RectInt(0, 100, 100, 220) });
                profile.surfaces.Add(new SurfaceMapping { name = "Wall Right", sourcePixels = new RectInt(420, 100, 100, 220) });
                profile.surfaces.Add(new SurfaceMapping { name = "Wall Bottom", sourcePixels = new RectInt(100, 320, 320, 100) });
                AssetDatabase.CreateAsset(profile, Root + "/Data/704_Output_UNCALIBRATED.asset");
            }
            CreateExample(1, "基本觸摸與掃碼（編輯示例）", venue, new[]
            {
                Item(TileKind.SafeGreen, 11, 11, 30, 3),
                Item(TileKind.HazardRed, 20, 17, 3, 11, "", "red-A"),
                Item(TileKind.TouchBlue, 13, 17, 3, 3, "1"),
                Item(TileKind.TouchBlue, 30, 25, 3, 3, "2"),
                Item(TileKind.ScanBlue, 34, 17, 4, 4),
                Item(TileKind.ScanBlue, 12, 2, 4, 4)
            });
            var movingBlue = Item(TileKind.TouchBlue, 12, 17, 3, 3, "1");
            movingBlue.waypoints.Add(new Vector2Int(26, 17)); movingBlue.waypoints.Add(new Vector2Int(26, 25));
            var movingScan = Item(TileKind.ScanBlue, 32, 15, 4, 4);
            movingScan.waypoints.Add(new Vector2Int(32, 25));
            CreateExample(2, "移動路徑（編輯示例）", venue, new[]
            {
                Item(TileKind.SafeGreen, 11, 29, 30, 2), movingBlue, movingScan,
                Item(TileKind.HazardRed, 21, 21, 6, 2, "", "red-A")
            });
            var hold = Item(TileKind.HoldPurple, 13, 25, 3, 3); hold.targetGroups.Add("red-A");
            var toggle = Item(TileKind.SwitchCyan, 34, 24, 4, 4); toggle.targetGroups.AddRange(new[] { "red-A", "red-B" });
            CreateExample(3, "機關群組（編輯示例）", venue, new[]
            {
                Item(TileKind.HazardRed, 19, 15, 3, 12, "", "red-A"),
                Item(TileKind.HazardRed, 27, 15, 3, 12, "", "red-B"),
                Item(TileKind.TouchBlue, 13, 17, 3, 3, "1"),
                Item(TileKind.ScanBlue, 34, 16, 4, 4), hold, toggle
            });
            PlayerSettings.companyName = "704"; PlayerSettings.productName = "704 Scan Pro";
            AssetDatabase.SaveAssets();
            Debug.Log("SCANPRO_SETUP_OK: editor assets initialized; physical output remains uncalibrated.");
        }

        static LevelItem Item(TileKind kind, int x, int y, int w, int h, string label = "", string group = "") =>
            new LevelItem { kind = kind, position = new Vector2Int(x, y), size = new Vector2Int(w, h), label = label, group = group };

        static void CreateExample(int number, string name, VenueLayout venue, IEnumerable<LevelItem> items)
        {
            var path = Root + $"/Levels/Level_{number:00}.asset";
            if (AssetDatabase.LoadAssetAtPath<ScanLevel>(path) != null) return;
            var level = ScriptableObject.CreateInstance<ScanLevel>();
            level.levelNumber = number; level.displayName = name; level.venue = venue; level.items.AddRange(items);
            AssetDatabase.CreateAsset(level, path);
        }

        [MenuItem("704/驗證原型規則")]
        public static void ValidatePrototype()
        {
            Initialize();
            var checks = new List<string>();
            void Check(bool condition, string label) { if (!condition) throw new Exception("CHECK FAILED: " + label); checks.Add(label); }
            var venue = AssetDatabase.LoadAssetAtPath<VenueLayout>(Root + "/Data/704_Venue.asset");
            Check(venue.background.width == 520 && venue.background.height == 420, "Original layout is 520 x 420 without rescaling");
            Check(venue.Width == 52 && venue.Height == 42 && venue.IsAllowed(new Vector2Int(10, 10)), "Top-left editor grid uses 10-pixel cells");
            Check(!venue.IsAllowed(new Vector2Int(0, 0)) && !venue.IsAllowed(new Vector2Int(31, 4)) && !venue.IsAllowed(new Vector2Int(52, 20)), "Black corners, entrance gap and out-of-bounds are not paintable");
            Check(!venue.IsAllowed(new Vector2Int(29, 4), new Vector2Int(2, 2)), "Stamp footprint may not cross the entrance gap");
            var maskCount = 0;
            for (var y = 0; y < venue.Height; y++) for (var x = 0; x < venue.Width; x++) if (venue.IsAllowed(new Vector2Int(x, y))) maskCount++;
            var sourcePixels = venue.background.GetPixels32();
            var sourceCellCount = 0;
            var sourceWidth = venue.background.width;
            var sourceHeight = venue.background.height;
            var sourceUnit = Mathf.Max(1, venue.pixelsPerCell);
            for (var y = 0; y < venue.Height; y++) for (var x = 0; x < venue.Width; x++)
            {
                var pixel = sourcePixels[(sourceHeight - 1 - (y * sourceUnit + sourceUnit / 2)) * sourceWidth + x * sourceUnit + sourceUnit / 2];
                if (pixel.a >= 128 && (pixel.r > 0 || pixel.g > 0 || pixel.b > 0)) sourceCellCount++;
            }
            Check(sourceCellCount == 1704, "Original PNG has exactly 1704 visible cell centers before protected-area filtering");
            Check(maskCount == 1664 && sourceCellCount - maskCount == 40,
                "Exactly 1664 editable cells remain after fixed entrance and scoreboard exclusions");
            Check(!venue.HasPhysicalScale && VenueLayout.MinimumBarcodeAreaCm2 == 20, "No invented pixel-to-cm conversion; barcode minimum is 20 square cm");

            var registry = new ScanRegistry(704);
            var targets = Enumerable.Range(0, 80).Select(i => new ScanTarget("target-" + i, false)).ToArray();
            var first = registry.BeginRound(targets); var firstRound = registry.Round;
            Check(first.Values.Distinct().Count() == 80 && first.Values.All(c => c.Length == 4 && c.All(char.IsDigit)), "80 random targets have distinct four-digit codes");
            var hits = new ConcurrentBag<ScanResult>();
            Parallel.For(0, 40, i => hits.Add(registry.Submit(firstRound, "SIM-" + i, first["target-0"], 1000)));
            Check(hits.Count(r => r.Disposition == ScanDisposition.Accepted) == 1 && hits.Count(r => r.Disposition == ScanDisposition.Duplicate) == 39, "40 simultaneous device-tagged scans award one completion");
            Check(registry.Submit(firstRound, "SIM-X", "12a4", 1100).Disposition == ScanDisposition.InvalidFrame, "Malformed payload rejected");
            Check(registry.Submit(firstRound, "", first["target-1"], 1100).Disposition == ScanDisposition.InvalidFrame, "Missing device prefix rejected");
            Check(registry.Submit(firstRound, "SIM-X", first["target-1"], 1100).Disposition == ScanDisposition.Accepted, "Different target can be scanned immediately");
            var second = registry.BeginRound(targets);
            Check(!second.Values.Intersect(first.Values).Any(), "Codes are not reused across rounds in the same session");
            Check(registry.Submit(firstRound, "SIM-X", second["target-0"], 1200).Disposition == ScanDisposition.WrongRound, "Queued stale-round frames rejected");
            Check(registry.Submit(registry.Round, "SIM-X", first["target-1"], 1300).Disposition == ScanDisposition.UnknownCode, "Old barcode rejected in new round");
            var switchCodes = registry.BeginRound(new[] { new ScanTarget("switch", true) });
            var switchCode = switchCodes["switch"];
            Check(registry.Submit(registry.Round, "SIM-A", switchCode, 2000).Disposition == ScanDisposition.Accepted, "Switch accepts first scan");
            Check(registry.Submit(registry.Round, "SIM-B", switchCode, 2100).Disposition == ScanDisposition.Duplicate, "Switch dedupe spans device prefixes");
            Check(registry.Submit(registry.Round, "SIM-A", switchCode, 2500).Disposition == ScanDisposition.Duplicate, "Continuously held scanner does not toggle after initial cooldown");
            Check(registry.Submit(registry.Round, "SIM-B", switchCode, 3000).Disposition == ScanDisposition.Accepted, "Switch re-arms after 500ms without another read");

            var origin = new Vector2(17, 22);
            var success = TransitionRules.For(LevelOutcome.Success, origin);
            var hurt = TransitionRules.For(LevelOutcome.HeartsDepleted, origin);
            var timeout = TransitionRules.For(LevelOutcome.Timeout, origin);
            Check(success.Expands && success.Origin == origin && success.Color == TilePalette.ColorOf(TileKind.TouchBlue), "Scan success expands blue from completed target");
            Check(hurt.Expands && hurt.Color == TilePalette.ColorOf(TileKind.HazardRed), "Heart depletion expands red");
            Check(!timeout.Expands && timeout.Color == TilePalette.ColorOf(TileKind.HazardRed), "Timeout is immediate full red without expansion");

            var output = AssetDatabase.LoadAssetAtPath<OutputProfile>(Root + "/Data/704_Output_UNCALIBRATED.asset");
            Check(!output.Validate(out _) && output.outputWidth == 0 && output.outputHeight == 0, "Hardware output is explicitly unconfigured, not copied from editor size");
            var fixture = ScriptableObject.CreateInstance<OutputProfile>(); fixture.editorLayout = venue;
            fixture.outputWidth = 2688; fixture.outputHeight = 3840;
            fixture.surfaces.Add(new SurfaceMapping { name = "Synthetic Wall Bottom", sourcePixels = new RectInt(100, 320, 320, 100), outputPixels = new RectInt(200, 3440, 640, 200) });
            fixture.surfaces.Add(new SurfaceMapping { name = "Entrance", faceIndex = 5, sourcePixels = VenueLayout.EntrancePixels, outputPixels = OutputProfile.EntranceOutputPixels, rotation = SurfaceRotation.Degrees90 });
            fixture.surfaces.Add(new SurfaceMapping { name = "Scoreboard", faceIndex = 6, sourcePixels = VenueLayout.ScoreboardPixels, outputPixels = OutputProfile.ScoreboardOutputPixels, rotation = SurfaceRotation.Degrees90 });
            Check(fixture.Validate(out _) && fixture.TryMap(new Vector2(260, 370), out var mapped) && mapped == new Vector2(520, 3540), "Synthetic output maps independent scale and offset with fixed entrance and scoreboard surfaces");
            fixture.surfaces.Add(new SurfaceMapping { sourcePixels = new RectInt(100, 100, 10, 10), outputPixels = new RectInt(200, 3440, 10, 10) });
            Check(!fixture.Validate(out _), "Overlapping destination surfaces rejected");
            UnityEngine.Object.DestroyImmediate(fixture);
            foreach (var guid in AssetDatabase.FindAssets("t:ScanLevel", new[] { Root }))
            {
                var level = AssetDatabase.LoadAssetAtPath<ScanLevel>(AssetDatabase.GUIDToAssetPath(guid));
                Check(level.items.All(venue.FootprintIsAllowed), $"Level {level.levelNumber} visible object footprints fit supplied map");
                Check(level.items.All(venue.PathEndpointsAreAllowed), $"Level {level.levelNumber} visible path endpoints fit supplied map");
                Check(level.items.All(venue.PathIsAllowed), $"Level {level.levelNumber} entire cyclic paths stay in supplied map");
            }
            var moving = new LevelItem { kind = TileKind.ScanBlue, position = new Vector2Int(10, 10), waypoints = new List<Vector2Int> { new Vector2Int(20, 10) } };
            Check(moving.PositionAt(.5, 1000) == new Vector2(15, 10) && moving.PositionAt(2, 1000) == new Vector2(10, 10), "Preview path interpolates and loops without Play Mode");
            moving.kind = TileKind.HazardRed;
            Check(moving.PositionAt(.5, 1000) == new Vector2(10, 10), "Red areas remain static even if path data exists");
            Directory.CreateDirectory("TestResults");
            File.WriteAllText("TestResults/prototype-checks.txt", string.Join("\n", checks.Select(c => "PASS " + c)) + $"\nTOTAL: {checks.Count} passed\nPhysical scanner / Touch Service / venue output: NOT TESTED\n");
            var sample = AssetDatabase.LoadAssetAtPath<ScanLevel>(Root + "/Levels/Level_01.asset");
            var texture = WorkbenchExportService.CreateEditorTexture(sample, 0);
            File.WriteAllBytes("TestResults/editor-layout-preview.png", texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
            Debug.Log($"SCANPRO_CHECKS_OK: {checks.Count} checks passed. Physical hardware not tested.");
        }

        public static void OpenWorkbench()
        {
            ScanProWorkbench.Open();
        }
    }
}
