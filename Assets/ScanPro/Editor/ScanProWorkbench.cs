using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScanPro704.Editor
{
    public sealed class ScanProWorkbench : EditorWindow
    {
        const string Root = "Assets/ScanPro";
        const string DraftFolder = Root + "/LevelDrafts";
        const string LevelFolder = Root + "/Levels";
        [SerializeField] ScanLevel level;
        [SerializeField] int tool;
        [SerializeField] TileKind brushKind;
        [SerializeField] TileShape brushShape = TileShape.Square;
        [SerializeField] Vector2Int brushSize = new Vector2Int(2, 2);
        [SerializeField] string brushGroup = "";
        [SerializeField] bool showGrid = true;
        int selected = -1;
        bool draggingItem, dragUndoCreated;
        int draggingItemIndex = -1, dragUndoGroup = -1;
        int itemDragControlId;
        Vector2 dragGrabOffset, dragVisualStart;
        Vector2Int dragStartPosition;
        Vector2Int[] dragStartWaypoints = Array.Empty<Vector2Int>();
        ScanLevel[] levels = Array.Empty<ScanLevel>();
        Vector2 scroll;
        Vector2Int lastPaintCell = new Vector2Int(-1, -1);
        double lastUpdate, previewSeconds, lastSourceCheck;
        bool playing;
        Dictionary<string, string> codes = new Dictionary<string, string>();
        int previewView;
        bool trial, mouseHeld;
        Vector2 trialMouse;
        GameSession trialGame;
        GameCanvasRenderer renderer;
        Material atlasMaterial;
        RenderTexture atlasPreview;
        CaveRoom cavePreview;
        bool previewDirty = true;
        bool caveCameraDirty = true;
        int observedColorDirty;
        bool draggingSymmetryAxis;
        int symmetryUndoGroup = -1;
        int symmetryAxisControlId;
        bool draggingShape;
        Vector2Int shapeDragStart, shapeDragEnd;
        int shapeDragControlId;
        bool orbitingCave;
        bool pathAwaitingItem;
        bool pathFinishRequested;
        int caveOrbitControlId;
        int observedLevelDirty, observedMaskRevision;
        VenueLayout observedVenue;
        readonly GameTouch[] mouseTouches = new GameTouch[1];
        string[] levelNames = Array.Empty<string>();
        string placementMessage;
        string consoleClearFeedback = "";
        GUIStyle entranceLabel;
        GUIStyle scoreboardLabel;
        static readonly string[] ViewNames = { "平面編輯", "CAVE 預覽", "場域輸出" };
        bool CanEditLevel => CanEditAsset(level);
        bool IsDraftLevel => IsDraftAsset(level);
        bool CanDeleteLevel => CanEditLevel;

        [MenuItem("704/關卡工作台")]
        public static void Open()
        {
            var window = GetWindow<ScanProWorkbench>();
            window.titleContent = new GUIContent("704 掃碼 Pro");
            window.minSize = new Vector2(1080, 760);
            window.Show();
        }

        void OnEnable()
        {
            minSize = new Vector2(1080, 760);
            wantsMouseMove = true;
            ReloadLevels();
            lastUpdate = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
            Undo.undoRedoPerformed += OnUndo;
            ResetPreview();
        }
        void OnDisable()
        {
            FinishCaveOrbit();
            FinishItemDrag(); FinishShapeDrag(); FinishSymmetryAxis();
            EditorApplication.update -= Tick; Undo.undoRedoPerformed -= OnUndo;
            renderer?.Dispose(); cavePreview?.Dispose();
            if (atlasPreview != null) { atlasPreview.Release(); DestroyImmediate(atlasPreview); }
            if (atlasMaterial != null) DestroyImmediate(atlasMaterial);
        }
        void OnUndo()
        {
            FinishItemDrag(); FinishShapeDrag();
            draggingSymmetryAxis = false; symmetryUndoGroup = -1;
            if (symmetryAxisControlId != 0 && GUIUtility.hotControl == symmetryAxisControlId) GUIUtility.hotControl = 0;
            symmetryAxisControlId = 0; selected = -1; ResetPreview(); Repaint();
        }
        void OnProjectChange() { ReloadLevels(); ResetPreview(); Repaint(); }
        void RefreshChangedSource()
        {
            var colors = GameColorSettings.Shared;
            var colorDirty = colors == null ? 0 : EditorUtility.GetDirtyCount(colors);
            if (observedColorDirty != colorDirty)
            {
                observedColorDirty = colorDirty;
                previewDirty = true;
                Repaint();
            }
            if (level == null || level.venue == null) return;
            if (observedLevelDirty == EditorUtility.GetDirtyCount(level) && observedVenue == level.venue && observedMaskRevision == level.venue.MaskRevision) return;
            ResetPreview(); Repaint();
        }
        void ReloadLevels()
        {
            levels = AssetDatabase.FindAssets("t:ScanLevel", new[] { Root }).Select(g => AssetDatabase.LoadAssetAtPath<ScanLevel>(AssetDatabase.GUIDToAssetPath(g)))
                .OrderBy(l => l.levelNumber).ThenBy(l => l.DisplayTitle, StringComparer.Ordinal)
                .ThenBy(l => AssetDatabase.GetAssetPath(l), StringComparer.Ordinal).ToArray();
            if (level == null) level = levels.FirstOrDefault();
            levelNames = levels.Select(LevelPickerLabel).ToArray();
            previewDirty = true;
        }
        internal static bool IsDraftAsset(ScanLevel asset) => asset != null &&
            AssetDatabase.GetAssetPath(asset).StartsWith(DraftFolder + "/", StringComparison.OrdinalIgnoreCase);
        internal static bool CanEditAsset(ScanLevel asset)
        {
            if (IsDraftAsset(asset)) return true;
            if (asset == null || asset.levelNumber < 1 || asset.levelNumber > 5) return false;
            return AssetDatabase.GetAssetPath(asset).StartsWith(LevelFolder + "/", StringComparison.OrdinalIgnoreCase);
        }
        internal static string LevelPickerLabel(ScanLevel asset)
        {
            if (IsDraftAsset(asset) && PrototypeBuild.IsOutputCandidate(asset))
                return $"{Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(asset))}  " +
                    (asset.excludeFromOutput ? "[匯入／不輸出]" : "[匯入／可輸出]");
            var status = IsDraftAsset(asset) ? "[草稿]" : CanEditAsset(asset) ? "[可編輯]" : "[唯讀]";
            return $"{asset.DisplayTitle}  {status}";
        }
        void Tick()
        {
            var now = EditorApplication.timeSinceStartup;
            if (now - lastSourceCheck >= .1) { lastSourceCheck = now; RefreshChangedSource(); }
            if (now - lastUpdate < 1.0 / 60) return;
            var dt = Math.Min(.1, now - lastUpdate);
            lastUpdate = now;
            if (!playing) return;
            if (playing && trial && trialGame != null)
            {
                mouseTouches[0] = new GameTouch(-1, trialMouse);
                trialGame.Tick(dt, mouseHeld ? mouseTouches : Array.Empty<GameTouch>());
                previewSeconds = trialGame.Elapsed;
                if (trialGame.Outcome.HasValue && trialGame.TransitionAge >= 1.3) playing = false;
            }
            else if (playing) previewSeconds += dt;
            previewDirty = true; Repaint();
        }

        void ResetPreview()
        {
            if (renderer != null) renderer.InvalidateQrScale();
            previewSeconds = 0; playing = false;
            placementMessage=null;
            codes = new Dictionary<string, string>();
            trialGame = null;
            if (level != null)
            {
                trialGame = new GameSession(Environment.TickCount);
                trialGame.Start(level);
                codes = trialGame.Codes;
            }
            mouseHeld = false;
            previewDirty = true;
            levelNames = levels.Select(LevelPickerLabel).ToArray();
            observedLevelDirty = level == null ? 0 : EditorUtility.GetDirtyCount(level);
            observedVenue = level == null ? null : level.venue;
            observedMaskRevision = observedVenue == null ? 0 : observedVenue.MaskRevision;
        }

        void OnGUI()
        {
            if (Event.current.type == EventType.MouseUp)
            {
                lastPaintCell = new Vector2Int(-1, -1); mouseHeld = false; FinishItemDrag(); FinishShapeDrag();
            }
            // Finishing path selection clears the current selection, which changes how many controls
            // the inspector draws. Apply it at the next layout pass. Keep drawing the full GUI for this
            // handled key event so every event walks the same GUILayout tree.
            if (Event.current.type == EventType.Layout && pathFinishRequested)
            {
                pathFinishRequested = false;
                FinishPathSelection();
            }
            if (tool == 5 && ShouldFinishPathSelection(Event.current, EditorGUIUtility.editingTextField))
            {
                pathFinishRequested = true;
                Event.current.Use();
                Repaint();
            }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("704  /  掃碼 Pro", EditorStyles.boldLabel, GUILayout.Width(145));
                var index = Array.IndexOf(levels, level);
                if (levels.Length > 0)
                {
                    var next = EditorGUILayout.Popup(index, levelNames, GUILayout.MinWidth(210));
                    if (next >= 0 && next < levels.Length && next != index) { level = levels[next]; selected = -1; ResetPreview(); }
                }
                else GUILayout.Label("目前沒有關卡", GUILayout.MinWidth(210));
                if (GUILayout.Button("匯入關卡包zip檔", EditorStyles.toolbarButton, GUILayout.Width(125))) ImportLevelPackage();
                if (GUILayout.Button("重新整理", EditorStyles.toolbarButton, GUILayout.Width(75))) ReloadLevels();
                if (GUILayout.Button("新建草稿", EditorStyles.toolbarButton, GUILayout.Width(75))) NewLevel();
                using (new EditorGUI.DisabledScope(!CanDeleteLevel))
                    if (GUILayout.Button("刪除關卡", EditorStyles.toolbarButton, GUILayout.Width(75))) DeleteLevel();
                using (new EditorGUI.DisabledScope(IsDraftLevel))
                    if (GUILayout.Button("建立草稿副本", EditorStyles.toolbarButton, GUILayout.Width(105))) CreateDraftCopy();
                using (new EditorGUI.DisabledScope(!CanEditLevel))
                    if (GUILayout.Button("儲存關卡", EditorStyles.toolbarButton, GUILayout.Width(75))) AssetDatabase.SaveAssetIfDirty(level);
                using (new EditorGUI.DisabledScope(!IsDraftLevel || PrototypeBuild.IsOutputCandidate(level)))
                    if (GUILayout.Button("另存正式關卡", EditorStyles.toolbarButton, GUILayout.Width(95))) PublishDraftAsNewLevel();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("製作規格", EditorStyles.toolbarButton, GUILayout.Width(75)))
                    Application.OpenURL(Path.GetFullPath("docs/Specification.md"));
            }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (PrototypeBuild.IsOutputCandidate(level))
                {
                    using (new EditorGUI.DisabledScope(!CanEditLevel))
                    {
                        var included = GUILayout.Toggle(!level.excludeFromOutput, "納入遊戲輸出", GUILayout.Width(125));
                        if (included == level.excludeFromOutput)
                        {
                            Undo.RecordObject(level, "變更關卡輸出選擇");
                            level.excludeFromOutput = !included;
                            EditorUtility.SetDirty(level);
                            AssetDatabase.SaveAssetIfDirty(level);
                            ReloadLevels();
                        }
                    }
                    GUILayout.Label("匯入關卡可直接 Build；無需另存正式關卡。", EditorStyles.miniLabel);
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("清空 Console", EditorStyles.toolbarButton, GUILayout.Width(100)))
                    ClearConsoleFromWorkbench();
                GUILayout.Label(consoleClearFeedback, EditorStyles.miniLabel, GUILayout.Width(220));
            }
            DrawImportInfo();
            if (level == null)
            {
                EditorGUILayout.HelpBox("目前沒有關卡。請匯入 ZIP 關卡包或建立草稿。", MessageType.Info);
                return;
            }
            if (level.venue == null || level.venue.background == null)
            {
                EditorGUILayout.HelpBox("請先執行 704 → 初始化新專案資料。", MessageType.Info);
                return;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                {
                    var oldGrid = showGrid;
                    using (new EditorGUI.DisabledScope(!CanEditLevel)) DrawTools();
                    if (oldGrid != showGrid) previewDirty = true;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var newView = GUILayout.Toolbar(previewView, ViewNames);
                        if (newView != previewView) { FinishCaveOrbit(); previewView = newView; previewDirty = true; }
                        if (previewView == 1 && GUILayout.Button("重設 3D 視角", GUILayout.Width(105))) ResetCaveOrbit();
                        var nextTrial = GUILayout.Toggle(trial, "試玩", GUILayout.Width(55));
                        if (nextTrial != trial) { trial = nextTrial; ResetPreview(); playing = trial; }
                    }
                    var available = GUILayoutUtility.GetRect(100, 10000, 260, 10000, GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));
                    DrawCanvas(available);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(playing ? "暫停路徑" : "播放路徑", GUILayout.Width(85))) { playing = !playing; lastUpdate = EditorApplication.timeSinceStartup; }
                        if (GUILayout.Button("重設預覽", GUILayout.Width(85))) ResetPreview();
                        using (new EditorGUI.DisabledScope(trial))
                        {
                            EditorGUI.BeginChangeCheck();
                            var newSeconds = EditorGUILayout.Slider((float)previewSeconds, 0, 30);
                            if (EditorGUI.EndChangeCheck()) { previewSeconds = newSeconds; previewDirty = true; }
                        }
                        GUILayout.Label($"{previewSeconds:0.00}s", GUILayout.Width(55));
                    }
                    if (GUILayout.Button("一鍵 Build EXE（已勾選輸出關卡）", GUILayout.Height(30))) PrototypeBuild.BuildCurrentLevelsFromWorkbench();
                    GUILayout.Label(previewView == 1 ? "CAVE 預覽｜按住右鍵拖曳環視；可在暫停時操作，按「重設 3D 視角」回到初始位置。" :
                        trial ? $"試玩：心數 {trialGame.Hearts}｜剩餘目標 {trialGame.Remaining}｜{trialGame.Outcome}｜左鍵觸摸藍格或點擊藍色 QR 掃碼；紫格長按。" : "不需 Play Mode｜左鍵操作；右鍵刪除｜黑區、入口與記分板不可放置｜20 cm² 仍須校正實體比例", EditorStyles.wordWrappedMiniLabel);
                }
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(320)))
                {
                    using (var inspectorScroll = new EditorGUILayout.ScrollViewScope(scroll))
                    {
                        scroll = inspectorScroll.scrollPosition;
                        DrawInspector();
                    }
                }
            }
        }

        void ClearConsoleFromWorkbench()
        {
            if (EditorConsoleUtility.TryClear(out var consoleError, out var remainingCount))
                consoleClearFeedback = remainingCount == 0 ? "已清空" : remainingCount > 0
                    ? $"仍有 {remainingCount} 筆（編譯錯誤會保留）" : "已執行清除";
            else
            {
                consoleClearFeedback = "清除失敗";
                EditorUtility.DisplayDialog("無法清空 Console", consoleError, "確定");
            }
            Repaint();
        }

        void DrawImportInfo()
        {
            if (level == null || string.IsNullOrWhiteSpace(level.importedPackageName)) return;
            var date = string.IsNullOrWhiteSpace(level.importedAtLocal) ? "未記錄" : level.importedAtLocal;
            var summary = string.IsNullOrWhiteSpace(level.importSummary) ? "沒有轉換摘要。" : level.importSummary;
            var currentWarnings = ImportWarningsForDisplay(level);
            var warnings = string.IsNullOrWhiteSpace(currentWarnings) ? "" : "\n注意事項：\n・" + currentWarnings.Replace("\n", "\n・");
            EditorGUILayout.HelpBox($"來源版本：{level.importedPackageName}\n匯入時間：{date}\n{summary}{warnings}",
                string.IsNullOrWhiteSpace(currentWarnings) ? MessageType.Info : MessageType.Warning);
        }

        internal static string ImportWarningsForDisplay(ScanLevel source)
        {
            if (source == null) return "";
            var warnings = (source.importWarnings ?? "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(warning => !(warning.Contains("QR") && warning.Contains("至少"))).ToList();
            foreach (var item in source.items.Where(item => item != null && item.IsScan))
                warnings.AddRange(LevelValidation.ContentErrors(item).Where(error => error.Contains("QR 區域")));
            return string.Join("\n", warnings);
        }

        void ImportLevelPackage()
        {
            var zipPath = EditorUtility.OpenFilePanel("匯入關卡包 ZIP", "", "zip");
            if (string.IsNullOrEmpty(zipPath)) return;
            var previousLevel = level;
            var venue = level != null && level.venue != null
                ? level.venue : AssetDatabase.LoadAssetAtPath<VenueLayout>(Root + "/Data/704_Venue.asset");
            var result = AsepriteZipImporter.Read(zipPath, venue);
            if (!result.Succeeded)
            {
                EditorUtility.DisplayDialog("關卡包匯入失敗", $"{Path.GetFileName(zipPath)}\n\n{result.Error}\n\n既有關卡沒有變更。", "確定");
                return;
            }

            string assetPath = null;
            try
            {
                EnsureDraftFolder();
                var asset = result.Level;
                asset.name = SafeAssetName(Path.GetFileNameWithoutExtension(zipPath));
                assetPath = AssetDatabase.GenerateUniqueAssetPath(DraftFolder + "/Imported_" + asset.name + ".asset");
                AssetDatabase.CreateAsset(asset, assetPath);
                AssetDatabase.SaveAssets();
                level = AssetDatabase.LoadAssetAtPath<ScanLevel>(assetPath);
                if (level == null) throw new InvalidOperationException("Unity 未能重新載入剛匯入的關卡資產。");
                selected = -1;
                trial = false;
                ReloadLevels();
                ResetPreview();
                var warningText = result.Warnings.Count == 0 ? "" : "\n\n注意事項：\n・" + string.Join("\n・", result.Warnings);
                EditorUtility.DisplayDialog("關卡包匯入完成", result.Summary + warningText, "確定");
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrEmpty(assetPath) && AssetDatabase.LoadAssetAtPath<ScanLevel>(assetPath) != null)
                    AssetDatabase.DeleteAsset(assetPath);
                else if (result.Level != null)
                    DestroyImmediate(result.Level);
                result.Level = null;
                level = previousLevel;
                EditorUtility.DisplayDialog("關卡包匯入失敗", $"{Path.GetFileName(zipPath)}\n\n{ex.Message}\n\n既有關卡沒有變更。", "確定");
                ReloadLevels();
                ResetPreview();
            }
        }

        static string SafeAssetName(string value)
        {
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            var safe = new string((value ?? "Imported").Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(safe) ? "ImportedLevel" : safe;
        }

        void DrawTools()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawToolButton(0, "選取", 54);
                DrawToolButton(1, "純筆刷", 64);
                DrawToolButton(2, "矩形", 54);
                DrawToolButton(3, "方形", 54);
                DrawToolButton(4, "圓形", 54);
                DrawToolButton(5, "新增路徑", 78);
                if (tool == 5 && GUILayout.Button("完成路徑（Enter）", GUILayout.Width(112))) FinishPathSelection();
                GUILayout.FlexibleSpace();
                showGrid = GUILayout.Toggle(showGrid, "格線", GUILayout.Width(55));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                for (var i = 0; i < TilePalette.Names.Length; i++)
                {
                    var selectedKind = (int)brushKind == i;
                    var old = GUI.backgroundColor;
                    GUI.backgroundColor = selectedKind ? new Color(.32f, .65f, 1f) : old;
                    if (GUILayout.Toggle(selectedKind, TilePalette.Names[i], "Button", GUILayout.MinWidth(90))) brushKind = (TileKind)i;
                    GUI.backgroundColor = old;
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("筆刷尺寸", GUILayout.Width(58));
                var shapeTool = tool >= 2 && tool <= 4;
                if (shapeTool) brushShape = ToolShape(tool);
                if (!shapeTool && brushShape == TileShape.Rectangle)
                {
                    var minimum = brushKind == TileKind.ScanBlue || brushKind == TileKind.SwitchCyan ? TileShapeUtility.MinimumScanSizeCells : 1;
                    brushSize.x = Mathf.Clamp(EditorGUILayout.IntField(brushSize.x, GUILayout.Width(42)), minimum, 52);
                    GUILayout.Label("×", GUILayout.Width(12));
                    brushSize.y = Mathf.Clamp(EditorGUILayout.IntField(brushSize.y, GUILayout.Width(42)), minimum, 42);
                }
                else if (!shapeTool)
                {
                    var minimum = brushKind == TileKind.ScanBlue || brushKind == TileKind.SwitchCyan
                        ? TileShapeUtility.MinimumScanSizeCells : 1;
                    var side = Mathf.Clamp(EditorGUILayout.IntField(brushSize.x, GUILayout.Width(42)), minimum, 42);
                    brushSize = new Vector2Int(side, side);
                }
                var mirrorX = GUILayout.Toggle(level.mirrorPainting, "左右鏡像", "Button", GUILayout.Width(80));
                var mirrorY = GUILayout.Toggle(level.mirrorAcrossHorizontalAxis, "上下鏡像", "Button", GUILayout.Width(80));
                var axisX = EditorGUILayout.FloatField(level.symmetryAxisX, GUILayout.Width(44));
                var axisY = EditorGUILayout.FloatField(level.symmetryAxisY, GUILayout.Width(44));
                if (mirrorX != level.mirrorPainting || mirrorY != level.mirrorAcrossHorizontalAxis ||
                    Mathf.Abs(axisX - level.symmetryAxisX) > .001f || Mathf.Abs(axisY - level.symmetryAxisY) > .001f)
                {
                    Undo.RecordObject(level, "修改鏡像工具設定");
                    level.mirrorPainting = mirrorX;
                    level.mirrorAcrossHorizontalAxis = mirrorY;
                    level.symmetryAxisX = Mathf.Clamp(Mathf.Round(axisX * 2f) * .5f, 0, level.venue.Width);
                    level.symmetryAxisY = Mathf.Clamp(Mathf.Round(axisY * 2f) * .5f, 0, level.venue.Height);
                    Changed();
                }
                GUILayout.Label("軸線 X／Y", EditorStyles.miniLabel, GUILayout.Width(56));
            }
            if (tool == 1 && brushShape == TileShape.Rectangle)
            {
                // The pure brush keeps a fixed rectangular footprint while painting a stroke.
            }
            brushGroup = EditorGUILayout.TextField("新增物件群組", brushGroup);
            EditorGUILayout.LabelField(tool == 0
                ? "選取物件後可按住拖曳；移動目標時會一併移動整條路徑。"
                : tool == 5 ? "點選移動物件後直接點路徑格；按「完成路徑」或 Enter 即可接著選另一個物件。"
                : tool == 1 ? "純筆刷可連續塗畫固定尺寸物件；矩形、方形與圓形請拖曳拉出整塊。"
                : "按住並拖曳拉出單一形狀，放開完成。", EditorStyles.wordWrappedMiniLabel);
        }

        void DrawToolButton(int value, string label, float width)
        {
            var old = GUI.backgroundColor;
            GUI.backgroundColor = tool == value ? new Color(.32f, .65f, 1f) : old;
            // A selected Toggle stays true during every GUI event; clear text focus only on a tool change.
            if (GUILayout.Toggle(tool == value, label, "Button", GUILayout.Width(width)) && tool != value)
            {
                if (tool != value && value == 5 && selected < 0) pathAwaitingItem = true;
                tool = value;
                if (value >= 2 && value <= 4) brushShape = ToolShape(value);
                else if (value == 1) brushShape = TileShape.Square;
                GUI.FocusControl(null);
            }
            GUI.backgroundColor = old;
        }

        static TileShape ToolShape(int value) => value == 2 ? TileShape.Rectangle : value == 3 ? TileShape.Square : TileShape.Circle;

        void DrawCanvas(Rect available)
        {
            EditorGUI.DrawRect(available, new Color(.09f, .10f, .12f));
            var venue = level.venue;
            if (renderer == null) renderer = new GameCanvasRenderer();
            if (Event.current.type == EventType.Repaint && previewDirty)
            {
                var textOrientation = previewView == 0 ? CanvasTextOrientation.FlatEditor : previewView == 1 ? CanvasTextOrientation.CavePreview : CanvasTextOrientation.VenueOutput;
                // Keep the initial gameplay state active during authoring previews. In
                // particular, inactive shutter groups must not render alongside the active state.
                renderer.Draw(level, previewSeconds, trialGame, codes, showGrid, -1, textOrientation);
                if (previewView == 1)
                {
                    if (cavePreview == null) cavePreview = new CaveRoom(renderer.Texture, Shader.Find("704/Cave Surface"));
                    cavePreview.SetTexture(renderer.Texture);
                    caveCameraDirty = true;
                }
                else if (previewView == 2)
                {
                    if (atlasMaterial == null) atlasMaterial = new Material(Shader.Find("704/Output Atlas"));
                    if (atlasPreview == null)
                    {
                        atlasPreview = new RenderTexture(2688,3840,0)
                            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                        atlasPreview.Create();
                    }
                    OutputCompositor.Blit(renderer.Texture, atlasPreview, atlasMaterial);
                }
                previewDirty = false;
            }
            // Repainting the GUI does not refresh a cached RenderTexture. Camera-only edits
            // render on the next Repaint without regenerating the gameplay canvas.
            if (Event.current.type == EventType.Repaint && previewView == 1 && caveCameraDirty && cavePreview != null)
            {
                cavePreview.Render();
                caveCameraDirty = false;
            }
            if (previewView != 0)
            {
                if (previewView == 1)
                {
                    if (cavePreview != null) GUI.DrawTexture(available, cavePreview.Image, ScaleMode.ScaleToFit);
                    HandleCaveOrbit(available);
                }
                else
                {
                    if (atlasPreview != null) GUI.DrawTexture(available, atlasPreview, ScaleMode.ScaleToFit);
                }
                return;
            }
            var scale = Mathf.Min((available.width - 30) / venue.Width, (available.height - 30) / venue.Height);
            if (scale <= 0) return;
            var canvas = new Rect(available.center.x - venue.Width * scale / 2, available.center.y - venue.Height * scale / 2, venue.Width * scale, venue.Height * scale);
            GUI.DrawTexture(canvas, renderer.Texture, ScaleMode.StretchToFill);
            var entry=VenueLayout.EntrancePixels;
            if(entranceLabel==null) entranceLabel=new GUIStyle(EditorStyles.whiteLabel){alignment=TextAnchor.MiddleCenter,wordWrap=true,fontSize=12};
            GUI.Label(new Rect(canvas.x+entry.x/(float)venue.pixelsPerCell*scale,canvas.y+entry.y/(float)venue.pixelsPerCell*scale,
                entry.width/(float)venue.pixelsPerCell*scale,entry.height/(float)venue.pixelsPerCell*scale),"入口\n禁止放置／互動",entranceLabel);
            var scoreboard=VenueLayout.ScoreboardPixels;
            var scoreboardRect=new Rect(canvas.x+scoreboard.x/(float)venue.pixelsPerCell*scale,canvas.y+scoreboard.y/(float)venue.pixelsPerCell*scale,
                scoreboard.width/(float)venue.pixelsPerCell*scale,scoreboard.height/(float)venue.pixelsPerCell*scale);
            EditorGUI.DrawRect(scoreboardRect,new Color(.72f,.08f,.10f,.76f));
            Handles.BeginGUI(); Handles.color=new Color(1f,.28f,.24f,.98f);
            Handles.DrawAAPolyLine(3,new Vector3(scoreboardRect.x,scoreboardRect.y),new Vector3(scoreboardRect.xMax,scoreboardRect.y),
                new Vector3(scoreboardRect.xMax,scoreboardRect.yMax),new Vector3(scoreboardRect.x,scoreboardRect.yMax),new Vector3(scoreboardRect.x,scoreboardRect.y));
            Handles.EndGUI();
            if(scoreboardLabel==null) scoreboardLabel=new GUIStyle(EditorStyles.whiteBoldLabel){alignment=TextAnchor.MiddleCenter,wordWrap=true,fontSize=11};
            GUI.Label(scoreboardRect,"記分板\n東小牆整面\n禁止放置",scoreboardLabel);
            if (showGrid)
            {
                Handles.BeginGUI(); Handles.color = new Color(0, 0, 0, .17f);
                for (var x = 0; x <= venue.Width; x++) Handles.DrawLine(new Vector3(canvas.x + x * scale, canvas.y), new Vector3(canvas.x + x * scale, canvas.yMax));
                for (var y = 0; y <= venue.Height; y++) Handles.DrawLine(new Vector3(canvas.x, canvas.y + y * scale), new Vector3(canvas.xMax, canvas.y + y * scale));
                Handles.EndGUI();
            }
            for (var i = 0; i < level.items.Count; i++)
            {
                var item = level.items[i];
                if (trial && trialGame != null && trialGame.States[item.id].Completed) continue;
                var p = item.PositionAt(previewSeconds, level.movementSegmentMs);
                var r = new Rect(canvas.x + p.x * scale, canvas.y + p.y * scale, item.size.x * scale, item.size.y * scale);
                if (i == selected)
                {
                    Handles.BeginGUI(); Handles.color = Color.yellow;
                    if (item.shape == TileShape.Circle)
                        Handles.DrawWireDisc(r.center, Vector3.forward, Mathf.Min(r.width, r.height) * .5f);
                    else Handles.DrawAAPolyLine(2, new Vector3(r.x, r.y), new Vector3(r.xMax, r.y), new Vector3(r.xMax, r.yMax), new Vector3(r.x, r.yMax), new Vector3(r.x, r.y));
                    if (item.CanMove && item.waypoints.Count > 0)
                    {
                        var points = new List<Vector3> { Center(canvas, scale, item.position, item.size) };
                        points.AddRange(item.waypoints.Select(point => Center(canvas, scale, point, item.size)));
                        points.Add(points[0]); Handles.DrawAAPolyLine(2, points.ToArray());
                    }
                    Handles.EndGUI();
                }
            }
            DrawSymmetryAxis(canvas, scale);
            DrawPlacementGhost(canvas, scale);
            itemDragControlId = GUIUtility.GetControlID(0x704704, FocusType.Passive, canvas);
            HandleSymmetryAxis(canvas, scale);
            HandleCanvasInput(canvas, scale, itemDragControlId);
        }
        static Vector3 Center(Rect canvas, float scale, Vector2 position, Vector2 size) => new Vector3(canvas.x + (position.x + size.x / 2) * scale, canvas.y + (position.y + size.y / 2) * scale);
        static Vector2Int BrushPositionCenteredAt(Vector2 cursor, Vector2Int size) => new Vector2Int(
            Mathf.FloorToInt(cursor.x - size.x * .5f + .5f),
            Mathf.FloorToInt(cursor.y - size.y * .5f + .5f));

        void DrawSymmetryAxis(Rect canvas, float scale)
        {
            if (!level.mirrorPainting && !level.mirrorAcrossHorizontalAxis) return;
            Handles.BeginGUI();
            Handles.color = new Color(1f, .82f, .18f, .95f);
            if (level.mirrorPainting)
            {
                var x = canvas.x + level.symmetryAxisX * scale;
                Handles.DrawDottedLine(new Vector3(x, canvas.y), new Vector3(x, canvas.yMax), 4f);
                Handles.DrawSolidDisc(new Vector3(x, canvas.y + 7), Vector3.forward, 6f);
            }
            if (level.mirrorAcrossHorizontalAxis)
            {
                var y = canvas.y + level.symmetryAxisY * scale;
                Handles.DrawDottedLine(new Vector3(canvas.x, y), new Vector3(canvas.xMax, y), 4f);
                Handles.DrawSolidDisc(new Vector3(canvas.x + 7, y), Vector3.forward, 6f);
            }
            Handles.EndGUI();
        }

        void HandleSymmetryAxis(Rect canvas, float scale)
        {
            var e = Event.current;
            if (e.type == EventType.MouseUp && draggingSymmetryAxis)
            {
                FinishSymmetryAxis();
                e.Use(); return;
            }
            if (!CanEditLevel || trial || previewView != 0 || (!level.mirrorPainting && !level.mirrorAcrossHorizontalAxis)) return;
            var axisPixel = canvas.x + level.symmetryAxisX * scale;
            var axisY = canvas.y + level.symmetryAxisY * scale;
            if (e.type == EventType.MouseDown && e.button == 0 && e.mousePosition.y >= canvas.y &&
                e.mousePosition.y <= canvas.yMax &&
                ((level.mirrorPainting && Mathf.Abs(e.mousePosition.x - axisPixel) <= 8f) ||
                 (level.mirrorAcrossHorizontalAxis && Mathf.Abs(e.mousePosition.y - axisY) <= 8f)))
            {
                Undo.IncrementCurrentGroup();
                symmetryUndoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("調整鏡像中心");
                Undo.RegisterCompleteObjectUndo(level, "調整鏡像中心");
                draggingSymmetryAxis = true;
                symmetryAxisControlId = GUIUtility.GetControlID(0x704705, FocusType.Passive);
                GUIUtility.hotControl = symmetryAxisControlId;
                e.Use(); return;
            }
            if (draggingSymmetryAxis && e.type == EventType.MouseDrag && GUIUtility.hotControl == symmetryAxisControlId)
            {
                if (level.mirrorPainting && Mathf.Abs(e.mousePosition.x - axisPixel) <= 16f)
                {
                    var x = Mathf.Clamp(Mathf.Round(((e.mousePosition.x - canvas.x) / scale) * 2f) * .5f, 0, level.venue.Width);
                    if (!Mathf.Approximately(x, level.symmetryAxisX)) level.symmetryAxisX = x;
                }
                if (level.mirrorAcrossHorizontalAxis && Mathf.Abs(e.mousePosition.y - axisY) <= 16f)
                {
                    var y = Mathf.Clamp(Mathf.Round(((e.mousePosition.y - canvas.y) / scale) * 2f) * .5f, 0, level.venue.Height);
                    if (!Mathf.Approximately(y, level.symmetryAxisY)) level.symmetryAxisY = y;
                }
                EditorUtility.SetDirty(level); observedLevelDirty = EditorUtility.GetDirtyCount(level);
                previewDirty = true; Repaint();
                e.Use();
            }
        }

        void FinishSymmetryAxis()
        {
            if (symmetryUndoGroup >= 0) Undo.CollapseUndoOperations(symmetryUndoGroup);
            if (symmetryAxisControlId != 0 && GUIUtility.hotControl == symmetryAxisControlId) GUIUtility.hotControl = 0;
            draggingSymmetryAxis = false; symmetryUndoGroup = -1; symmetryAxisControlId = 0;
        }

        void DrawPlacementGhost(Rect canvas, float scale)
        {
            var e = Event.current;
            if (!CanEditLevel || trial || previewView != 0 || (tool != 1 && (tool < 2 || tool > 4)) ||
                (!canvas.Contains(e.mousePosition) && !draggingShape)) return;
            var shape = tool >= 2 && tool <= 4 ? ToolShape(tool) : brushShape;
            var bounds = tool >= 2 && tool <= 4
                ? draggingShape ? ShapeDragBounds() : ((Vector2Int)ClampCell(Vector2Int.FloorToInt((e.mousePosition - canvas.position) / scale)), Vector2Int.one)
                : (BrushPositionCenteredAt((e.mousePosition - canvas.position) / scale, brushSize), brushSize);
            var item = new LevelItem { kind = brushKind, position = bounds.Item1, size = bounds.Item2, shape = shape };
            var valid = PlacementRejection(item) == null;
            foreach (var variant in SymmetryVariants(item)) valid &= PlacementRejection(variant) == null;
            WorkbenchShapeTool.DrawGhost(canvas, scale, item.position, item.size, shape, valid);
            foreach (var variant in SymmetryVariants(item)) WorkbenchShapeTool.DrawGhost(canvas, scale, variant.position, variant.size, shape, valid);
        }

        void HandleCaveOrbit(Rect area)
        {
            var e = Event.current;
            var controlId = GUIUtility.GetControlID(0x704706, FocusType.Passive, area);
            if (e.type == EventType.MouseUp && e.button == 1 && orbitingCave)
            {
                FinishCaveOrbit(); e.Use(); return;
            }
            if (e.type == EventType.MouseDown && e.button == 1 && area.Contains(e.mousePosition))
            {
                orbitingCave = true;
                caveOrbitControlId = controlId;
                GUIUtility.hotControl = caveOrbitControlId; e.Use(); return;
            }
            if (orbitingCave && e.type == EventType.MouseDrag && GUIUtility.hotControl == caveOrbitControlId)
            {
                cavePreview?.Orbit(e.delta.x, e.delta.y);
                caveCameraDirty = true;
                e.Use(); Repaint();
            }
        }

        void ResetCaveOrbit()
        {
            cavePreview?.ResetOrbit();
            caveCameraDirty = true;
            Repaint();
        }

        void FinishCaveOrbit()
        {
            if (caveOrbitControlId != 0 && GUIUtility.hotControl == caveOrbitControlId) GUIUtility.hotControl = 0;
            orbitingCave = false;
            caveOrbitControlId = 0;
        }

        void OnLostFocus() => FinishCaveOrbit();

        void FinishPathSelection()
        {
            if (tool != 5) return;
            selected = -1; pathAwaitingItem = true; previewDirty = true; Repaint();
        }

        void FinishShapeDrag()
        {
            if (!draggingShape) return;
            draggingShape = false;
            if (GUIUtility.hotControl == shapeDragControlId) GUIUtility.hotControl = 0;
            shapeDragControlId = 0;
            var bounds = ShapeDragBounds();
            if (bounds.Item2.x > 0 && bounds.Item2.y > 0) PlaceItem(bounds.Item1, bounds.Item2, ToolShape(tool));
            Repaint();
        }

        (Vector2Int, Vector2Int) ShapeDragBounds()
            => WorkbenchShapeTool.DragBounds(shapeDragStart, shapeDragEnd, ToolShape(tool));

        List<LevelItem> SymmetryVariants(LevelItem source)
            => WorkbenchMirrorUtility.Variants(source, level.mirrorPainting, level.mirrorAcrossHorizontalAxis,
                level.symmetryAxisX, level.symmetryAxisY);

        string PlacementRejection(LevelItem item)
        {
            if (item.IsScan && !TileShapeUtility.HasMinimumScanBounds(item.size))
                return $"QR 至少 {TileShapeUtility.MinimumScanSizeCells}×{TileShapeUtility.MinimumScanSizeCells} 格；本次放置未執行。";
            if (item.kind == TileKind.SafeGreen && !item.allowSafeGreenOutsideFloor && !level.venue.PathIsOnFloor(item))
                return LevelPlacementGuard.SafeGreenFloorRejection;
            if (level.venue.TouchesEntrance(item)) return LevelPlacementGuard.Rejection;
            if (level.venue.TouchesScoreboard(item)) return LevelPlacementGuard.ScoreboardRejection;
            if (!level.venue.PathIsAllowed(item)) return LevelPlacementGuard.BlackAreaRejection;
            return null;
        }

        bool SamePlacementExists(LevelItem item) => level.items.Any(i =>
            i.kind == item.kind && i.position == item.position && i.size == item.size && i.shape == item.shape);

        internal static int FindMirroredDeleteIndex(IReadOnlyList<LevelItem> items, int sourceIndex, float axisX)
            => WorkbenchMirrorUtility.FindMirrorIndex(items, sourceIndex, axisX);

        void PlaceBrush(Vector2Int position) => PlaceItem(position, brushSize, brushShape);

        void PlaceItem(Vector2Int position, Vector2Int size, TileShape shape)
        {
            var first = new LevelItem { kind = brushKind, shape = shape, position = position, size = size, group = brushGroup };
            var variants = SymmetryVariants(first);
            placementMessage = PlacementRejection(first) ?? variants.Select(PlacementRejection).FirstOrDefault(message => message != null);
            if (placementMessage != null) { Repaint(); return; }
            if (SamePlacementExists(first) || variants.Any(SamePlacementExists))
            { placementMessage = "其中一個鏡像位置已有相同物件，本次放置未執行。"; Repaint(); return; }

            var generated = level.newTouchContentMode == TouchContentMode.Automatic && brushKind == TileKind.TouchBlue;
            if (generated)
            {
                var used = new List<int>();
                if (!TouchNumberContent.TryAssignGenerated(level, first, used, out placementMessage)) { Repaint(); return; }
                used.Add(first.generatedAnswer);
                foreach (var variant in variants)
                {
                    if (!TouchNumberContent.TryAssignGenerated(level, variant, used, out placementMessage)) { Repaint(); return; }
                    used.Add(variant.generatedAnswer);
                }
            }
            Undo.RecordObject(level, variants.Count > 0 ? "鏡像放置物件" : "放置物件");
            level.items.Add(first);
            selected = level.items.Count - 1;
            level.items.AddRange(variants);
            placementMessage = null; Changed();
        }

        void MirrorSelectedItem()
        {
            if (!CanEditLevel || selected < 0 || selected >= level.items.Count) return;
            var source = level.items[selected];
            var variants = SymmetryVariants(source);
            if (variants.Count == 0) { placementMessage = "請先開啟左右或上下鏡像。"; Repaint(); return; }
            placementMessage = variants.Select(PlacementRejection).FirstOrDefault(message => message != null);
            if (placementMessage != null) { Repaint(); return; }
            if (variants.Any(SamePlacementExists))
            { placementMessage = "鏡射位置已有相同物件，未建立重複物件。"; Repaint(); return; }
            var usedAnswers = new List<int>();
            foreach (var copy in variants)
                if (copy.kind == TileKind.TouchBlue && copy.useGeneratedContent &&
                    !TouchNumberContent.TryAssignGenerated(level, copy, usedAnswers, out placementMessage))
                { Repaint(); return; }
                else if (copy.kind == TileKind.TouchBlue && copy.useGeneratedContent) usedAnswers.Add(copy.generatedAnswer);
            Undo.RecordObject(level, "鏡射複製物件");
            level.items.AddRange(variants); selected = level.items.Count - 1;
            placementMessage = null; Changed();
        }

        void ApplyAutomaticContentToBlankItems()
        {
            if (!CanEditLevel || level.newTouchContentMode != TouchContentMode.Automatic) return;
            var blankIndices = Enumerable.Range(0, level.items.Count)
                .Where(i => level.items[i].kind == TileKind.TouchBlue && !level.items[i].useGeneratedContent &&
                    string.IsNullOrWhiteSpace(level.items[i].label)).ToArray();
            if (blankIndices.Length == 0) { placementMessage = "沒有空白的藍色觸摸格需要填入。"; Repaint(); return; }
            var temporary = Instantiate(level);
            var reservations = new List<int>();
            var generated = new List<(string id, int answer, string text)>();
            try
            {
                foreach (var index in blankIndices)
                {
                    var item = temporary.items[index];
                    if (!TouchNumberContent.TryAssignGenerated(temporary, item, reservations, out placementMessage))
                    { Repaint(); return; }
                    reservations.Add(item.generatedAnswer);
                    generated.Add((item.id, item.generatedAnswer, item.generatedText));
                }
            }
            finally { DestroyImmediate(temporary); }

            Undo.RecordObject(level, "填入空白觸摸數字");
            foreach (var result in generated)
            {
                var item = level.items.First(i => i.id == result.id);
                item.useGeneratedContent = true; item.generatedAnswer = result.answer; item.generatedText = result.text;
            }
            placementMessage = null; Changed();
        }

        void RegenerateSelectedAnswer(LevelItem item)
        {
            Undo.RecordObject(level, "重新分配答案");
            if (TouchNumberContent.TryAssignGenerated(level, item, null, out placementMessage)) Changed();
            else Repaint();
        }

        void RegenerateSelectedExpression(LevelItem item)
        {
            if (!TouchNumberContent.TryMakeArithmetic(item.generatedAnswer, level.arithmeticOperandMinimum,
                level.arithmeticOperandMaximum, TouchNumberContent.ContentFootprint(item), new System.Random(Environment.TickCount),
                out var expression, item.HasAnimation ? TileShape.Rectangle : item.shape))
            { placementMessage = $"答案 {item.generatedAnswer} 在指定運算元範圍內找不到可放入此藍格的加減算式。"; Repaint(); return; }
            Undo.RecordObject(level, "只更換算式");
            item.generatedText = expression; placementMessage = null; Changed();
        }

        void ReapplyAutomaticContentToAllItems()
        {
            if (!CanEditLevel || level.newTouchContentMode != TouchContentMode.Automatic) return;
            if (level.proMechanic != ProMechanic.None)
            { placementMessage = "請先將舊版 Pro 機制設為「無」，再重新套用自動數字／算式。"; Repaint(); return; }

            var targets = level.items.Where(item => item.kind == TileKind.TouchBlue).ToArray();
            if (targets.Length == 0) { placementMessage = "目前沒有藍色觸摸格可套用。"; Repaint(); return; }

            var temporary = Instantiate(level);
            var generated = new List<(string id, int answer, string text)>();
            try
            {
                foreach (var item in temporary.items.Where(item => item.kind == TileKind.TouchBlue))
                {
                    item.label = string.Empty;
                    item.useGeneratedContent = false;
                    item.generatedText = string.Empty;
                }

                var reservations = new List<int>();
                foreach (var item in temporary.items.Where(item => item.kind == TileKind.TouchBlue))
                {
                    if (!TouchNumberContent.TryAssignGenerated(temporary, item, reservations, out placementMessage))
                    { Repaint(); return; }
                    reservations.Add(item.generatedAnswer);
                    generated.Add((item.id, item.generatedAnswer, item.generatedText));
                }
            }
            finally { DestroyImmediate(temporary); }

            Undo.RecordObject(level, "重新套用全部觸摸數字／算式");
            foreach (var result in generated)
            {
                var item = level.items.First(candidate => candidate.id == result.id);
                item.label = string.Empty;
                item.useGeneratedContent = true;
                item.generatedAnswer = result.answer;
                item.generatedText = result.text;
            }
            placementMessage = null;
            Changed();
        }

        internal static bool ShouldFinishPathSelection(Event current, bool editingTextField) =>
            !editingTextField && current != null && current.type == EventType.KeyDown &&
            (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter);

        void HandleCanvasInput(Rect canvas, float scale, int dragControlId)
        {
            var e = Event.current;
            if (e.type == EventType.MouseUp)
            {
                lastPaintCell = new Vector2Int(-1, -1); mouseHeld = false; FinishItemDrag();
                if (GUIUtility.hotControl == dragControlId) GUIUtility.hotControl = 0;
                return;
            }
            if (trial)
            {
                if (canvas.Contains(e.mousePosition)) trialMouse = (e.mousePosition - canvas.position) / scale;
                if (e.type == EventType.MouseDown && e.button == 0 && canvas.Contains(e.mousePosition))
                {
                    mouseHeld = false;
                    if (playing && trialGame != null && trialGame.TryScanAt(trialMouse, "A",
                        EditorApplication.timeSinceStartup * 1000, out _))
                    {
                        previewDirty = true;
                        Repaint();
                    }
                    else if (playing) mouseHeld = true;
                    e.Use();
                }
                return;
            }
            if (e.type != EventType.MouseDown && e.type != EventType.MouseDrag) return;
            if ((!canvas.Contains(e.mousePosition) && !draggingItem && !draggingShape) || level == null || level.venue == null) return;
            var cursor = (e.mousePosition - canvas.position) / scale;
            var cell = Vector2Int.FloorToInt(cursor);
            if (draggingShape && e.type == EventType.MouseDrag)
            {
                shapeDragEnd = ClampCell(cell); previewDirty = true; Repaint(); e.Use(); return;
            }
            if (draggingItem && e.type == EventType.MouseDrag)
            {
                UpdateItemDrag(cursor); e.Use(); return;
            }
            var hit = -1;
            if (canvas.Contains(e.mousePosition))
            {
                var renderOrder = TileLayerOrder.OrderedItems(level.items, level.layerOrder);
                for (var i = renderOrder.Count - 1; i >= 0; i--)
                {
                    var item = renderOrder[i];
                    if (TileShapeUtility.Contains(item,
                        item.PositionAt(previewSeconds, level.movementSegmentMs), cursor,
                        item.AnimationFrameIndex(previewSeconds, level.animationSpeedMilliseconds)))
                    { hit = level.items.IndexOf(item); break; }
                }
            }
            if (!CanEditLevel)
            {
                if (e.type == EventType.MouseDown && e.button == 0) { selected = hit; Repaint(); e.Use(); }
                return;
            }
            if (e.type == EventType.MouseDown && e.button == 1 && hit >= 0)
            {
                var deleteIndices = SymmetricDeleteIndices(hit);
                Undo.RecordObject(level, deleteIndices.Count > 1 ? "鏡像刪除物件" : "刪除物件");
                foreach (var index in deleteIndices.OrderByDescending(i => i)) level.items.RemoveAt(index);
                selected = -1; Changed(); e.Use(); return;
            }
            if (e.button != 0) return;
            if (tool == 0)
            {
                if (e.type == EventType.MouseDown)
                {
                    selected = hit;
                    BeginItemDrag(hit, cursor);
                    if (draggingItem) GUIUtility.hotControl = dragControlId;
                    Repaint(); e.Use();
                }
                else if (draggingItem) { UpdateItemDrag(cursor); e.Use(); }
                return;
            }
            if (!canvas.Contains(e.mousePosition)) return;
            if (tool == 1)
            {
                var placement = BrushPositionCenteredAt(cursor, brushSize);
                if (placement != lastPaintCell)
                {
                    lastPaintCell = placement;
                    PlaceBrush(placement);
                }
                e.Use();
            }
            if (tool >= 2 && tool <= 4)
            {
                if (e.type == EventType.MouseDown)
                {
                    shapeDragStart = shapeDragEnd = ClampCell(cell); draggingShape = true;
                    shapeDragControlId = GUIUtility.GetControlID(0x704707, FocusType.Passive);
                    GUIUtility.hotControl = shapeDragControlId; e.Use(); Repaint();
                }
                else if (draggingShape) { shapeDragEnd = ClampCell(cell); e.Use(); Repaint(); }
                return;
            }
            if (tool == 5 && e.type == EventType.MouseDown)
            {
                if ((pathAwaitingItem || selected < 0) && hit >= 0)
                {
                    var target = level.items[hit];
                    if (target.CanMove) { selected = hit; pathAwaitingItem = false; placementMessage = null; Repaint(); }
                    e.Use(); return;
                }
                if (selected < 0 || selected >= level.items.Count) { e.Use(); return; }
                var item = level.items[selected];
                if (item.CanMove)
                {
                    var candidate=new LevelItem {kind=item.kind,position=item.position,size=item.size};candidate.waypoints.AddRange(item.waypoints);candidate.waypoints.Add(cell);
                    var rejection=PlacementRejection(candidate);
                    if(rejection!=null)
                    {placementMessage=rejection;Repaint();}
                    else
                    {
                        Undo.RecordObject(level, "新增路徑點");
                        item.waypoints.Add(cell);
                        if (item.waypointHoldSeconds == null) item.waypointHoldSeconds = new List<float>();
                        item.waypointHoldSeconds.Add(0);
                        Changed();
                    }
                }
                e.Use();
            }
        }

        Vector2Int ClampCell(Vector2Int cell) => new Vector2Int(
            Mathf.Clamp(cell.x, 0, level.venue.Width - 1), Mathf.Clamp(cell.y, 0, level.venue.Height - 1));

        List<int> SymmetricDeleteIndices(int sourceIndex)
        {
            var result = new HashSet<int> { sourceIndex };
            var source = level.items[sourceIndex];
            foreach (var variant in SymmetryVariants(source))
                for (var i = level.items.Count - 1; i >= 0; i--)
                {
                    var candidate = level.items[i];
                    if (i != sourceIndex && candidate.position == variant.position && candidate.size == source.size && candidate.shape == source.shape)
                    { result.Add(i); break; }
                }
            return result.ToList();
        }

        void BeginItemDrag(int index, Vector2 cursor)
        {
            FinishItemDrag();
            if (index < 0 || index >= level.items.Count) return;
            var item = level.items[index];
            draggingItem = true;
            draggingItemIndex = index;
            dragStartPosition = item.position;
            dragStartWaypoints = item.waypoints.ToArray();
            dragVisualStart = item.PositionAt(previewSeconds, level.movementSegmentMs);
            dragGrabOffset = cursor - dragVisualStart;
            playing = false;
        }

        void UpdateItemDrag(Vector2 cursor)
        {
            if (draggingItemIndex < 0 || draggingItemIndex >= level.items.Count) return;
            var item = level.items[draggingItemIndex];
            var delta = Vector2Int.RoundToInt(cursor - dragGrabOffset - dragVisualStart);
            var candidate = new LevelItem { kind = item.kind, position = dragStartPosition + delta, size = item.size };
            candidate.waypoints.AddRange(dragStartWaypoints.Select(point => point + delta));
            var rejection = PlacementRejection(candidate);
            if (rejection != null)
            {
                placementMessage = rejection;
                Repaint(); return;
            }
            if (candidate.position == item.position && candidate.waypoints.SequenceEqual(item.waypoints)) return;
            if (!dragUndoCreated)
            {
                Undo.IncrementCurrentGroup();
                dragUndoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("移動物件");
                Undo.RegisterCompleteObjectUndo(level, "移動物件");
                dragUndoCreated = true;
            }
            item.position = candidate.position;
            item.waypoints.Clear(); item.waypoints.AddRange(candidate.waypoints);
            placementMessage = null;
            EditorUtility.SetDirty(level);
            observedLevelDirty = EditorUtility.GetDirtyCount(level); observedVenue = level.venue; observedMaskRevision = level.venue.MaskRevision;
            previewDirty = true; Repaint();
        }

        void FinishItemDrag()
        {
            if (dragUndoCreated) Undo.CollapseUndoOperations(dragUndoGroup);
            if (itemDragControlId != 0 && GUIUtility.hotControl == itemDragControlId) GUIUtility.hotControl = 0;
            draggingItem = false; dragUndoCreated = false;
            draggingItemIndex = -1; dragUndoGroup = -1;
            dragStartWaypoints = Array.Empty<Vector2Int>();
        }

        void Changed() { EditorUtility.SetDirty(level); ResetPreview(); Repaint(); }
        void DrawInspector()
        {
            GUILayout.Label("關卡設定", EditorStyles.boldLabel);
            var serialized = new SerializedObject(level);
            serialized.Update();
            using (new EditorGUI.DisabledScope(!CanEditLevel))
            {
                EditorGUILayout.PropertyField(serialized.FindProperty("displayName"), new GUIContent("名稱"));
                EditorGUILayout.PropertyField(serialized.FindProperty("timeLimitSeconds"), new GUIContent("限時（秒）"));
                EditorGUILayout.PropertyField(serialized.FindProperty("initialHearts"), new GUIContent("初始心數"));
                EditorGUILayout.PropertyField(serialized.FindProperty("movementSegmentMs"), new GUIContent("每段移動時間（ms）"));
                EditorGUILayout.PropertyField(serialized.FindProperty("timingFramesPerSecond"), new GUIContent("秒／幀換算 FPS"));
                EditorGUILayout.PropertyField(serialized.FindProperty("proMechanic"), new GUIContent("Pro 機制"));
                EditorGUILayout.PropertyField(serialized.FindProperty("purpleHoldSeconds"), new GUIContent("紫格長按（秒）"));
                EditorGUILayout.PropertyField(serialized.FindProperty("switchQuietIntervalMs"), new GUIContent("淺藍重啟間隔（ms）"));
            }
            ApplyInspector(serialized);
            DrawColorSettings();
            DrawAuthoringSettings();
            GUILayout.Space(8);
            GUILayout.Label($"物件  {level.items.Count}", EditorStyles.boldLabel);
            if (selected >= 0 && selected < level.items.Count)
            {
                serialized.Update();
                var item = level.items[selected];
                var property = serialized.FindProperty("items").GetArrayElementAtIndex(selected);
                EditorGUILayout.LabelField(TilePalette.Names[(int)item.kind]);
                if (level.mirrorPainting &&
                    (item.kind == TileKind.HazardRed || item.kind == TileKind.HoldPurple || item.kind == TileKind.SwitchCyan))
                    EditorGUILayout.HelpBox("鏡射時會複製目前群組設定；兩側沿用相同群組名稱時會共同作用。", MessageType.Info);
                using (new EditorGUI.DisabledScope(!CanEditLevel))
                {
                    EditorGUILayout.PropertyField(property.FindPropertyRelative("position"), new GUIContent("左上格座標"));
                    var hasFrameMasks = item.animationFrames != null && item.animationFrames.Count > 0;
                    var shapeProperty = property.FindPropertyRelative("shape");
                    var sizeProperty = property.FindPropertyRelative("size");
                    using (new EditorGUI.DisabledScope(hasFrameMasks))
                    {
                        EditorGUILayout.PropertyField(shapeProperty, new GUIContent("形狀"));
                        var itemShape = (TileShape)shapeProperty.enumValueIndex;
                        if (itemShape == TileShape.Rectangle)
                        {
                            EditorGUILayout.PropertyField(sizeProperty, new GUIContent("格數大小"));
                            if (item.IsScan)
                            {
                                var scanSize = sizeProperty.vector2IntValue;
                                sizeProperty.vector2IntValue = new Vector2Int(
                                    Mathf.Max(TileShapeUtility.MinimumScanSizeCells, scanSize.x),
                                    Mathf.Max(TileShapeUtility.MinimumScanSizeCells, scanSize.y));
                            }
                        }
                        else
                        {
                            var size = sizeProperty.vector2IntValue;
                            var minimum = item.IsScan ? TileShapeUtility.MinimumScanSizeCells : 1;
                            var maximum = Mathf.Min(level.venue.Width, level.venue.Height);
                            var dimension = Mathf.Clamp(itemShape == TileShape.Circle ? Mathf.Min(size.x, size.y) : size.x, minimum, maximum);
                            dimension = Mathf.Clamp(EditorGUILayout.IntField(itemShape == TileShape.Circle ? "圓形直徑（格）" : "方形邊長（格）", dimension), minimum, maximum);
                            sizeProperty.vector2IntValue = new Vector2Int(dimension, dimension);
                        }
                    }
                    if (hasFrameMasks) EditorGUILayout.HelpBox("此物件由匯入影格遮罩定義範圍；可調整位置與內容，遮罩尺寸依原圖保留。", MessageType.Info);
                    if (item.kind == TileKind.TouchBlue)
                    {
                        var nextGenerated = EditorGUILayout.Toggle("自動數字內容", item.useGeneratedContent);
                        if (nextGenerated != item.useGeneratedContent)
                        {
                            if (nextGenerated)
                            {
                                Undo.RecordObject(level, "產生觸摸數字");
                                if (!TouchNumberContent.TryAssignGenerated(level, item, null, out placementMessage)) Repaint();
                                else Changed();
                            }
                            else
                            {
                                Undo.RecordObject(level, "切回手動內容");
                                if (string.IsNullOrWhiteSpace(item.label) && item.useGeneratedContent)
                                    item.label = TouchNumberContent.DisplayText(item);
                                item.useGeneratedContent = false; placementMessage = null; Changed();
                            }
                        }
                        if (item.useGeneratedContent)
                        {
                            EditorGUILayout.LabelField("顯示內容", TouchNumberContent.DisplayText(item));
                            EditorGUILayout.LabelField("計算結果", item.generatedAnswer.ToString());
                            if (level.autoTouchStyle == AutoTouchContentStyle.Arithmetic &&
                                GUILayout.Button("只換算式（答案不變）")) RegenerateSelectedExpression(item);
                            if (GUILayout.Button("重新分配答案")) RegenerateSelectedAnswer(item);
                        }
                        else EditorGUILayout.PropertyField(property.FindPropertyRelative("label"), new GUIContent("手動顯示文字"));
                    }
                    else EditorGUILayout.PropertyField(property.FindPropertyRelative("label"), new GUIContent("顯示文字"));
                    EditorGUILayout.PropertyField(property.FindPropertyRelative("group"), new GUIContent("群組"));
                    EditorGUILayout.PropertyField(property.FindPropertyRelative("proTarget"), new GUIContent("參與 Pro 題目"));
                    if (item.kind == TileKind.HoldPurple || item.kind == TileKind.SwitchCyan)
                        EditorGUILayout.PropertyField(property.FindPropertyRelative("targetGroups"), new GUIContent("控制群組"), true);
                    if (item.CanMove) PathTimingEditor.Draw(level, serialized, property, item);
                }
                ApplyInspector(serialized);
                if (GUILayout.Button("鏡射複製到另一側")) MirrorSelectedItem();
                if (item.kind == TileKind.SafeGreen && !item.allowSafeGreenOutsideFloor && !level.venue.PathIsOnFloor(item))
                    EditorGUILayout.HelpBox(LevelPlacementGuard.SafeGreenFloorRejection, MessageType.Error);
                foreach (var contentError in LevelValidation.ContentErrors(item))
                    EditorGUILayout.HelpBox(contentError, MessageType.Warning);
                if (!level.venue.PathIsAllowed(item)) EditorGUILayout.HelpBox("物件位置或完整循環路徑穿過黑區、入口或記分板保留區，請修正。", MessageType.Error);
                if (item.IsScan)
                {
                    if (!level.venue.HasPhysicalScale) EditorGUILayout.HelpBox("條碼至少 20 cm²。實際公分比例尚未校正，不能用像素代替面積驗收。", MessageType.Warning);
                    else if (TileShapeUtility.BarcodeAreaCm2(level.venue, item) < VenueLayout.MinimumBarcodeAreaCm2) EditorGUILayout.HelpBox("目前 QR 圖樣含淨空的估算面積不足 20 cm²。", MessageType.Error);
                }
            }
            else EditorGUILayout.HelpBox("選取物件可修改位置、大小、群組及路徑。", MessageType.None);
            if(!string.IsNullOrEmpty(placementMessage)) EditorGUILayout.HelpBox(placementMessage,MessageType.Warning);
            GUILayout.Space(10);
            GUILayout.Label("場域輸出", EditorStyles.boldLabel);
            if (GUILayout.Button("匯出場域 PNG（2688×3840）")) WorkbenchExportService.ExportAtlas(level, previewSeconds, codes);
            if (GUILayout.Button("開啟試玩場景")) { if (CanEditLevel) AssetDatabase.SaveAssetIfDirty(level); PrototypeBuild.OpenCurrentOutputScene(); }
            GUILayout.Label("場域 PNG 為 2688×3840，底部保留黑色留白。", EditorStyles.wordWrappedMiniLabel);
        }

        void DrawColorSettings()
        {
            GUILayout.Space(8);
            GUILayout.Label("全關卡共用色彩", EditorStyles.boldLabel);
            var settings = GameColorSettings.Shared;
            if (settings == null)
            {
                EditorGUILayout.HelpBox("找不到全域色彩設定 704_GameColors。", MessageType.Warning);
                return;
            }
            var serialized = new SerializedObject(settings);
            serialized.Update();
            var red = serialized.FindProperty("hazardRedSaturation");
            var blue = serialized.FindProperty("successBlueSaturation");
            EditorGUI.BeginChangeCheck();
            var redPercent = EditorGUILayout.Slider("關卡紅色飽和度（%）", red.floatValue * 100, 0, 100);
            var bluePercent = EditorGUILayout.Slider("過關藍色飽和度（%）", blue.floatValue * 100, 0, 100);
            if (EditorGUI.EndChangeCheck())
            {
                red.floatValue = redPercent / 100;
                blue.floatValue = bluePercent / 100;
            }
            if (serialized.ApplyModifiedProperties())
            {
                observedColorDirty = EditorUtility.GetDirtyCount(settings);
                previewDirty = true;
                Repaint();
            }
            GUILayout.Label("100% 保留原色；0% 為灰階。藍格與 QR 保持原色，失敗紅色過渡不受影響。", EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("儲存全域色彩")) AssetDatabase.SaveAssetIfDirty(settings);
        }

        void DrawAuthoringSettings()
        {
            GUILayout.Space(8);
            GUILayout.Label("對稱與觸摸數字", EditorStyles.boldLabel);
            if (!CanEditLevel) EditorGUILayout.HelpBox("建立草稿副本後可調整這些設定。", MessageType.None);
            using (new EditorGUI.DisabledScope(!CanEditLevel))
            {
                EditorGUI.BeginChangeCheck();
                var contentMode = (TouchContentMode)EditorGUILayout.Popup("新觸摸格內容", (int)level.newTouchContentMode, new[] { "手動輸入", "自動數字" });
                var style = level.autoTouchStyle;
                var distribution = level.autoNumberOrder;
                var minimum = level.autoAnswerMinimum;
                var maximum = level.autoAnswerMaximum;
                var operandMinimum = level.arithmeticOperandMinimum;
                var operandMaximum = level.arithmeticOperandMaximum;
                if (contentMode == TouchContentMode.Automatic)
                {
                    style = (AutoTouchContentStyle)EditorGUILayout.Popup("自動內容", (int)style, new[] { "一般數字", "算式模式" });
                    distribution = (AutoNumberOrder)EditorGUILayout.Popup("數字分配", (int)distribution, new[] { "依序分配", "範圍隨機" });
                        DrawCompactIntegerRange("答案", ref minimum, ref maximum);
                    if (style == AutoTouchContentStyle.Arithmetic)
                    {
                        DrawCompactIntegerRange("運算元範圍", ref operandMinimum, ref operandMaximum);
                        EditorGUILayout.LabelField("算式使用一次加法或減法；負數答案不產生。", EditorStyles.wordWrappedMiniLabel);
                    }
                    if (level.proMechanic != ProMechanic.None)
                        EditorGUILayout.HelpBox("使用自動觸摸數字／算式時，請將舊版 Pro 機制設為「無」。", MessageType.Warning);
                }
                EditorGUILayout.HelpBox("所有數字藍格遊玩時都強制依答案由小到大觸碰；按錯不會產生效果。", MessageType.Info);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(level, "修改工作台觸摸數字設定");
                    level.newTouchContentMode = contentMode;
                    level.autoTouchStyle = style;
                    level.autoNumberOrder = distribution;
                    level.autoAnswerMinimum = minimum;
                    level.autoAnswerMaximum = maximum;
                    level.arithmeticOperandMinimum = operandMinimum;
                    level.arithmeticOperandMaximum = operandMaximum;
                    placementMessage = null;
                    Changed();
                }
                if (contentMode == TouchContentMode.Automatic &&
                    GUILayout.Button("填入現有空白藍色觸摸格（不覆蓋手寫內容）"))
                    ApplyAutomaticContentToBlankItems();
                if (contentMode == TouchContentMode.Automatic)
                {
                    EditorGUILayout.HelpBox("修改答案／運算元範圍後，可重新套用；重新套用會覆蓋全部藍格目前內容，可用 Undo 復原。", MessageType.Info);
                    if (GUILayout.Button("重新套用至全部藍格（覆蓋手寫內容）"))
                        ReapplyAutomaticContentToAllItems();
                }
            }
        }

        static void DrawCompactIntegerRange(string label, ref int minimum, ref int maximum)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, GUILayout.Width(88));
                minimum = EditorGUILayout.DelayedIntField(minimum, GUILayout.Width(70));
                GUILayout.Label("至", GUILayout.Width(20));
                maximum = EditorGUILayout.DelayedIntField(maximum, GUILayout.Width(70));
            }
        }

        void ApplyInspector(SerializedObject serialized)
        {
            if(!serialized.hasModifiedProperties) return;
            if(LevelPlacementGuard.Apply(serialized,out placementMessage)) ResetPreview();
        }

        void NewLevel()
        {
            EnsureDraftFolder();
            var nextNumber = Enumerable.Range(1, 50).FirstOrDefault(n => levels.All(existing => existing.levelNumber != n));
            if (nextNumber == 0) nextNumber = 50;
            var path = EditorUtility.SaveFilePanelInProject("新增草稿關卡", $"Level_{nextNumber:00}_Draft", "asset", "儲存草稿關卡", DraftFolder);
            if (string.IsNullOrEmpty(path)) return;
            if (!path.StartsWith(DraftFolder + "/", StringComparison.OrdinalIgnoreCase))
            { EditorUtility.DisplayDialog("請儲存到草稿資料夾", "為保護現有關卡，新增關卡請存入 Assets/ScanPro/LevelDrafts。", "確定"); return; }
            var created = CreateInstance<ScanLevel>(); created.levelNumber = nextNumber; created.name = Path.GetFileNameWithoutExtension(path);
            created.layerOrder = TileLayerOrder.CreateDefaultOrder();
            created.venue = level != null ? level.venue : AssetDatabase.LoadAssetAtPath<VenueLayout>(Root + "/Data/704_Venue.asset");
            AssetDatabase.CreateAsset(created, path); level = created; selected = -1; ReloadLevels(); ResetPreview();
        }

        static void EnsureDraftFolder()
        {
            if (!AssetDatabase.IsValidFolder(LevelFolder))
                AssetDatabase.CreateFolder(Root, "Levels");
            if (!AssetDatabase.IsValidFolder(DraftFolder))
                AssetDatabase.CreateFolder(Root, "LevelDrafts");
        }

        internal static bool CanDeleteAsset(ScanLevel asset) => CanEditAsset(asset);

        internal static bool DeleteEditableAsset(ScanLevel asset)
        {
            if (!CanDeleteAsset(asset)) return false;
            var path = AssetDatabase.GetAssetPath(asset);
            if (PrototypeBuild.IsOutputCandidate(asset) && !RemoveLevelFromPrototypeScene(asset)) return false;
            return !string.IsNullOrEmpty(path) && AssetDatabase.DeleteAsset(path);
        }

        static bool RemoveLevelFromPrototypeScene(ScanLevel asset)
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            var scenePath = PrototypeBuild.ScenePath;
            var scene = SceneManager.GetSceneByPath(scenePath);
            var wasLoaded = scene.IsValid() && scene.isLoaded;
            var previousActive = SceneManager.GetActiveScene();
            try
            {
                if (!wasLoaded) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                if (!scene.IsValid() || !scene.isLoaded) return false;
                var games = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<PrototypeGame>(true)).ToArray();
                if (games.Length == 0) return false;
                var changed = false;
                foreach (var game in games)
                {
                    var serialized = new SerializedObject(game);
                    serialized.Update();
                    var references = serialized.FindProperty("levels");
                    if (references == null || !references.isArray) return false;
                    for (var i = references.arraySize - 1; i >= 0; i--)
                    {
                        if (references.GetArrayElementAtIndex(i).objectReferenceValue != asset) continue;
                        references.DeleteArrayElementAtIndex(i);
                        if (i < references.arraySize && references.GetArrayElementAtIndex(i).objectReferenceValue == null)
                            references.DeleteArrayElementAtIndex(i);
                        changed = true;
                    }
                    if (changed)
                    {
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                        EditorUtility.SetDirty(game);
                    }
                }
                if (!changed) return true;
                EditorSceneManager.MarkSceneDirty(scene);
                return EditorSceneManager.SaveScene(scene);
            }
            catch (Exception exception)
            {
                Debug.LogError("無法更新試玩場景的關卡清單，為保護場景引用已取消刪除：" + exception.Message);
                return false;
            }
            finally
            {
                if (!wasLoaded && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
            }
        }

        void DeleteLevel()
        {
            if (!CanDeleteLevel || level == null) return;
            var identifier = level.DisplayTitle;
            var path = AssetDatabase.GetAssetPath(level);
            var formalNote = PrototypeBuild.IsOutputCandidate(level) ? "\n\n此關卡也會從試玩場景的關卡清單移除。" : "";
            if (!level.excludeFromOutput && PrototypeBuild.IsOutputCandidate(level) && PrototypeBuild.FindOutputLevels().Length == 1)
                formalNote += "\n\n刪除後暫無輸出關卡；匯入新關卡或重新勾選關卡後即可 Build。";
            if (!EditorUtility.DisplayDialog("刪除關卡",
                    $"確定永久刪除「{identifier}」？\n\n{path}{formalNote}\n\n唯讀關卡不會被刪除。此動作無法復原。",
                    "刪除", "取消")) return;
            if (!DeleteEditableAsset(level))
            {
                EditorUtility.DisplayDialog("刪除失敗", "此關卡無法安全刪除。試玩場景未能更新時，關卡資產會保留。", "確定");
                return;
            }
            level = null;
            selected = -1;
            ReloadLevels();
            ResetPreview();
            Repaint();
        }

        void CreateDraftCopy()
        {
            if (level == null || IsDraftLevel) return;
            EnsureDraftFolder();
            var copy = Instantiate(level);
            copy.excludeFromOutput = true;
            copy.name = level.name + "_Draft";
            copy.displayName = string.IsNullOrWhiteSpace(level.displayName) ? "關卡草稿" : level.displayName + "（草稿）";
            var path = AssetDatabase.GenerateUniqueAssetPath(DraftFolder + "/" + level.name + "_Draft.asset");
            AssetDatabase.CreateAsset(copy, path);
            level = copy; selected = -1; ReloadLevels(); ResetPreview();
        }

        void PublishDraftAsNewLevel()
        {
            if (!IsDraftLevel) return;
            if (PublishDraftAsset(level, out var error) == null)
            { EditorUtility.DisplayDialog("另存正式關卡失敗", error, "確定"); return; }
            ReloadLevels(); Repaint();
        }

        internal static bool TryResolvePublishNumber(ScanLevel draft, IEnumerable<ScanLevel> formalLevels, out int number, out string error)
        {
            error = null;
            var existing = formalLevels.Where(asset => asset != null).ToArray();
            if (!string.IsNullOrEmpty(draft.importedPackageName) && ScanLevel.TryParsePackageLevelNumber(draft.importedPackageName, out number))
            {
                var packageNumber = number;
                var conflict = existing.FirstOrDefault(asset => asset.levelNumber == packageNumber);
                if (conflict == null) return true;
                error = $"關號 {ScanLevel.FormatLevelIdentifier(packageNumber)} 已由「{conflict.DisplayTitle}」使用。請先處理既有正式關卡；草稿與原關卡均未變更。";
                return false;
            }
            number = Enumerable.Range(1, 50).FirstOrDefault(n => existing.All(asset => asset.levelNumber != n));
            if (number != 0) return true;
            error = "正式關卡編號 1～50 已使用；未覆寫任何關卡。";
            return false;
        }

        internal static ScanLevel PublishDraftAsset(ScanLevel draft, out string error)
        {
            error = null;
            if (!IsDraftAsset(draft)) { error = "只有草稿可以另存正式關卡。"; return null; }
            var formalLevels = AssetDatabase.FindAssets("t:ScanLevel", new[] { LevelFolder })
                .Select(g => AssetDatabase.LoadAssetAtPath<ScanLevel>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
            if (!TryResolvePublishNumber(draft, formalLevels, out var number, out error)) return null;
            var path = $"{LevelFolder}/Level_{number:00}.asset";
            if (File.Exists(path)) { error = $"目標資產已存在：{path}；未覆寫任何關卡。"; return null; }
            var copy = Instantiate(draft);
            copy.excludeFromOutput = false;
            copy.levelNumber = number;
            copy.name = $"Level_{number:00}";
            copy.displayName = copy.displayName.Replace("（草稿）", string.Empty);
            AssetDatabase.CreateAsset(copy, path);
            if (!SyncPrototypeSceneLevels(PrototypeBuild.FindOutputLevels()))
            {
                AssetDatabase.DeleteAsset(path);
                error = "無法安全更新試玩場景的關卡清單；已移除新建立的正式關卡。";
                return null;
            }
            return copy;
        }

        static ScanLevel[] FindFormalLevels()
        {
            return AssetDatabase.FindAssets("t:ScanLevel", new[] { LevelFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ScanLevel>)
                .Where(asset => asset != null)
                .OrderBy(asset => asset.levelNumber)
                .ToArray();
        }

        internal static bool SyncPrototypeSceneLevels(IReadOnlyList<ScanLevel> formalLevels)
        {
            if (formalLevels == null) return false;
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;

            var scenePath = PrototypeBuild.ScenePath;
            var scene = SceneManager.GetSceneByPath(scenePath);
            var wasLoaded = scene.IsValid() && scene.isLoaded;
            var previousActive = SceneManager.GetActiveScene();
            try
            {
                if (!wasLoaded) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                if (!scene.IsValid() || !scene.isLoaded) return false;
                var games = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<PrototypeGame>(true)).ToArray();
                if (games.Length == 0) return false;

                foreach (var game in games)
                {
                    var serialized = new SerializedObject(game);
                    serialized.Update();
                    var references = serialized.FindProperty("levels");
                    if (references == null || !references.isArray) return false;
                    references.arraySize = formalLevels.Count;
                    for (var i = 0; i < formalLevels.Count; i++)
                        references.GetArrayElementAtIndex(i).objectReferenceValue = formalLevels[i];
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(game);
                }

                EditorSceneManager.MarkSceneDirty(scene);
                return EditorSceneManager.SaveScene(scene);
            }
            catch (Exception exception)
            {
                Debug.LogError("無法更新試玩場景的輸出關卡清單：" + exception.Message);
                return false;
            }
            finally
            {
                if (!wasLoaded && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
            }
        }
    }
}
