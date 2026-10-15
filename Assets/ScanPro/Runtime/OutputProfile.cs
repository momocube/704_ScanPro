using System;
using System.Collections.Generic;
using UnityEngine;

namespace ScanPro704
{
    public enum SurfaceRotation { Degrees0, Degrees90, Degrees180, Degrees270 }
    [Serializable]
    public sealed class SurfaceMapping
    {
        public int faceIndex;
        public string name = "Wall Bottom";
        [Tooltip("編輯底圖中的像素矩形，左上角為原點。")]
        public RectInt sourcePixels;
        [Tooltip("場域輸出中的像素矩形，左上角為原點。不可直接沿用編輯圖尺寸。")]
        public RectInt outputPixels;
        public SurfaceRotation rotation;
    }

    [CreateAssetMenu(menuName = "704/Output Profile")]
    public sealed class OutputProfile : ScriptableObject
    {
        public static readonly RectInt EntranceOutputPixels = new RectInt(2048,1920,640,512);
        public static readonly RectInt ScoreboardOutputPixels = new RectInt(2048,2432,640,256);
        public VenueLayout editorLayout;
        [Tooltip("Player 將此 Shader 用於場域畫布等比例輸出，確保建置會包含它。")]
        public Shader presentationShader;
        [Tooltip("待場域資料確認；0 表示尚未設定。")]
        public int outputWidth;
        public int outputHeight;
        [HideInInspector]
        public int fullscreenDisplay;
        [Range(-.5f,.5f), InspectorName("全螢幕水平微調")]
        [Tooltip("調整等比例畫布在橫向螢幕上的水平位置，不會裁切；負值往左。704 原尺寸畫面沒有左右留白，因此不會偏移。")]
        public float fullscreenHorizontalOffset = -.13f;
        public List<SurfaceMapping> surfaces = new List<SurfaceMapping>();
        [TextArea] public string calibrationNotes = "等待確認實際輸出尺寸與各面位置。Wall Bottom 下方不存在區域須保留黑色，不在編輯底圖中補畫。";

        public bool Validate(out string reason)
        {
            if (editorLayout == null || editorLayout.background == null) { reason = "尚未指定編輯底圖。"; return false; }
            if (outputWidth <= 0 || outputHeight <= 0) { reason = "場域輸出尺寸尚未確認；不可沿用 520×420。"; return false; }
            if (fullscreenDisplay < 0) { reason = "全螢幕顯示器索引不能小於 0。"; return false; }
            if (fullscreenHorizontalOffset < -.5f || fullscreenHorizontalOffset > .5f) { reason = "全螢幕水平微調須介於 -0.5 與 0.5。"; return false; }
            if (surfaces.Count == 0) { reason = "尚未設定各面的輸出映射。"; return false; }
            if(!HasFixedEntrance(surfaces,out reason)) return false;
            if(!HasFixedScoreboard(surfaces,out reason)) return false;
            for (var i = 0; i < surfaces.Count; i++)
            {
                var s = surfaces[i];
                if (s == null || !Inside(s.sourcePixels, editorLayout.background.width, editorLayout.background.height) ||
                    !Inside(s.outputPixels, outputWidth, outputHeight))
                { reason = $"第 {i + 1} 面的來源或目的矩形未設定／超出範圍。"; return false; }
                for (var j = 0; j < i; j++)
                    if (s.outputPixels.Overlaps(surfaces[j].outputPixels))
                    { reason = $"{s.name} 與 {surfaces[j].name} 的輸出區域重疊。"; return false; }
            }
            reason = "輸出設定完整；仍須現場校正。"; return true;
        }

        public static bool HasFixedEntrance(IReadOnlyList<SurfaceMapping> mappings,out string reason)
        {
            var entrances=0;
            foreach(var surface in mappings)
            {
                if(surface==null) continue;
                if(surface.faceIndex==5)
                {
                    entrances++;
                    if(!SameRect(surface.sourcePixels,VenueLayout.EntrancePixels) || !SameRect(surface.outputPixels,EntranceOutputPixels) || surface.rotation!=SurfaceRotation.Degrees90)
                    {reason="入口位置為全關卡固定配置，不可移動或改變大小。";return false;}
                }
                else if(surface.sourcePixels.Overlaps(VenueLayout.EntrancePixels))
                {reason=$"{surface.name} 的來源不能佔用入口禁止區域。";return false;}
            }
            reason=entrances==1 ? null : "必須保留唯一的固定入口面。";
            return entrances==1;
        }

        public static bool HasFixedScoreboard(IReadOnlyList<SurfaceMapping> mappings,out string reason)
        {
            var scoreboards=0;
            foreach(var surface in mappings)
            {
                if(surface==null) continue;
                if(surface.faceIndex==6)
                {
                    scoreboards++;
                    if(!SameRect(surface.sourcePixels,VenueLayout.ScoreboardPixels) || !SameRect(surface.outputPixels,ScoreboardOutputPixels) || surface.rotation!=SurfaceRotation.Degrees90)
                    {reason="記分板固定在東小牆整面，不可移動或改變大小。";return false;}
                }
                else if(surface.sourcePixels.Overlaps(VenueLayout.ScoreboardPixels))
                {reason=$"{surface.name} 的來源不能佔用東小牆記分板區域。";return false;}
            }
            reason=scoreboards==1 ? null : "必須保留唯一的東小牆記分板面。";
            return scoreboards==1;
        }

