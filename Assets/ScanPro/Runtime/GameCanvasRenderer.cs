using System;
using System.Collections.Generic;
using System.Linq;
using QRCoder;
using UnityEngine;

namespace ScanPro704
{
    public enum CanvasTextOrientation { FlatEditor, CavePreview, VenueOutput }
    public enum TextQuarterTurn { None, Clockwise90, HalfTurn180, CounterClockwise90 }

    public sealed class GameCanvasRenderer : IDisposable
    {
        public Texture2D Texture { get; private set; }
        Color32[] pixels;
        Color32[] basePixels;
        VenueLayout baseVenue;
        int baseMaskRevision = -1;
        bool baseGrid;
        readonly int baseScale;
        int scale;
        int textureWidth, textureHeight;
        ScanLevel qrScaleLevel;
        IReadOnlyDictionary<string, string> qrScaleCodes;
        bool qrScalePrepared;
        static readonly Dictionary<string, List<System.Collections.BitArray>> QrMatrixCache =
            new Dictionary<string, List<System.Collections.BitArray>>();
        static readonly object QrMatrixCacheLock = new object();
        static readonly Color32 White = new Color32(255, 255, 255, 255);
        static readonly Color32 Black = new Color32(0, 0, 0, 255);
        // Nearly black retains full contrast without being treated as an empty wall
        // by the CAVE surface shader's black-background tint.
        static readonly Color32 QrInk = new Color32(2, 2, 2, 255);
        const int MinimumQrModulePixels = 3;
        const float VenuePixelsPerCanvasPixel = 6.4f;
        public GameCanvasRenderer(int scale = 2)
        {
            baseScale = this.scale = Math.Max(1, scale);
            textureWidth = 520 * scale; textureHeight = 420 * scale;
            Texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "704 gameplay canvas" };
            pixels = new Color32[textureWidth * textureHeight];
            basePixels = new Color32[pixels.Length];
        }
        public void Draw(ScanLevel level, double seconds, GameSession session = null, IReadOnlyDictionary<string, string> codes = null, bool grid = false, int layer = -1,
            CanvasTextOrientation textOrientation = CanvasTextOrientation.FlatEditor,
            TransitionSpec? transitionPreview = null, double previewTransitionAge = 0)
        {
            EnsureQrScale(level, codes);
            var revision = level.venue.MaskRevision;
            if (baseVenue != level.venue || baseMaskRevision != revision || baseGrid != grid)
            {
                for (var i = 0; i < pixels.Length; i++) pixels[i] = Black;
                if (grid)
                {
                    var width = level.venue.Width; var height = level.venue.Height;
                    for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                        if (level.venue.IsAllowed(new Vector2Int(x, y))) Fill(x * 10, y * 10, 10, 10, new Color32((byte)((x + y) % 2 == 0 ? 21 : 27), 29, 39, 255));
                }
                Array.Copy(pixels, basePixels, pixels.Length);
                baseVenue = level.venue; baseMaskRevision = revision; baseGrid = grid;
            }
            else Array.Copy(basePixels, pixels, pixels.Length);
            var hazardColor = GameColorSettings.HazardColor;
            if (layer == -1)
            {
                var entry=VenueLayout.EntrancePixels;
                var entrance = new Color32(0, (byte)(40 + 20 * (1 + Math.Sin(seconds * 2))), 100, 255);
                Fill(entry.x,entry.y,entry.width,entry.height,entrance);
            }
            foreach (var item in level.OrderedItems())
            {
                if (layer >= 0 && (int)item.kind != layer) continue;
                GameSession.ItemState state = null;
                if (session != null) session.States.TryGetValue(item.id, out state);
                if (state != null && (state.Completed || (item.proTarget && state.Disabled))) continue;
                if (state != null && session.IsInactiveShutterHazard(item)) continue;
                if (state != null && item.kind == TileKind.SafeGreen && state.Disabled) continue;
                // Color and Color32 convert implicitly in both directions, so the nested
                // conditional is ambiguous to the Unity C# compiler. Resolve each branch into
                // the renderer's pixel type explicitly.
                Color32 color;
                if (state != null && item.kind == TileKind.HoldPurple && state.PurpleHoldActive)
                    color = Color.white;
                else if (state != null && state.Disabled && item.kind == TileKind.HazardRed)
                    color = TilePalette.ColorOf(TileKind.SafeGreen);
                else
                    color = item.kind == TileKind.HazardRed ? hazardColor : TilePalette.ColorOf(item.kind);
                var currentPosition = item.PositionAt(seconds, level.movementSegmentMs);
                var frameIndex = item.AnimationFrameIndex(seconds, level.animationSpeedMilliseconds);
                var p = currentPosition * 10;
                var x = Mathf.RoundToInt(p.x); var y = Mathf.RoundToInt(p.y); var w = item.size.x * 10; var h = item.size.y * 10;
                var purpleClip = session != null && session.IsPurpleClearTarget(item);
                if (item.animationFrames != null && item.animationFrames.Count > 0)
                {
                    var frame = item.animationFrames[frameIndex];
                    if (frame == null) continue;
                    var mask = frame.Pixels;
                    for (var my = 0; my < frame.height; my++) for (var mx = 0; mx < frame.width; mx++)
                    {
                        if (!mask[my * frame.width + mx]) continue;
                        var cellX = x + (frame.offset.x + mx) * 10;
                        var cellY = y + (frame.offset.y + my) * 10;
                        var clearPoint = new Vector2((cellX + 5) / 10f, (cellY + 5) / 10f);
                        if (!purpleClip || !session.IsPurpleClearedAt(item, clearPoint)) Fill(cellX, cellY, 10, 10, color);
                    }
                }
                else if (purpleClip && item.shape == TileShape.Circle)
                    FillCircleWithPurpleClip(item, session, x, y, w, h, color);
                else if (purpleClip)
                {
                    for (var cellY = 0; cellY < item.size.y; cellY++)
                    for (var cellX = 0; cellX < item.size.x; cellX++)
                    {
                        var left = x + cellX * 10;
                        var top = y + cellY * 10;
                        if (!session.IsPurpleClearedAt(item, new Vector2((left + 5) / 10f, (top + 5) / 10f)))
                            Fill(left, top, 10, 10, color);
                    }
                }
                else if (item.shape == TileShape.Circle) FillCircle(x, y, w, h, color);
                else Fill(x, y, w, h, color);
                if (item.kind == TileKind.HoldPurple) continue;
                if (!(item.IsScan
                    ? item.TryGetQrContentBounds(currentPosition, frameIndex, out var content)
                    : item.TryGetContentBounds(currentPosition, frameIndex, out content))) continue;
                var textX = Mathf.RoundToInt(content.x * 10); var textY = Mathf.RoundToInt(content.y * 10);
                var textWidth = Mathf.RoundToInt(content.width * 10); var textHeight = Mathf.RoundToInt(content.height * 10);
                if (!item.IsScan && !item.HasAnimation && item.shape == TileShape.Circle)
                {
                    var inner = TileShapeUtility.InscribedSquarePixels(w, h);
                    textX += (w - inner) / 2; textY += (h - inner) / 2;
                    textWidth = textHeight = inner;
                }
                var text = session == null ? TouchNumberContent.DisplayText(item) : session.Label(item);
                if (item.IsScan && codes != null && codes.TryGetValue(item.id, out var code))
                {
                    var side = Math.Min(textWidth, textHeight);
                    var inset = QrInsetPixels(item, side);
                    DrawQr(code, textX + (textWidth - side) / 2 + inset,
                        textY + (textHeight - side) / 2 + inset, side - inset * 2);
                }
                else if (!string.IsNullOrEmpty(text))
                {
                    // Wall orientation belongs to the blue-cell label; the wall UVs,
                    // tile geometry, QR and touch coordinates remain unchanged.
                    var blueCellRotation = item.kind == TileKind.TouchBlue
                        ? ResolveBlueCellTextRotation(textOrientation, content.center * 10)
                        : TextQuarterTurn.None;
                    DrawText(text, textX, textY, textWidth, textHeight, item.kind == TileKind.TouchBlue, blueCellRotation);
                }
            }
            if (transitionPreview.HasValue || (session != null && session.Outcome.HasValue))
            {
                var effect = transitionPreview ?? TransitionRules.For(session.Outcome.Value, session.LastPosition);
                var age = transitionPreview.HasValue ? previewTransitionAge : session.TransitionAge;
                var radius = (float)(age / 1.2) * new Vector2(52, 42).magnitude;
                for (var y = 0; y < level.venue.Height; y++) for (var x = 0; x < level.venue.Width; x++)
                    if (level.venue.IsAllowed(new Vector2Int(x, y)) && (!effect.Expands || Vector2.Distance(new Vector2(x + .5f, y + .5f), effect.Origin) <= radius)) Fill(x * 10, y * 10, 10, 10, effect.Color);
                if (!effect.Expands) {var entry=VenueLayout.EntrancePixels;Fill(entry.x,entry.y,entry.width,entry.height,effect.Color);}
            }
            if (session != null && session.Level != null && layer < 0 && textOrientation == CanvasTextOrientation.VenueOutput)
                DrawVenueScoreboard(session);
            Texture.SetPixels32(pixels); Texture.Apply(false);
        }
        void DrawVenueScoreboard(GameSession session)
        {
            DrawScoreboardLabel(VenueScoreboardHud.ProgressLabel(session.Level), VenueLayout.ScoreboardProgressPixels);
            DrawScoreboardLabel(VenueScoreboardHud.CountdownLabel(session.Level.timeLimitSeconds, session.Elapsed), VenueLayout.ScoreboardCountdownPixels);
        }
        void DrawScoreboardLabel(string label, RectInt region)
        {
            var center = new Vector2(region.x + region.width * .5f, region.y + region.height * .5f);
            DrawText(label, region.x, region.y, region.width, region.height, false,
                ResolveTextRotation(CanvasTextOrientation.VenueOutput, center));
        }
        void Fill(int x, int y, int w, int h, Color32 color)
        {
            var x0 = Mathf.Clamp(x * scale, 0, textureWidth); var x1 = Mathf.Clamp((x + w) * scale, 0, textureWidth);
            var y0 = Mathf.Clamp(y * scale, 0, textureHeight); var y1 = Mathf.Clamp((y + h) * scale, 0, textureHeight);
            for (var py = y0; py < y1; py++)
            {
                var row = (textureHeight - 1 - py) * textureWidth;
                for (var px = x0; px < x1; px++) pixels[row + px] = color;
            }
        }
        void FillCircle(int x, int y, int w, int h, Color32 color)
        {
            var diameter = Mathf.Min(w, h) * scale;
            if (diameter <= 0) return;
            var x0 = Mathf.Clamp(x * scale, 0, textureWidth);
            var x1 = Mathf.Clamp((x + w) * scale, 0, textureWidth);
            var y0 = Mathf.Clamp(y * scale, 0, textureHeight);
            var y1 = Mathf.Clamp((y + h) * scale, 0, textureHeight);
            var centerX = x * scale + w * scale * .5f;
            var centerY = y * scale + h * scale * .5f;
            var radius = diameter * .5f;
            var radiusSquared = radius * radius;
            for (var py = y0; py < y1; py++)
            {
                var dy = py + .5f - centerY;
                var row = (textureHeight - 1 - py) * textureWidth;
                for (var px = x0; px < x1; px++)
                {
                    var dx = px + .5f - centerX;
                    if (dx * dx + dy * dy < radiusSquared) pixels[row + px] = color;
                }
            }
        }
        void FillCircleWithPurpleClip(LevelItem item, GameSession session, int x, int y, int w, int h, Color32 color)
        {
            var diameter = Mathf.Min(w, h) * scale;
            if (diameter <= 0) return;
            var x0 = Mathf.Clamp(x * scale, 0, textureWidth);
            var x1 = Mathf.Clamp((x + w) * scale, 0, textureWidth);
            var y0 = Mathf.Clamp(y * scale, 0, textureHeight);
            var y1 = Mathf.Clamp((y + h) * scale, 0, textureHeight);
            var centerX = x * scale + w * scale * .5f;
            var centerY = y * scale + h * scale * .5f;
            var radius = diameter * .5f;
            var radiusSquared = radius * radius;
            for (var py = y0; py < y1; py++)
            {
                var dy = py + .5f - centerY;
                var row = (textureHeight - 1 - py) * textureWidth;
                for (var px = x0; px < x1; px++)
                {
                    var dx = px + .5f - centerX;
                    if (dx * dx + dy * dy >= radiusSquared) continue;
                    var clearPoint = new Vector2((px + .5f) / (scale * 10f), (py + .5f) / (scale * 10f));
                    if (!session.IsPurpleClearedAt(item, clearPoint)) pixels[row + px] = color;
                }
            }
        }
        static int QrInsetPixels(LevelItem item, int side)
        {
            // Circles already expose their colored background outside the inscribed
            // square. Small square/animated tiles need a narrower visible border.
            if (!item.HasAnimation && item.shape == TileShape.Circle) return 0;
            return side <= 20 ? 1 : 2;
        }

