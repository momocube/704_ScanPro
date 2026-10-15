using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZXing;

namespace ScanPro704.Editor
{
    // Inspect the rendered tile, including its colored border, rather than rebuilding
    // the renderer's module coordinates in the decoder.
    public static class QrRenderingVerification
    {
        static readonly List<string> results = new List<string>();
        static int failures;
        static void Check(bool condition, string message)
        {
            results.Add((condition ? "PASS " : "FAIL ") + message);
            if (!condition) failures++;
        }

        public static void Run()
        {
            results.Clear(); failures = 0;
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "--qa-dir");
            var directory = index >= 0 ? args[index + 1] : "TestResults/QrRendering";
            Directory.CreateDirectory(directory);
            var level = ScriptableObject.CreateInstance<ScanLevel>();
            level.venue = AssetDatabase.LoadAssetAtPath<VenueLayout>("Assets/ScanPro/Data/704_Venue.asset");
            level.animationSpeedMilliseconds = 350;
            level.items.AddRange(new[]
            {
                Tile("square-2", 12, 12, 2, 2), Tile("square-3", 17, 12, 3, 3),
                Tile("square-4", 23, 12, 4, 4), Tile("square-6", 29, 12, 6, 6),
                Tile("circle-2", 37, 12, 2, 2), Tile("cyan-2", 12, 21, 2, 2),
                Tile("animated-2", 17, 21, 4, 2), Tile("rectangle-3x4", 23, 21, 3, 4)
            });
            level.items[4].shape = TileShape.Circle;
            level.items[5].kind = TileKind.SwitchCyan;
            level.items[6].animationFrames = new List<LevelMaskFrame>
            {
                new LevelMaskFrame { width = 2, height = 2, packedPixels = "Dw==" },
                new LevelMaskFrame { offset = new Vector2Int(2, 0), width = 2, height = 2, packedPixels = "Dw==" }
            };
            var codeValues = new[] { "0000", "1234", "9999", "0070", "2589", "6666", "9012", "5555" };
            var codes = level.items.Select((item, i) => new { item.id, code = codeValues[i] })
                .ToDictionary(pair => pair.id, pair => pair.code);
            var material = new Material(Shader.Find("704/Output Atlas"));
            var atlas = new RenderTexture(2688, 3840, 0, RenderTextureFormat.ARGB32)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            atlas.Create();
            var presenterObject = new GameObject("QR output verification");
            var presenter = presenterObject.AddComponent<VenueOutputPresenter>();
            presenterObject.GetComponent<Camera>().enabled = false;
            try
            {
                using (var renderer = new GameCanvasRenderer(2))
                {
                    renderer.Draw(level, 0, codes: codes, textOrientation: CanvasTextOrientation.VenueOutput);
                    OutputCompositor.Blit(renderer.Texture, atlas, material);
                    foreach (var divisor in new[] { 1, 2 })
                    {
                        var output = new RenderTexture(2688 / divisor, 3840 / divisor, 0, RenderTextureFormat.ARGB32);
                        output.Create();
                        try
                        {
                            presenter.Present(atlas, output);
                            var capture = Capture(output);
                            try
                            {
                                File.WriteAllBytes(Path.Combine(directory, $"venue-output-{output.width}x{output.height}.png"), capture.EncodeToPNG());
                                foreach (var item in level.items)
                                {
                                    var crop = CropTile(capture, item, divisor, 0);
                                    try
                                    {
                                        var name = item.id + (divisor == 1 ? "-native" : "-half");
                                        File.WriteAllBytes(Path.Combine(directory, name + ".png"), crop.EncodeToPNG());
                                        // A native 2x2 circle has less usable area than a square.
                                        if (divisor == 1 || item.shape != TileShape.Circle)
                                            Check(Decode(crop) == codes[item.id], name + " independently decodes the assigned four-digit code");
                                        Check(HasOnlySharpColors(crop, (Color32)TilePalette.ColorOf(item.kind)),
                                            name + " contains only QR ink, white and the tile background (no blended edges)");
                                        if (divisor == 1 && item.shape != TileShape.Circle)
                                            VerifyBorder(crop, item, name);
                                    }
                                    finally { UnityEngine.Object.DestroyImmediate(crop); }
                                }
                            }
                            finally { UnityEngine.Object.DestroyImmediate(capture); }
                        }
                        finally { output.Release(); UnityEngine.Object.DestroyImmediate(output); }
                    }
                    renderer.Draw(level, .35, codes: codes, textOrientation: CanvasTextOrientation.VenueOutput);
                    OutputCompositor.Blit(renderer.Texture, atlas, material);
                    var movedCapture = Capture(atlas);
                    try
                    {
                        var moved = CropTile(movedCapture, level.items[6], 1, 1);
                        try
                        {
                            File.WriteAllBytes(Path.Combine(directory, "animated-2-second-frame.png"), moved.EncodeToPNG());
                            Check(Decode(moved) == codes["animated-2"], "A moved 2x2 animation frame preserves a decodable QR");
                            VerifyBorder(moved, level.items[6], "animated-2-second-frame");
                        }
                        finally { UnityEngine.Object.DestroyImmediate(moved); }
                    }
                    finally { UnityEngine.Object.DestroyImmediate(movedCapture); }
                }

                // Adding an unrelated smaller circle may raise the whole canvas scale.
                // The square's QR card must retain the same physical output footprint.
                var square = level.items[0];
                level.items.Clear(); level.items.Add(square);
                foreach (var baseScale in new[] { 2, 8, 9 })
                using (var renderer = new GameCanvasRenderer(baseScale))
                {
                    renderer.Draw(level, 0, codes: codes);
                    OutputCompositor.Blit(renderer.Texture, atlas, material);
                    var capture = Capture(atlas);
                    try
                    {
                        var crop = CropTile(capture, square, 1, 0);
                        try
                        {
                            Check(Decode(crop) == codes[square.id], $"2x2 QR decodes at canvas scale {renderer.RenderScale}");
                            VerifyBorder(crop, square, $"square-2-scale-{renderer.RenderScale}");
                        }
                        finally { UnityEngine.Object.DestroyImmediate(crop); }
                    }
                    finally { UnityEngine.Object.DestroyImmediate(capture); }
                }
            }
            finally
            {
                atlas.Release(); UnityEngine.Object.DestroyImmediate(atlas);
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(presenterObject);
                UnityEngine.Object.DestroyImmediate(level);
                File.WriteAllText(Path.Combine(directory, "qr-rendering-checks.txt"),
                    string.Join("\n", results) + $"\nTOTAL: {results.Count - failures} passed, {failures} failed\nPhysical scanners/projectors: NOT TESTED\n");
            }
            if (failures > 0) throw new Exception($"QR rendering verification: {failures} checks failed.");
            Debug.Log($"SCANPRO_QR_RENDERING_OK: {results.Count} checks passed.");
        }

