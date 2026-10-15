using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ScanPro704.Editor
{
    public static class PerformanceVerification
    {
        [Serializable] public sealed class Measurement
        {
            public string name;
            public int iterations;
            public double meanMilliseconds, p95Milliseconds, allocatedBytesPerCall;
        }
        [Serializable] public sealed class Report
        {
            public string label, unityVersion, graphicsDevice, utc;
            public int canvasWidth, canvasHeight;
            public bool allocationCounterSupported;
            public List<Measurement> measurements = new List<Measurement>();
        }
        static Measurement Measure(string name, int count, Action<int> operation)
        {
            for (var i = 0; i < 5; i++) operation(i);
            var samples = new double[count];
            var watch = new Stopwatch();
            var check = GC.GetAllocatedBytesForCurrentThread();
            GC.KeepAlive(new byte[32768]);
            var supported = GC.GetAllocatedBytesForCurrentThread() - check >= 32768;
            var allocations = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < count; i++)
            {
                watch.Restart(); operation(i); watch.Stop(); samples[i] = watch.Elapsed.TotalMilliseconds;
            }
            allocations = GC.GetAllocatedBytesForCurrentThread() - allocations;
            var mean = samples.Average(); Array.Sort(samples);
            return new Measurement { name = name, iterations = count, meanMilliseconds = mean,
                p95Milliseconds = samples[(int)(count * .95)], allocatedBytesPerCall = supported ? allocations / (double)count : -1 };
        }
        static string Option(string key, string fallback)
        {
            var args = Environment.GetCommandLineArgs(); var index = Array.IndexOf(args, key);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }
        [MenuItem("704/效能測量")]
        public static void Run()
        {
            var report = new Report { label = Option("--perf-label", "manual"), unityVersion = Application.unityVersion,
                graphicsDevice = SystemInfo.graphicsDeviceName, utc = DateTime.UtcNow.ToString("O"), canvasWidth = 1040, canvasHeight = 840 };
            foreach (var number in new[] { 1, 2, 5 })
            {
                var level = AssetDatabase.LoadAssetAtPath<ScanLevel>($"Assets/ScanPro/Levels/Level_{number:00}.asset");
                var game = new GameSession(); game.Start(level);
                using (var renderer = new GameCanvasRenderer())
                    report.measurements.Add(Measure($"level-{number}-grid-canvas", 80, i => renderer.Draw(level, i / 60.0, game, game.Codes, true)));
            }
            var venue = AssetDatabase.LoadAssetAtPath<VenueLayout>("Assets/ScanPro/Data/704_Venue.asset");
            var allowedCount = 0;
            report.measurements.Add(Measure("venue-mask-2184-cells", 80, _ =>
            {
                allowedCount = 0;
                for (var y = 0; y < 42; y++) for (var x = 0; x < 52; x++) if (venue.IsAllowed(new Vector2Int(x, y))) allowedCount++;
            }));
            if (allowedCount != 1704) throw new Exception("Venue mask changed during optimization");
            var targets = Enumerable.Range(0, 80).Select(i => new ScanTarget(i.ToString(), false)).ToArray();
            report.measurements.Add(Measure("scanner-preview-reset-80-targets", 40, i => new ScanRegistry(704 + i).BeginRound(targets)));
            report.allocationCounterSupported = report.measurements.All(m => m.allocatedBytesPerCall >= 0);
            var folder = Option("--perf-results", "TestResults/Performance"); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, report.label + ".json"), JsonUtility.ToJson(report, true));
            foreach (var measurement in report.measurements)
                UnityEngine.Debug.Log($"PERF {measurement.name}: mean {measurement.meanMilliseconds:0.000}ms, p95 {measurement.p95Milliseconds:0.000}ms, alloc {measurement.allocatedBytesPerCall:0}B/call");
            UnityEngine.Debug.Log("SCANPRO_PERFORMANCE_OK: " + report.label);
        }
        public static void CompareReference()
        {
            var referenceType = typeof(PerformanceVerification).Assembly.GetType("ScanPro704.ReferenceCanvasRenderer");
            if (referenceType == null) throw new Exception("Run pixel comparison in the isolated performance sandbox containing the pre-change reference renderer.");
            var checks = new List<string>();
            foreach (var number in new[] { 1, 2, 5 })
            {
                var level = AssetDatabase.LoadAssetAtPath<ScanLevel>($"Assets/ScanPro/Levels/Level_{number:00}.asset");
                var game = new GameSession(); game.Start(level);
                using (var renderer = new GameCanvasRenderer())
                using (var reference = (IDisposable)Activator.CreateInstance(referenceType, new object[] { 2 }))
                {
                    var oldTexture = (Texture2D)referenceType.GetProperty("Texture").GetValue(reference);
                    var draw = referenceType.GetMethod("Draw");
                    void Compare(double seconds, bool grid, int layer)
                    {
                        renderer.Draw(level, seconds, game, game.Codes, grid, layer);
                        draw.Invoke(reference, new object[] { level, seconds, game, game.Codes, grid, layer });
                        var actual = renderer.Texture.GetPixels32(); var expected = oldTexture.GetPixels32();
                        for (var i = 0; i < actual.Length; i++)
                            if (actual[i].r != expected[i].r || actual[i].g != expected[i].g || actual[i].b != expected[i].b || actual[i].a != expected[i].a)
                                throw new Exception($"Pixel changed: level {number}, time {seconds}, grid {grid}, layer {layer}, pixel {i}");
                        checks.Add($"PASS identical RGBA pixels: level {number}, time {seconds}, grid {grid}, layer {layer}");
                    }
                    Compare(0, true, -1); Compare(.35, true, -1); Compare(1.2, false, -1);
                    for (var layer = 0; layer < 6; layer++) Compare(.4, false, layer);
                    game.Tick(level.timeLimitSeconds, Array.Empty<GameTouch>()); Compare(0, true, -1);
                }
            }
            var folder = Option("--perf-results", "TestResults/Performance"); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "pixel-parity-checks.txt"), string.Join("\n", checks) + $"\nTOTAL: {checks.Count} passed\n");
            UnityEngine.Debug.Log("SCANPRO_PIXEL_PARITY_OK: " + checks.Count);
        }
    }
}
