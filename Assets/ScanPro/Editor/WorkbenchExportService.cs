using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ScanPro704.Editor
{
    /// <summary>PNG, GIF, atlas export, and render-texture file writing for the workbench.</summary>
    internal static class WorkbenchExportService
    {
        public static void ExportEditorPng(ScanLevel level, double seconds, Dictionary<string, string> codes, int exportLayer)
        {
            var renderScale = ConfirmQrScale(level, codes, 1, "編輯圖 PNG");
            if (renderScale == 0) return;
            var defaultName = $"Level_{level.levelNumber:00}_EDITOR{(renderScale > 1 ? $"_{renderScale}x" : "")}";
            var path = EditorUtility.SaveFilePanel("匯出編輯圖（非場域輸出）", "", defaultName, "png");
            if (string.IsNullOrEmpty(path)) return;
            using (var export = new GameCanvasRenderer(renderScale))
            {
                export.Draw(level, seconds, CreateInitialPreviewSession(level), codes, false, exportLayer - 1);
                File.WriteAllBytes(path, export.Texture.EncodeToPNG());
            }
            EditorUtility.RevealInFinder(path);
        }

        public static void ExportGif(ScanLevel level, Dictionary<string, string> codes)
        {
            var renderScale = ConfirmQrScale(level, codes, 1, "路徑 GIF");
            if (renderScale == 0) return;
            var defaultName = $"Level_{level.levelNumber:00}_EDITOR{(renderScale > 1 ? $"_{renderScale}x" : "")}";
            var path = EditorUtility.SaveFilePanel("匯出路徑 GIF", "", defaultName, "gif");
            if (string.IsNullOrEmpty(path)) return;
            var previewSession = CreateInitialPreviewSession(level);
            using (var export = new GameCanvasRenderer(renderScale))
            using (var gif = new AnimatedGif(path, 520 * renderScale, 420 * renderScale))
                for (var frame = 0; frame < 40; frame++)
                {
                    export.Draw(level, frame * .1, previewSession, codes);
                    gif.Add(export.Texture.GetPixels32());
                }
            EditorUtility.RevealInFinder(path);
        }

        public static void ExportAtlas(ScanLevel level, double seconds, Dictionary<string, string> codes)
        {
            var renderScale = ConfirmQrScale(level, codes, 2, "場域 atlas PNG");
            if (renderScale == 0) return;
            var path = EditorUtility.SaveFilePanel("匯出場域 PNG", "", $"Level_{level.levelNumber:00}_OUTPUT", "png");
            if (string.IsNullOrEmpty(path)) return;
            var shader = Shader.Find("704/Output Atlas");
            if (shader == null) throw new System.InvalidOperationException("找不到 704/Output Atlas shader。");
            using (var export = new GameCanvasRenderer(renderScale))
            {
                var material = new Material(shader);
                var target = new RenderTexture(2688, 3840, 0);
                try
                {
                    target.Create();
                    export.Draw(level, seconds, CreateInitialPreviewSession(level), codes, false, -1, CanvasTextOrientation.VenueOutput);
                    OutputCompositor.Blit(export.Texture, target, material);
                    WriteRenderTexture(target, path);
                }
                finally
                {
                    target.Release();
                    Object.DestroyImmediate(target);
                    Object.DestroyImmediate(material);
                }
            }
            EditorUtility.RevealInFinder(path);
        }

        internal static GameSession CreateInitialPreviewSession(ScanLevel level)
        {
            var session = new GameSession(704);
            session.Start(level);
            return session;
        }

        static int ConfirmQrScale(ScanLevel level, Dictionary<string, string> codes, int baseScale, string outputName)
        {
            var scale = GameCanvasRenderer.RequiredQrScale(level, codes, baseScale);
            if (scale <= baseScale) return baseScale;
            var logicalScale = $"{520 * scale}×{420 * scale}";
            var outputSizeNote = outputName == "場域 atlas PNG"
                ? "場域輸出維持固定 2688×3840；只提高內部 QR 渲染解析度。"
                : $"編輯 PNG／GIF 尺寸將為 {logicalScale}。";
            return EditorUtility.DisplayDialog("QR 需要高解析度輸出",
                $"此關最小 QR 需要 {scale}× 渲染，才能以完整 QR 模組與白邊輸出 {outputName}。\n{outputSizeNote}",
                $"匯出 {scale}×", "取消") ? scale : 0;
        }

        public static void WriteRenderTexture(RenderTexture target, string path)
        {
            var previous = RenderTexture.active;
            Texture2D image = null;
            try
            {
                RenderTexture.active = target;
                image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                if (image != null) Object.DestroyImmediate(image);
            }
        }

        public static Texture2D CreateEditorTexture(ScanLevel source, double seconds)
        {
            var background = source.venue.background;
            var image = new Texture2D(background.width, background.height, TextureFormat.RGBA32, false);
            image.SetPixels(background.GetPixels());
            var unit = source.venue.pixelsPerCell;
            foreach (var item in source.OrderedItems())
            {
                var p = item.PositionAt(seconds, source.movementSegmentMs) * unit;
                for (var y = 0; y < item.size.y * unit; y++)
                    for (var x = 0; x < item.size.x * unit; x++)
                    {
                        var px = Mathf.RoundToInt(p.x) + x;
                        var py = Mathf.RoundToInt(p.y) + y;
                        if (px < 0 || py < 0 || px >= image.width || py >= image.height) continue;
                        if (!source.venue.IsAllowed(new Vector2Int(px / unit, py / unit))) continue;
                        if (item.shape == TileShape.Circle)
                        {
                            var point = new Vector2(px + .5f, py + .5f);
                            if (!TileShapeUtility.Contains(item, p / unit, point / unit)) continue;
                        }
                        image.SetPixel(px, image.height - 1 - py, TilePalette.ColorOf(item.kind));
                    }
            }
            image.Apply();
            return image;
        }
    }
}