        void DrawQr(string code, int x, int y, int size)
        {
            var matrix = GetQrMatrix(code);
            // Quantize in the final 704 atlas pixel grid first. In a 2x2 tile,
            // 29 modules at 4 px occupy 116 of 128 px, leaving a 6 px blue border.
            // The 1 px allowance absorbs rounding of the requested logical inset.
            var modulePixels = Mathf.FloorToInt((size * VenuePixelsPerCanvasPixel + 1f) / matrix.Count);
            if (Mathf.FloorToInt(modulePixels * scale / VenuePixelsPerCanvasPixel) < MinimumQrModulePixels)
                throw new InvalidOperationException($"QR {code} requires a higher render scale; refusing to export an undersampled code.");
            var outputSide = matrix.Count * modulePixels;
            var outputX = Mathf.RoundToInt((x + size * .5f) * VenuePixelsPerCanvasPixel - outputSide * .5f);
            var outputY = Mathf.RoundToInt((y + size * .5f) * VenuePixelsPerCanvasPixel - outputSide * .5f);
            // Map the output-aligned edges back into the source texture. Small QR
            // canvases are at least as dense as the atlas, so nearest sampling keeps
            // each module equally wide even after the atlas is scaled down by half.
            for (var row = 0; row < matrix.Count; row++) for (var col = 0; col < matrix.Count; col++)
            {
                var x0 = Mathf.RoundToInt((outputX + col * modulePixels) * scale / VenuePixelsPerCanvasPixel);
                var x1 = Mathf.RoundToInt((outputX + (col + 1) * modulePixels) * scale / VenuePixelsPerCanvasPixel);
                var y0 = Mathf.RoundToInt((outputY + row * modulePixels) * scale / VenuePixelsPerCanvasPixel);
                var y1 = Mathf.RoundToInt((outputY + (row + 1) * modulePixels) * scale / VenuePixelsPerCanvasPixel);
                var color = matrix[row][col] ? QrInk : White;
                for (var py = Math.Max(0, y0); py < Math.Min(textureHeight, y1); py++)
                for (var px = Math.Max(0, x0); px < Math.Min(textureWidth, x1); px++)
                {
                    pixels[(textureHeight - 1 - py) * textureWidth + px] = color;
                }
            }
        }