        static LevelItem Tile(string id, int x, int y, int w, int h) => new LevelItem
        { id = id, kind = TileKind.ScanBlue, position = new Vector2Int(x, y), size = new Vector2Int(w, h) };

        static Texture2D Capture(RenderTexture target)
        {
            var capture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                capture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); capture.Apply(false);
            }
            finally { RenderTexture.active = previous; }
            return capture;
        }

        static Texture2D CropTile(Texture2D capture, LevelItem item, int divisor, int frame)
        {
            // Use the fixed venue mapping and whole visible tile, without consulting QR raster layout.
            var position = (Vector2)item.position;
            var size = (Vector2)item.size;
            if (item.HasAnimation)
            {
                var mask = item.animationFrames[frame]; position += (Vector2)mask.offset;
                size = new Vector2(mask.width, mask.height);
            }
            var factor = 64f / divisor;
            var x = Mathf.RoundToInt((42 - position.y - size.y) * factor);
            var y = Mathf.RoundToInt(position.x * factor);
            var w = Mathf.RoundToInt(size.y * factor); var h = Mathf.RoundToInt(size.x * factor);
            var crop = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            crop.SetPixels(capture.GetPixels(x, capture.height - y - h, w, h)); crop.Apply(false);
            return crop;
        }

        static void VerifyBorder(Texture2D tile, LevelItem item, string name)
        {
            var pixels = tile.GetPixels32();
            var background = (Color32)TilePalette.ColorOf(item.kind);
            var small = Mathf.Min(tile.width, tile.height) == 128;
            var band = small ? 5 : 11;
            var border = true;
            var whiteMinX = tile.width; var whiteMinY = tile.height; var whiteMaxX = -1; var whiteMaxY = -1;
            for (var y = 0; y < tile.height; y++) for (var x = 0; x < tile.width; x++)
            {
                var color = pixels[y * tile.width + x];
                if (x < band || y < band || x >= tile.width - band || y >= tile.height - band)
                    border &= Near(color, background);
                if (color.r > 250 && color.g > 250 && color.b > 250)
                {
                    whiteMinX = Math.Min(whiteMinX, x); whiteMaxX = Math.Max(whiteMaxX, x);
                    whiteMinY = Math.Min(whiteMinY, y); whiteMaxY = Math.Max(whiteMaxY, y);
                }
            }
            Check(border, name + " retains the colored background around all four sides");
            // Golden native sizes for version-1 four-digit codes, including 4-module
            // white margins: 4/5/7/12 pixels per module for 2/3/4/6-cell tiles.
            var shortest = Mathf.Min(tile.width, tile.height);
            var modulePixels = shortest == 128 ? 4 : shortest == 192 ? 5 : shortest == 256 ? 7 : 12;
            var expected = modulePixels * 29;
            Check(whiteMaxX - whiteMinX + 1 == expected && whiteMaxY - whiteMinY + 1 == expected,
                name + " keeps a stable QR card size aligned to native output pixels");
            var aligned = true;
            for (var y = 0; y < tile.height; y++)
            {
                var run = 0;
                for (var x = 0; x <= tile.width; x++)
                {
                    var ink = x < tile.width && (Near(pixels[y * tile.width + x], new Color32(2,2,2,255)) ||
                        Near(pixels[y * tile.width + x], new Color32(0,20,75,255)));
                    if (ink) run++;
                    else if (run > 0) { aligned &= run % modulePixels == 0; run = 0; }
                }
            }
            Check(aligned, name + " preserves equal-width QR modules in the final atlas");
        }

        static bool HasOnlySharpColors(Texture2D tile, Color32 background) => tile.GetPixels32().All(p =>
            Near(p, background) || Near(p, new Color32(255,255,255,255)) ||
            Near(p, new Color32(2,2,2,255)) || Near(p, new Color32(0,20,75,255)) ||
            Near(p, new Color32(0,0,0,255)));
        static bool Near(Color32 a, Color32 b) => Math.Abs(a.r-b.r) <= 1 && Math.Abs(a.g-b.g) <= 1 && Math.Abs(a.b-b.b) <= 1;

        static string Decode(Texture2D texture)
        {
            var pixels = texture.GetPixels32(); var w = texture.width; var h = texture.height;
            for (var turn = 0; turn < 4; turn++)
            {
                var rgb = new byte[w * h * 3];
                for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
                {
                    var p = pixels[(h - 1 - y) * w + x]; var i = (y * w + x) * 3;
                    rgb[i] = p.r; rgb[i+1] = p.g; rgb[i+2] = p.b;
                }
                var reader = new BarcodeReaderGeneric { Options = new ZXing.Common.DecodingOptions { TryHarder = true } };
                var result = reader.Decode(new RGBLuminanceSource(rgb, w, h, RGBLuminanceSource.BitmapFormat.RGB24));
                if (result != null) return result.Text;
                var rotated = new Color32[pixels.Length];
                for (var y = 0; y < h; y++) for (var x = 0; x < w; x++) rotated[x * h + h - 1 - y] = pixels[y * w + x];
                pixels = rotated; var swap = w; w = h; h = swap;
            }
            return null;
        }
    }
}
