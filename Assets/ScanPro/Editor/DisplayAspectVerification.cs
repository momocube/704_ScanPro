using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ScanPro704.Editor
{
    // Run with a graphics device in an isolated project. Exercises the null destination
    // callback separately: an explicit RenderTexture never exposed the display-size bug.
    public static class DisplayAspectVerification
    {
        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            var option = Array.IndexOf(args, "--qa-dir");
            var directory = option >= 0 ? args[option + 1] : "TestResults/DisplayAspect";
            Directory.CreateDirectory(directory);
            var checks = new List<string>();
            var failures = 0;
            void Check(bool ok, string description)
            {
                checks.Add((ok ? "PASS " : "FAIL ") + description);
                if (!ok) failures++;
            }
            var profile = AssetDatabase.LoadAssetAtPath<OutputProfile>(PrototypeBuild.ProfilePath);
            FullscreenVerification.Verify(profile, Check);

            var diagnostic = new Texture2D(70, 100, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            diagnostic.SetPixels32(Enumerable.Repeat(new Color32(128,128,128,255), 7000).ToArray());
            for (var y = 0; y < 13; y++) for (var x = 0; x < 70; x++) diagnostic.SetPixel(x, y, Color.black);
            for (var y = 20; y < 30; y++) for (var x = 10; x < 20; x++) diagnostic.SetPixel(x, y, Color.green);
            var corners = new[] { new Vector2Int(1,14), new Vector2Int(68,14), new Vector2Int(1,98), new Vector2Int(68,98) };
            var colors = new[] { Color.red, Color.blue, Color.yellow, Color.magenta };
            for (var i = 0; i < corners.Length; i++) diagnostic.SetPixel(corners[i].x, corners[i].y, colors[i]);
            diagnostic.Apply();
            var atlas = new RenderTexture(70, 100, 0) { filterMode = FilterMode.Point }; atlas.Create();
            var previousTarget = RenderTexture.active;
            Graphics.Blit(diagnostic, atlas);
            RenderTexture.active = previousTarget;
            var owner = new GameObject("Display aspect callback verification");
            var presenter = owner.AddComponent<VenueOutputPresenter>(); presenter.enabled = false;
            owner.GetComponent<Camera>().enabled = false;
            presenter.Source = atlas;
            presenter.HorizontalOffsetFraction = profile.fullscreenHorizontalOffset;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var callback = typeof(VenueOutputPresenter).GetMethod("OnRenderImage", flags);
            try
            {
                foreach (var size in new[] { new Vector2Int(1920,1080), new Vector2Int(1024,1920), new Vector2Int(2688,3840), new Vector2Int(720,960) })
                {
                    var frame = new RenderTexture(size.x, size.y, 0); frame.Create();
                    var target = new RenderTexture(size.x, size.y, 0); target.Create();
                    Texture2D image = null;
                    try
                    {
                        callback.Invoke(presenter, new object[] { frame, null });
                        Check(presenter.PresentationWidth == size.x && presenter.PresentationHeight == size.y,
                            $"Null destination uses camera frame {size.x}x{size.y}, independently of primary Screen {Screen.width}x{Screen.height}");
                        // Read back the very material configured by the null-destination callback,
                        // at that display's dimensions; do not recompute its content rectangle.
                        var material = (Material)typeof(VenueOutputPresenter).GetField("presentationMaterial", flags).GetValue(presenter);
                        OutputCompositor.Blit(atlas, target, material);
                        image = Read(target);
                        var scale = Math.Min(size.x / 70.0, size.y / 100.0);
                        var left = (size.x - 70 * scale) * (.5 + profile.fullscreenHorizontalOffset);
                        var bottom = (size.y - 100 * scale) / 2;
                        var square = Bounds(image.GetPixels32(), size.x, size.y);
                        Check(Math.Abs(square.width - 10 * scale) <= 1 && Math.Abs(square.height - 10 * scale) <= 1 &&
                            Math.Abs(square.width - square.height) <= 1,
                            $"Null-destination GPU square stays square at {size.x}x{size.y}: {square.width}x{square.height}");
                        var cornersMatch = true;
                        for (var i = 0; i < corners.Length; i++)
                            cornersMatch &= Same(image.GetPixel((int)(left + (corners[i].x + .5) * scale),
                                (int)(bottom + (corners[i].y + .5) * scale)), colors[i]);
                        Check(cornersMatch, $"Null-destination GPU retains four markers, orientation and horizontal offset at {size.x}x{size.y}");
                        var black = true;
                        for (var x = 0; x < size.x; x += 7)
                            black &= Same(image.GetPixel(x, (int)(bottom + 5 * scale)), Color.black);
                        Check(black, $"Null-destination GPU retains full bottom black band and margins at {size.x}x{size.y}");
                        File.WriteAllBytes(Path.Combine(directory, $"callback-{size.x}x{size.y}.png"), image.EncodeToPNG());

                        // An explicit render target takes precedence over a differently sized frame.
                        callback.Invoke(presenter, new object[] { atlas, target });
                        Check(presenter.PresentationWidth == size.x && presenter.PresentationHeight == size.y,
                            $"Explicit destination keeps its own dimensions at {size.x}x{size.y}");
                    }
                    finally
                    {
                        if (image != null) UnityEngine.Object.DestroyImmediate(image);
                        frame.Release(); target.Release();
                        UnityEngine.Object.DestroyImmediate(frame); UnityEngine.Object.DestroyImmediate(target);
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner); atlas.Release();
                UnityEngine.Object.DestroyImmediate(atlas); UnityEngine.Object.DestroyImmediate(diagnostic);
                File.WriteAllText(Path.Combine(directory, "display-aspect-checks.txt"), string.Join("\n", checks) +
                    $"\nTOTAL: {checks.Count - failures} passed, {failures} failed\nPhysical secondary monitor: NOT TESTED by this Editor regression\n");
            }
            if (failures > 0) throw new Exception($"Display aspect verification: {failures} failed");
            Debug.Log($"SCANPRO_DISPLAY_ASPECT_CHECKS_OK: {checks.Count} passed");
        }

        static Texture2D Read(RenderTexture target)
        {
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                var result = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                result.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); result.Apply(); return result;
            }
            finally { RenderTexture.active = previous; }
        }
        static RectInt Bounds(Color32[] pixels, int width, int height)
        {
            var left = width; var bottom = height; var right = -1; var top = -1;
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            {
                var p = pixels[y * width + x];
                if (p.r > 3 || p.g < 250 || p.b > 3) continue;
                left = Math.Min(left, x); bottom = Math.Min(bottom, y); right = Math.Max(right, x); top = Math.Max(top, y);
            }
            return right < 0 ? new RectInt() : new RectInt(left, bottom, right-left+1, top-bottom+1);
        }
        static bool Same(Color a, Color b) => Math.Abs(a.r-b.r)<.02 && Math.Abs(a.g-b.g)<.02 && Math.Abs(a.b-b.b)<.02;
    }
}