        public int RenderScale => scale;

        public void InvalidateQrScale() => qrScalePrepared = false;

        public void PrepareQrScale(ScanLevel level, IReadOnlyDictionary<string, string> codes)
        {
            var required = RequiredQrScale(level, codes, baseScale);
            if (required != scale)
            {
                scale = required;
                textureWidth = 520 * scale; textureHeight = 420 * scale;
                var previous = Texture;
                Texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false)
                    { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "704 gameplay canvas" };
                pixels = new Color32[textureWidth * textureHeight];
                basePixels = new Color32[pixels.Length];
                baseVenue = null; baseMaskRevision = -1;
                if (Application.isPlaying) UnityEngine.Object.Destroy(previous);
                else UnityEngine.Object.DestroyImmediate(previous);
            }
            qrScaleLevel = level;
            qrScaleCodes = codes;
            qrScalePrepared = true;
        }

        public static int RequiredQrScale(ScanLevel level, IReadOnlyDictionary<string, string> codes, int baseScale)
        {
            var required = Math.Max(1, baseScale);
            if (level == null || codes == null) return required;
            var moduleCounts = new Dictionary<string, int>();
            foreach (var item in level.items)
            {
                if (item == null || !item.IsScan || !codes.TryGetValue(item.id, out var code)) continue;
                if (!moduleCounts.TryGetValue(code, out var modules))
                    moduleCounts.Add(code, modules = GetQrMatrix(code).Count);
                var frameCount = item.HasAnimation ? item.animationFrames.Count : 1;
                for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
                {
                    if (!item.TryGetQrContentBounds(item.position, frameIndex, out var content)) continue;
                    var side = Mathf.RoundToInt(Mathf.Min(content.width, content.height) * 10f);
                    var logicalSide = side - QrInsetPixels(item, side) * 2;
                    if (logicalSide <= 0) continue;
                    var outputModulePixels = Mathf.Max(1,
                        Mathf.FloorToInt((logicalSide * VenuePixelsPerCanvasPixel + 1f) / modules));
                    required = Math.Max(required,
                        Mathf.CeilToInt(MinimumQrModulePixels * VenuePixelsPerCanvasPixel / outputModulePixels));
                    if (side <= 20) required = Math.Max(required, Mathf.CeilToInt(VenuePixelsPerCanvasPixel));
                }
            }
            return required;
        }

        static List<System.Collections.BitArray> GetQrMatrix(string code)
        {
            lock (QrMatrixCacheLock)
            {
                if (QrMatrixCache.TryGetValue(code, out var matrix)) return matrix;
                using (var generator = new QRCodeGenerator())
                using (var data = generator.CreateQrCode(code, QRCodeGenerator.ECCLevel.Q))
                    matrix = data.ModuleMatrix.Select(row => new System.Collections.BitArray(row)).ToList();
                if (QrMatrixCache.Count >= 512) QrMatrixCache.Clear();
                QrMatrixCache.Add(code, matrix);
                return matrix;
            }
        }

        void EnsureQrScale(ScanLevel level, IReadOnlyDictionary<string, string> codes)
        {
            if (qrScalePrepared && ReferenceEquals(qrScaleLevel, level) && ReferenceEquals(qrScaleCodes, codes)) return;
            PrepareQrScale(level, codes);
        }
        const int GlyphHeight = 7;
        const int GlyphGapColumns = 1;
        const int UnderlineGapRows = 1;
        static readonly Dictionary<char, string[]> Font = new Dictionary<char, string[]>
        {
            // Hand-drawn 5×7 numerals: the narrow 1 keeps 10–12 legible in a 1×1 cell.
            ['0']=new[]{"01110","10001","10011","10101","11001","10001","01110"},
            ['1']=new[]{"010","110","010","010","010","010","111"},
            ['2']=new[]{"01110","10001","00001","00010","00100","01000","11111"},
            ['3']=new[]{"11110","00001","00001","01110","00001","00001","11110"},
            ['4']=new[]{"00010","00110","01010","10010","11111","00010","00010"},
            ['5']=new[]{"11111","10000","10000","11110","00001","00001","11110"},
            ['6']=new[]{"01110","10000","10000","11110","10001","10001","01110"},
            ['7']=new[]{"11111","00001","00010","00100","01000","01000","01000"},
            ['8']=new[]{"01110","10001","10001","01110","10001","10001","01110"},
            ['9']=new[]{"01110","10001","10001","01111","00001","00001","01110"},
            ['-']=new[]{"00000","00000","00000","11111","00000","00000","00000"},
            ['+']=new[]{"00100","00100","00100","11111","00100","00100","00100"},
            ['/']=new[]{"00001","00010","00010","00100","01000","01000","10000"},
            ['x']=new[]{"10001","01010","00100","00100","00100","01010","10001"},
            ['H']=new[]{"10001","10001","10001","11111","10001","10001","10001"},
            ['−']=new[]{"00000","00000","00000","11111","00000","00000","00000"},
            ['s']=new[]{"01111","10000","10000","01110","00001","00001","11110"}
        };
        static int GlyphWidth(char character) => Font.TryGetValue(character, out var rows) ? rows[0].Length : 5;

        /// <summary>Measures the same grid used by rendering and content-capacity checks.</summary>
        public static Vector2Int MeasureTextGrid(string text, bool underlineOrientationDigits = false)
        {
            if (string.IsNullOrEmpty(text)) return Vector2Int.zero;
            var columns = 0;
            for (var i = 0; i < text.Length; i++)
            {
                columns += GlyphWidth(text[i]);
                if (i < text.Length - 1) columns += GlyphGapColumns;
            }
            var rows = GlyphHeight + (HasOrientationUnderline(text, underlineOrientationDigits) ? UnderlineGapRows + 1 : 0);
            return new Vector2Int(columns, rows);
        }
        public static TextQuarterTurn ResolveTextRotation(CanvasTextOrientation orientation, Vector2 centerPixels)
        {
            if (orientation == CanvasTextOrientation.CavePreview)
            {
                return ResolveCaveWallRotation(centerPixels);
            }
            if (orientation == CanvasTextOrientation.VenueOutput)
                return ResolveVenueOutputRotation(centerPixels);
            return TextQuarterTurn.None;
        }

        public static TextQuarterTurn ResolveBlueCellTextRotation(CanvasTextOrientation orientation, Vector2 centerPixels)
        {
            // Keep blue-cell numerals at the same local angle in the CAVE preview and venue output.
            // The atlas mapping, wall UVs, and non-blue venue labels retain their existing rules.
            return orientation == CanvasTextOrientation.VenueOutput
                ? ResolveCaveWallRotation(centerPixels)
                : ResolveTextRotation(orientation, centerPixels);
        }

        static bool Contains(Vector2 point, int x, int y, int width, int height) =>
            point.x >= x && point.x < x + width && point.y >= y && point.y < y + height;

        static TextQuarterTurn ResolveCaveWallRotation(Vector2 point)
        {
            if (Contains(point, 0, 100, 100, 220)) return TextQuarterTurn.CounterClockwise90;
            if (Contains(point, 420, 100, 100, 220)) return TextQuarterTurn.Clockwise90;
            if (Contains(point, 100, 320, 320, 100)) return TextQuarterTurn.HalfTurn180;
            return TextQuarterTurn.None;
        }

        static bool IsCaveWall(Vector2 point) =>
            Contains(point, 0, 100, 100, 220) || Contains(point, 420, 100, 100, 220) ||
            Contains(point, 100, 320, 320, 100) || Contains(point, 100, 0, 200, 100) ||
            Contains(point, 380, 0, 40, 100);

        static TextQuarterTurn ResolveVenueOutputRotation(Vector2 point)
        {
            // The output shader rotates the complete 520×420 canvas clockwise. The
            // physical right wall (source top strip / output Face 4) is the confirmed
            // upright reference, so counter-rotate text on each of the four wall faces
            // before atlas composition. Floor and entrance remain in their own frame.
            if (!IsCaveWall(point)) return TextQuarterTurn.None; // floor and entrance
            return TextQuarterTurn.CounterClockwise90;
        }

        void DrawText(string text, int x, int y, int width, int height, bool underlineOrientationDigits = false,
            TextQuarterTurn rotation = TextQuarterTurn.None)
        {
            var grid = MeasureTextGrid(text, underlineOrientationDigits);
            if (grid.x < 1 || grid.y < 1) return;
            var quarterTurned = rotation == TextQuarterTurn.Clockwise90 || rotation == TextQuarterTurn.CounterClockwise90;
            var fitWidth = quarterTurned ? height : width;
            var fitHeight = quarterTurned ? width : height;
            var unit = Math.Min(fitWidth / grid.x, fitHeight / grid.y);
            if (unit < 1) return;
            var glyphWidth = grid.x * unit;
            var glyphHeight = grid.y * unit;
            var ox = x + (width - (quarterTurned ? glyphHeight : glyphWidth)) / 2;
            var oy = y + (height - (quarterTurned ? glyphWidth : glyphHeight)) / 2;
            void GlyphPixel(int cellX, int cellY)
            {
                var localX = cellX * unit;
                var localY = cellY * unit;
                if (rotation == TextQuarterTurn.Clockwise90)
                    Fill(ox + glyphHeight - localY - unit, oy + localX, unit, unit, White);
                else if (rotation == TextQuarterTurn.CounterClockwise90)
                    Fill(ox + localY, oy + glyphWidth - localX - unit, unit, unit, White);
                else if (rotation == TextQuarterTurn.HalfTurn180)
                    Fill(ox + glyphWidth - localX - unit, oy + glyphHeight - localY - unit, unit, unit, White);
                else Fill(ox + localX, oy + localY, unit, unit, White);
            }
            var cursorX = 0;
            for (var characterIndex = 0; characterIndex < text.Length; characterIndex++)
            {
                var character = text[characterIndex];
                if (Font.TryGetValue(character, out var rows))
                {
                    var characterWidth = rows[0].Length;
                    for (var yy = 0; yy < GlyphHeight; yy++) for (var xx = 0; xx < characterWidth; xx++)
                        if (rows[yy][xx] == '1') GlyphPixel(cursorX + xx, yy);
                    if (underlineOrientationDigits && (character == '2' || character == '5' || character == '6' || character == '9'))
                        for (var xx = 0; xx < characterWidth; xx++)
                            GlyphPixel(cursorX + xx, GlyphHeight + UnderlineGapRows);
                    cursorX += characterWidth;
                }
                else cursorX += GlyphWidth(character);
                if (characterIndex < text.Length - 1) cursorX += GlyphGapColumns;
            }
        }

        public static bool HasOrientationUnderline(string text, bool enabled = true) => enabled && !string.IsNullOrEmpty(text) &&
            (text.IndexOf('2') >= 0 || text.IndexOf('5') >= 0 || text.IndexOf('6') >= 0 || text.IndexOf('9') >= 0);
        public void Dispose() { UnityEngine.Object.DestroyImmediate(Texture); }
    }
}
