using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace ScanPro704.Editor
{
    // Run only with Tools/verify_touchservice.py, which starts an isolated service.
    public static class TouchServiceVerification
    {
        public static void Run()
        {
            const string directory = "TestResults/TouchServiceSmoke";
            Directory.CreateDirectory(directory);
            var profile = AssetDatabase.LoadAssetAtPath<OutputProfile>(PrototypeBuild.ProfilePath);
            var level = AssetDatabase.LoadAssetAtPath<ScanLevel>("Assets/ScanPro/Levels/Level_01.asset");
            var touchTargets = level.items.Where(i => i.kind == TileKind.TouchBlue);
            var target = level.orderedTouchMode == OrderedTouchMode.Ascending
                ? touchTargets.OrderBy(i => TouchNumberContent.TryGetAnswer(i, out var answer) ? answer : int.MaxValue).First()
                : touchTargets.First();
            var game = new GameSession(); game.Start(level);
            var before = game.Remaining;
            profile.TryMap((target.position + (Vector2)target.size / 2) * 10, out var pixel);
            File.WriteAllText(directory + "/ready.json", $"{{\"face\":2,\"x\":{pixel.x.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"y\":{pixel.y.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}");
            var state = new TouchState(); var seen = false; var released = false; var connected = false;
            using (var client = new TouchServiceClient("ws://127.0.0.1:29111/"))
            {
                var watch = Stopwatch.StartNew();
                while (watch.Elapsed.TotalSeconds < 15)
                {
                    connected |= client.Status == "TouchService 已連線";
                    while (client.Messages.TryDequeue(out var json))
                    {
                        if (json == "__RESET__") state.Reset();
                        else if (state.Apply(json, profile))
                        {
                            seen |= state.Points.Count > 0;
                            game.Tick(0, state.Points);
                            released |= seen && state.Points.Count == 0;
                        }
                    }
                    if (released && game.States[target.id].Completed) break;
                    Thread.Sleep(10);
                }
            }
            var checks = new Dictionary<string, bool>
            {
                ["Service profile matches the separate 2688x3840 atlas"] = profile.Validate(out _),
                ["Real ClientWebSocket connects to isolated TouchService"] = connected,
                ["Synthetic raw UDP passes through the actual service into Unity"] = seen,
                ["Mapped sensor touch completes the intended blue target"] = game.States[target.id].Completed,
                ["Repeated held sensor packets complete exactly one target"] = game.Remaining == before - 1,
                ["Service timeout/up/alive clears the released touch"] = released && state.Points.Count == 0
            };
            File.WriteAllText(directory + "/touchservice-checks.txt", string.Join("\n", checks.Select(p => (p.Value ? "PASS " : "FAIL ") + p.Key)) + $"\nTOTAL: {checks.Count(p => p.Value)} passed\nSynthetic UDP, isolated local service; physical sensors NOT TESTED\n");
            if (checks.Any(p => !p.Value)) throw new Exception("TouchService integration failed; see touchservice-checks.txt");
            UnityEngine.Debug.Log("SCANPRO_TOUCHSERVICE_CHECKS_OK: " + checks.Count);
        }
    }
}