        static bool Inside(RectInt r, int width, int height) => r.width > 0 && r.height > 0 && r.x >= 0 && r.y >= 0 && r.xMax <= width && r.yMax <= height;
        static bool SameRect(RectInt a,RectInt b) => a.x==b.x && a.y==b.y && a.width==b.width && a.height==b.height;

        public bool TryMap(Vector2 sourceTopLeftPixel, out Vector2 outputTopLeftPixel)
        {
            outputTopLeftPixel = default;
            if (!Validate(out _)) return false;
            foreach (var s in surfaces)
            {
                var r = s.sourcePixels;
                if (sourceTopLeftPixel.x < r.xMin || sourceTopLeftPixel.y < r.yMin || sourceTopLeftPixel.x >= r.xMax || sourceTopLeftPixel.y >= r.yMax) continue;
                var uv = new Vector2((sourceTopLeftPixel.x - r.x) / r.width, (sourceTopLeftPixel.y - r.y) / r.height);
                switch (s.rotation)
                {
                    case SurfaceRotation.Degrees90: uv = new Vector2(1 - uv.y, uv.x); break;
                    case SurfaceRotation.Degrees180: uv = Vector2.one - uv; break;
                    case SurfaceRotation.Degrees270: uv = new Vector2(uv.y, 1 - uv.x); break;
                }
                outputTopLeftPixel = new Vector2(s.outputPixels.x + uv.x * s.outputPixels.width, s.outputPixels.y + uv.y * s.outputPixels.height);
                return true;
            }
            return false;
        }

        public bool TryUnmap(int faceIndex, Vector2 outputTopLeftPixel, out Vector2 sourceTopLeftPixel)
        {
            sourceTopLeftPixel = default;
            if(faceIndex==5) return false;
            if (!Validate(out _)) return false;
            foreach (var s in surfaces)
            {
                if (s.faceIndex != faceIndex) continue;
                var r = s.outputPixels;
                // TouchService rawMax can equal the far edge; clamp it within the face.
                if (outputTopLeftPixel.x < r.xMin || outputTopLeftPixel.y < r.yMin || outputTopLeftPixel.x > r.xMax || outputTopLeftPixel.y > r.yMax) return false;
                var uv = new Vector2(Mathf.Clamp01((outputTopLeftPixel.x - r.x) / r.width), Mathf.Clamp01((outputTopLeftPixel.y - r.y) / r.height));
                switch (s.rotation)
                {
                    case SurfaceRotation.Degrees90: uv = new Vector2(uv.y, 1 - uv.x); break;
                    case SurfaceRotation.Degrees180: uv = Vector2.one - uv; break;
                    case SurfaceRotation.Degrees270: uv = new Vector2(1 - uv.y, uv.x); break;
                }
                sourceTopLeftPixel = new Vector2(Mathf.Clamp(s.sourcePixels.x + uv.x * s.sourcePixels.width, s.sourcePixels.xMin, s.sourcePixels.xMax - .001f),
                    Mathf.Clamp(s.sourcePixels.y + uv.y * s.sourcePixels.height, s.sourcePixels.yMin, s.sourcePixels.yMax - .001f));
                return editorLayout.IsAllowed(Vector2Int.FloorToInt(sourceTopLeftPixel / editorLayout.pixelsPerCell));
            }
            return false;
        }

        public void Configure704()
        {
            outputWidth = 2688; outputHeight = 3840; surfaces.Clear();
            Add(0, "Wall Left", new RectInt(100, 320, 320, 100), new RectInt(0, 640, 640, 2048));
            Add(1, "Wall Button", new RectInt(420, 100, 100, 220), new RectInt(640, 2688, 1408, 640));
            Add(2, "Floor", new RectInt(100, 100, 320, 220), new RectInt(640, 640, 1408, 2048));
            Add(3, "Wall Top", new RectInt(0, 100, 100, 220), new RectInt(640, 0, 1408, 640));
            Add(4, "Wall Right Big", new RectInt(100, 0, 200, 100), new RectInt(2048, 640, 640, 1280));
            Add(5, "Entrance", VenueLayout.EntrancePixels, EntranceOutputPixels);
            Add(6, "Wall Right little - Scoreboard", VenueLayout.ScoreboardPixels, ScoreboardOutputPixels);
            calibrationNotes = "依 TouchService/venues/704.json 的七面矩形設定。CW 90° ×6.4；y>=3328 保留黑色。尺寸與數學映射已核對，現場方向與公分比例仍需驗收。";
        }
        void Add(int index, string label, RectInt source, RectInt destination) => surfaces.Add(new SurfaceMapping
            { faceIndex = index, name = label, sourcePixels = source, outputPixels = destination, rotation = SurfaceRotation.Degrees90 });
    }
}
