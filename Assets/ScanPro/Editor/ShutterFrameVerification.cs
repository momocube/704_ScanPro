using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ScanPro704.Editor
{
    // Uses transient fixtures and reads the user's imported draft without saving it.
    public static class ShutterFrameVerification
    {
        static readonly List<string> checks = new List<string>();
        static string output;

        [MenuItem("704/驗證/Shutter 掃碼逐幀切換")]
        public static void Run()
        {
            checks.Clear();
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "--qa-dir");
            output = index >= 0 && index + 1 < args.Length ? args[index + 1] : "TestResults/ShutterFrames";
            Directory.CreateDirectory(output);
            try
            {
                VerifyImportedDraft();
                VerifySequence(TileKind.HazardRed);
                VerifySequence(TileKind.SafeGreen);
                VerifyTouchesAndPurpleHold();
                File.WriteAllLines(Path.Combine(output, "checks.txt"), checks.Concat(new[] { $"TOTAL: {checks.Count} passed" }));
                Debug.Log($"SCANPRO_SHUTTER_FRAMES_OK: {checks.Count} checks passed.");
            }
            catch (Exception error)
            {
                File.WriteAllLines(Path.Combine(output, "checks.txt"), checks.Concat(new[] { "FAIL " + error }));
                throw;
            }
        }

        static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            checks.Add("PASS " + message);
        }

        static LevelItem Item(TileKind kind, int x, int y, string group = "") => new LevelItem
        { kind = kind, position = new Vector2Int(x, y), size = new Vector2Int(3, 3), group = group };

        static ScanLevel Fixture(TileKind stateKind)
        {
            var level = ScriptableObject.CreateInstance<ScanLevel>();
            level.venue = AssetDatabase.LoadAssetAtPath<VenueLayout>("Assets/ScanPro/Data/704_Venue.asset");
            level.items.AddRange(new[] {
                Item(TileKind.TouchBlue, 12, 12), Item(TileKind.TouchBlue, 22, 12),
                Item(TileKind.TouchBlue, 32, 26),
                Item(stateKind, 12, 12, "frame-A"), Item(stateKind, 22, 12, "frame-B"),
                Item(stateKind, 26, 12, "frame-B"), Item(stateKind, 32, 12, "frame-C")
            });
            var cyan = Item(TileKind.SwitchCyan, 12, 22);
            cyan.targetGroups.AddRange(new[] { "frame-A", "frame-B", "frame-C" });
            level.items.Add(cyan);
            var purple = Item(TileKind.HoldPurple, 22, 22);
            purple.targetGroups.AddRange(new[] { "frame-A", "frame-B" });
            purple.purpleClearMask = new LevelMaskFrame { offset = new Vector2Int(12, 12), width = 17, height = 3,
                packedPixels = Convert.ToBase64String(new byte[] { 255, 255, 255, 255, 255, 255, 7 }) };
            level.items.Add(purple);
            return level;
        }

        static Dictionary<string, int> InitialSelection(ScanLevel level) => level.items
            .Where(item => item.kind == TileKind.SwitchCyan).ToDictionary(item => item.id, _ => 0);

        static void VerifySequence(TileKind kind)
        {
            var level = Fixture(kind);
            try
            {
                var game = new GameSession(704); game.Start(level);
                var trigger = level.items.Single(item => item.kind == TileKind.SwitchCyan);
                var selected = InitialSelection(level);
                VerifySelection(level, game, selected, kind + " initial");
                game.Tick(2.35, Array.Empty<GameTouch>());
                VerifySelection(level, game, selected, kind + " no scan after elapsed animation time");
                Check(game.Scan("A", game.Codes[trigger.id], 1000).Disposition == ScanDisposition.Accepted,
                    kind + " first intentional scan is accepted");
                selected[trigger.id] = 1;
                VerifySelection(level, game, selected, kind + " first scan");
                Check(game.Scan("B", game.Codes[trigger.id], 1100).Disposition == ScanDisposition.Duplicate &&
                      game.Scan("A", game.Codes[trigger.id], 1400).Disposition == ScanDisposition.Duplicate,
                    kind + " repeated delivery and simultaneous scanners do not advance again");
                VerifySelection(level, game, selected, kind + " repeated scan");
                Check(game.Scan("B", game.Codes[trigger.id], 1900).Disposition == ScanDisposition.Accepted,
                    kind + " a new scan after the quiet interval is accepted");
                selected[trigger.id] = 2;
                VerifySelection(level, game, selected, kind + " second scan");
                Check(game.Scan("A", game.Codes[trigger.id], 2400).Disposition == ScanDisposition.Accepted,
                    kind + " third scan is accepted");
                selected[trigger.id] = 0;
                VerifySelection(level, game, selected, kind + " wraps to first frame");
                game.Scan("A", game.Codes[trigger.id], 3000);
                game.Start(level);
                VerifySelection(level, game, selected, kind + " restart resets to first frame");
            }
            finally { UnityEngine.Object.DestroyImmediate(level); }
        }

        static void VerifySelection(ScanLevel level, GameSession game, Dictionary<string, int> selected, string label)
        {
            var hidden = new HashSet<string>();
            foreach (var trigger in level.items.Where(item => item.kind == TileKind.SwitchCyan))
            {
                var active = selected[trigger.id];
                for (var i = 0; i < trigger.targetGroups.Count; i++)
                {
                    var group = trigger.targetGroups[i];
                    var regions = level.items.Where(item => item.group == group).ToArray();
                    Check(regions.Length > 0 && regions.All(item => game.States[item.id].Disabled == (i != active)),
                        label + ": only the selected state is enabled (" + group + ")");
                    if (i != active) hidden.Add(group);
                }
            }
            // The reference physically omits unselected states and has no gameplay session.
            // This detects stale colors and overlays, independently of the visibility helper.
            var reference = ScriptableObject.CreateInstance<ScanLevel>();
            reference.venue = level.venue;
            reference.movementSegmentMs = level.movementSegmentMs;
            reference.animationSpeedMilliseconds = level.animationSpeedMilliseconds;
            reference.items = level.items.Where(item => !hidden.Contains(item.group) && !game.States[item.id].Completed).ToList();
            try
            {
                using (var actual = new GameCanvasRenderer(1))
                using (var expected = new GameCanvasRenderer(1))
                {
                    actual.Draw(level, game.Elapsed, game, game.Codes);
                    expected.Draw(reference, game.Elapsed, null, game.Codes);
                    Check(actual.Texture.GetPixels32().SequenceEqual(expected.Texture.GetPixels32()),
                        label + ": every rendered pixel matches the single selected frame");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(reference); }
        }

        static void VerifyTouchesAndPurpleHold()
        {
            var level = Fixture(TileKind.HazardRed);
            try
            {
                var game = new GameSession(704); game.Start(level);
                game.Tick(.01, new[] { new GameTouch(1, new Vector2(22.5f, 12.5f)) });
                Check(game.States[level.items[1].id].Completed && game.Hearts == 15,
                    "A hidden shutter frame neither blocks nor harms the exposed blue target");
                game.Tick(.01, new[] { new GameTouch(2, new Vector2(12.5f, 12.5f)) });
                Check(!game.States[level.items[0].id].Completed && game.Hearts == 14,
                    "The selected red frame still blocks the lower blue target and deducts one heart");
                var purple = new GameTouch(3, new Vector2(22.5f, 22.5f));
                game.Tick(.01, new[] { purple }); game.Tick(.11, new[] { purple });
                Check(game.IsPurpleClearedAt(level.items[3], new Vector2(12.5f, 12.5f)),
                    "Purple hold clears its designated region within the selected shutter frame");
                var trigger = level.items.Single(item => item.kind == TileKind.SwitchCyan);
                game.Scan("A", game.Codes[trigger.id], 1000);
                Check(game.IsInactiveShutterHazard(level.items[3]) &&
                      game.IsPurpleClearedAt(level.items[4], new Vector2(22.5f, 12.5f)),
                    "Scanning while purple is held hides the old frame and preserves the clear mask on the new frame");
                game.Tick(.01, Array.Empty<GameTouch>());
                var selected = InitialSelection(level); selected[trigger.id] = 1;
                VerifySelection(level, game, selected, "Purple release after scanning");
                game.Tick(.01, new[] { new GameTouch(4, new Vector2(12.5f, 12.5f)) });
                Check(game.States[level.items[0].id].Completed && game.Hearts == 14,
                    "After switching, the old frame releases its blue target without damage");
            }
            finally { UnityEngine.Object.DestroyImmediate(level); }
        }

        static void SavePreview(ScanLevel level, GameSession game, string filename)
        {
            using (var renderer = new GameCanvasRenderer(1))
            {
                renderer.Draw(level, game.Elapsed, game, game.Codes);
                File.WriteAllBytes(Path.Combine(output, filename + ".png"), renderer.Texture.EncodeToPNG());
            }
        }

        static void VerifyImportedDraft()
        {
            const string path = "Assets/ScanPro/LevelDrafts/Imported_1-3 (100ms).asset";
            var level = AssetDatabase.LoadAssetAtPath<ScanLevel>(path);
            Check(level != null, "The actual imported 1-3 draft is present");
            var before = File.ReadAllBytes(path);
            var game = new GameSession(704); game.Start(level);
            var selected = InitialSelection(level);
            Check(selected.Count == 4, "The actual 1-3 draft has four independent shutter mechanisms");
            SavePreview(level, game, "1-3-initial");
            VerifySelection(level, game, selected, "1-3 initial");
            game.Tick(1.35, Array.Empty<GameTouch>());
            VerifySelection(level, game, selected, "1-3 elapsed time without scanning");
            var triggerNumber = 0;
            foreach (var trigger in level.items.Where(item => item.kind == TileKind.SwitchCyan))
            {
                triggerNumber++;
                for (var scan = 1; scan <= trigger.targetGroups.Count; scan++)
                {
                    Check(game.Scan("QA", game.Codes[trigger.id], scan * 1000).Disposition == ScanDisposition.Accepted,
                        $"1-3 mechanism {triggerNumber}, intentional scan {scan} accepted");
                    selected[trigger.id] = scan % trigger.targetGroups.Count;
                    VerifySelection(level, game, selected, $"1-3 mechanism {triggerNumber}, scan {scan}");
                    if (scan == 1) SavePreview(level, game, $"1-3-switch-{triggerNumber}");
                }
            }
            Check(before.SequenceEqual(File.ReadAllBytes(path)), "The imported draft remains byte-for-byte unchanged");
        }
    }
}
