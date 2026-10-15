using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ScanPro704.Editor
{
    // Uses a disposable draft asset and real EditorWindow; never touches formal levels or scenes.
    public static class WorkbenchVerification
    {
        static ScanProWorkbench window;
        static ScanLevel fixture;
        static string fixturePath;
        static readonly List<string> checks = new List<string>();
        static int stage;
        static double stageStart;
        static uint textureRevision;
        static string reportPath;
        static string refreshFixturePath;
        static bool exitWhenDone;
        static readonly List<string> layoutErrors = new List<string>();
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static T Get<T>(string name) => (T)typeof(ScanProWorkbench).GetField(name, Private).GetValue(window);
        static void Set(string name, object value) => typeof(ScanProWorkbench).GetField(name, Private).SetValue(window, value);
        static void Call(string name, params object[] arguments) => typeof(ScanProWorkbench).GetMethod(name, Private).Invoke(window, arguments);
        static Texture2D Canvas => Get<GameCanvasRenderer>("renderer")?.Texture;
        static Color PixelAtCell(int x, int y) => Canvas.GetPixel(x * Canvas.width / 52, Canvas.height - 1 - y * Canvas.height / 42);

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "--qa-dir");
            reportPath = Path.Combine(index >= 0 && index + 1 < args.Length ? args[index + 1] : "TestResults/Workbench", "workbench-checks.txt");
            exitWhenDone = Array.IndexOf(args, "--qa-exit") >= 0;
            checks.Clear(); layoutErrors.Clear(); stage = 0;
            Application.logMessageReceived += CaptureLayoutError;
            var enter = new Event { type = EventType.KeyDown, keyCode = KeyCode.Return };
            Check(ScanProWorkbench.ShouldFinishPathSelection(enter, false) &&
                  !ScanProWorkbench.ShouldFinishPathSelection(enter, true),
                "Enter finishes path selection only when a text field is not being edited");
            fixture = ScriptableObject.CreateInstance<ScanLevel>();
            fixture.venue = AssetDatabase.LoadAssetAtPath<VenueLayout>("Assets/ScanPro/Data/704_Venue.asset");
            fixturePath = AssetDatabase.GenerateUniqueAssetPath("Assets/ScanPro/LevelDrafts/_WorkbenchVerification.asset");
            AssetDatabase.CreateAsset(fixture, fixturePath);
            var defaultLayerOrder = TileLayerOrder.CreateDefaultOrder();
            Check(fixture.layerOrder.SequenceEqual(defaultLayerOrder),
                "A newly created workbench draft uses green, red, purple, cyan, blue from top to bottom");
            var previousLayerOrder = defaultLayerOrder.AsEnumerable().Reverse().ToList();
            fixture.layerOrder = previousLayerOrder;
            Check(typeof(ScanLevel).GetField("layerOrder").IsDefined(typeof(HideInInspector), false),
                "Legacy layer data is hidden from the Inspector; the template cannot be reordered");
            Check(typeof(ScanProWorkbench).Assembly.GetType("ScanPro704.Editor.WorkbenchLayerOrderEditor") == null,
                "The reorder and apply-default workbench interface has been removed");
            fixture.layerOrder = defaultLayerOrder;
            fixture.items.Add(new LevelItem { kind = TileKind.TouchBlue, position = new Vector2Int(12,12), size = new Vector2Int(4,4) });
            fixture.items[0].waypoints.Add(new Vector2Int(32,24));
            fixture.items.Add(new LevelItem { kind = TileKind.ScanBlue, position = new Vector2Int(34,20), size = new Vector2Int(4,4) });
            const string firstShutterState = "qa-switch-first";
            const string secondShutterState = "qa-switch-second";
            fixture.items.Add(new LevelItem { kind = TileKind.HazardRed, position = new Vector2Int(7,9), size = new Vector2Int(3,3), group = firstShutterState });
            fixture.items.Add(new LevelItem { kind = TileKind.HazardRed, position = new Vector2Int(15,9), size = new Vector2Int(3,3), group = secondShutterState });
            var switchItem = new LevelItem { kind = TileKind.SwitchCyan, position = new Vector2Int(24,9), size = new Vector2Int(3,3) };
            switchItem.targetGroups.Add(firstShutterState);
            switchItem.targetGroups.Add(secondShutterState);
            fixture.items.Add(switchItem);
            window = ScriptableObject.CreateInstance<ScanProWorkbench>();
            window.titleContent = new GUIContent("704 工作台驗證");
            window.position = new Rect(100,100,1200,800);
            Set("level", fixture); Call("ResetPreview");
            window.Show(); window.Focus(); window.Repaint();
            stageStart = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }
        static void Check(bool condition, string description)
        {
            if (!condition) throw new Exception(description);
            checks.Add("PASS " + description);
        }
        static void Next(int next)
        {
            stage = next; stageStart = EditorApplication.timeSinceStartup;
            if (Canvas != null) textureRevision = Canvas.updateCount;
        }
        static void Tick()
        {
            try
            {
                var age = EditorApplication.timeSinceStartup - stageStart;
                if (age > 15) throw new Exception("Timed out waiting for workbench GUI stage " + stage);
                window.Repaint(); // Repaint requests alone must not regenerate the canvas.
                switch (stage)
                {
                    case 0:
                        if (Canvas == null || Canvas.updateCount == 0 || age < .5) return;
                        var requiredScale = GameCanvasRenderer.RequiredQrScale(fixture, Get<Dictionary<string,string>>("codes"), 2);
                        Check(Canvas.width == 520 * requiredScale && Canvas.height == 420 * requiredScale, "Real workbench Repaint generates the full-resolution preview at the required QR scale");
                        var activeRed = (Color32)PixelAtCell(8,10);
                        var inactiveRed = (Color32)PixelAtCell(16,10);
                        var expectedRed = (Color32)GameColorSettings.HazardColor;
                        Check(activeRed.r == expectedRed.r && activeRed.g == expectedRed.g && activeRed.b == expectedRed.b &&
                              !inactiveRed.Equals(expectedRed) &&
                              Get<GameSession>("trialGame").States[fixture.items[3].id].Disabled,
                            "The workbench initially displays only the selected shutter frame");
                        var logEntriesType = typeof(EditorWindow).Assembly.GetType("UnityEditor.LogEntries");
                        var getLogCount = logEntriesType?.GetMethod("GetCount", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                            null, Type.EmptyTypes, null);
                        if (getLogCount == null) throw new Exception("Unity Console 訊息數量 API 不存在，無法驗證工作台按鈕。");
                        Debug.Log("SCANPRO_WORKBENCH_CONSOLE_BUTTON_MARKER");
                        Check((int)getLogCount.Invoke(null, null) > 0, "Console button fixture message is present before clearing");
                        Call("ClearConsoleFromWorkbench");
                        Check((int)getLogCount.Invoke(null, null) == 0 && Get<string>("consoleClearFeedback") == "已清空",
                            "The real workbench window callback clears the Console and shows success feedback");
                        Debug.Log("SCANPRO_WORKBENCH_CONSOLE_BUTTON_SECOND_MARKER");
                        Check((int)getLogCount.Invoke(null, null) > 0, "New Console messages remain available after clearing");
                        Call("ClearConsoleFromWorkbench");
                        Check((int)getLogCount.Invoke(null, null) == 0,
                            "The real workbench window can clear the Console repeatedly");
                        Next(1); break;
                    case 1:
                        if (age < .5) return;
                        Check(Canvas.updateCount == textureRevision, "Repeated paused GUI repaints reuse the canvas without texture uploads");
                        Undo.RecordObject(fixture,"QA move target");
                        fixture.items[0].position = new Vector2Int(24,12);
                        EditorUtility.SetDirty(fixture);
                        Next(2); break;
                    case 2:
                        if (Canvas.updateCount == textureRevision) return;
                        var blue = TilePalette.ColorOf(TileKind.TouchBlue);
                        Check(PixelAtCell(25,13)==blue && PixelAtCell(13,13)!=blue,
                            "External level edits invalidate a paused preview and show the new target position");
                        Set("playing",true); Set("lastUpdate",EditorApplication.timeSinceStartup); Next(3); break;
                    case 3:
                        if (age < .5) return;
                        var draws = Canvas.updateCount-textureRevision;
                        Check(draws>0 && Get<double>("previewSeconds")>0, "Playing routes animate through real workbench GUI repaints");
                        Check(draws<=Math.Ceiling(age*60)+2, "Animation canvas uploads respect the 60 Hz upper bound");
                        Set("playing",false); Next(4); break;
                    case 4:
                        if (age < .2) return; // Allow the last requested animation frame to finish.
                        Next(5); break;
                    case 5:
                        if (age < .5) return;
                        Check(Canvas.updateCount==textureRevision, "Pausing animation stops canvas uploads while the GUI still repaints");
                        Undo.PerformUndo(); Next(6); break;
                    case 6:
                        if (Canvas.updateCount==textureRevision) return;
                        Check(fixture.items[0].position==new Vector2Int(12,12) && PixelAtCell(13,13)==TilePalette.ColorOf(TileKind.TouchBlue),
                            "Undo refreshes the paused preview and restores target pixels");
                        var itemCount=fixture.items.Count;
                        Set("brushKind",TileKind.SafeGreen);
                        Call("PlaceItem",new Vector2Int(12,2),new Vector2Int(4,4),TileShape.Rectangle);
                        Check(fixture.items.Count==itemCount && Get<string>("placementMessage")==LevelPlacementGuard.SafeGreenFloorRejection,
                            "Workbench rejects SafeGreen placement on a wall face");
                        Call("PlaceItem",new Vector2Int(12,12),new Vector2Int(4,4),TileShape.Rectangle);
                        Check(fixture.items.Count==itemCount+1 && fixture.items[itemCount].kind==TileKind.SafeGreen && Get<string>("placementMessage")==null,
                            "Workbench accepts SafeGreen placement fully inside Floor");
                        itemCount=fixture.items.Count;
                        Set("brushKind",TileKind.ScanBlue);
                        Call("PlaceItem",new Vector2Int(36,20),new Vector2Int(1,2),TileShape.Rectangle);
                        Check(fixture.items.Count==itemCount && Get<string>("placementMessage").Contains("2×2"),
                            "Workbench rejects a QR smaller than 2×2 without placing a partial object");
                        Call("PlaceItem",new Vector2Int(36,20),new Vector2Int(2,2),TileShape.Rectangle);
                        Check(fixture.items.Count==itemCount+1 && fixture.items[itemCount].kind==TileKind.ScanBlue && Get<string>("placementMessage")==null,
                            "Workbench accepts a 2×2 QR object");
                        fixture.items.Add(new LevelItem { id="qa-blue-manual", kind=TileKind.TouchBlue,
                            position=new Vector2Int(20,12), size=new Vector2Int(4,4), label="99" });
                        fixture.items.Add(new LevelItem { id="qa-blue-generated", kind=TileKind.TouchBlue,
                            position=new Vector2Int(28,12), size=new Vector2Int(4,4), useGeneratedContent=true,
                            generatedAnswer=77, generatedText="77" });
                        fixture.newTouchContentMode=TouchContentMode.Automatic;
                        fixture.autoTouchStyle=AutoTouchContentStyle.Arithmetic;
                        fixture.autoNumberOrder=AutoNumberOrder.Sequential;
                        fixture.autoAnswerMinimum=1; fixture.autoAnswerMaximum=3;
                        fixture.arithmeticOperandMinimum=1; fixture.arithmeticOperandMaximum=10;
                        Call("ReapplyAutomaticContentToAllItems");
                        var blueItems=fixture.items.FindAll(item=>item.kind==TileKind.TouchBlue);
                        var arithmeticApplied=blueItems.Count==3 && blueItems.All(item=>item.useGeneratedContent && string.IsNullOrEmpty(item.label) &&
                                TouchNumberContent.TryGetAnswer(item,out _) &&
                                (item.generatedText.Contains("+") || item.generatedText.Contains("-"))) &&
                              blueItems.Select(item=>item.generatedAnswer).OrderBy(answer=>answer).SequenceEqual(new[]{1,2,3});
                        var arithmeticState=string.Join(";",blueItems.Select(item=>$"{item.id}:{item.useGeneratedContent}:{item.generatedAnswer}:{item.generatedText}:{item.label}"));
                        Check(arithmeticApplied,
                            "Workbench reapplies the configured arithmetic range to every blue tile, including tiles with prior content. " +
                            $"Message={Get<string>("placementMessage") ?? "<none>"}; State={arithmeticState}");
                        var oldAssignments=blueItems.Select(item=>(item.useGeneratedContent,item.generatedAnswer,item.generatedText)).ToArray();
                        fixture.autoAnswerMaximum=2;
                        Call("ReapplyAutomaticContentToAllItems");
                        Check(blueItems.Select(item=>(item.useGeneratedContent,item.generatedAnswer,item.generatedText)).SequenceEqual(oldAssignments) &&
                              !string.IsNullOrEmpty(Get<string>("placementMessage")),
                            "Reapplying an exhausted answer range fails atomically and preserves existing tile content");
                        BeginShutterScanCheck(false);
                        Next(7); break;
                    case 7:
                        if (Canvas.updateCount == textureRevision || age < .6) return;
                        VerifyShutterPreview(true, "Authoring scan callback");
                        SimulateMouseScan();
                        Next(8); break;
                    case 8:
                        if (Canvas.updateCount == textureRevision || age < .2) return;
                        VerifyShutterPreview(false, "Authoring second scan wraps");
                        BeginShutterScanCheck(true);
                        Next(9); break;
                    case 9:
                        if (Canvas.updateCount == textureRevision || age < .6) return;
                        VerifyShutterPreview(true, "Trial scan callback");
                        SimulateMouseScan();
                        Next(10); break;
                    case 10:
                        if (Canvas.updateCount == textureRevision || age < .2) return;
                        VerifyShutterPreview(false, "Trial second scan wraps");
                        Set("trial", false); Set("previewView", 1); Set("previewDirty", true); Set("caveCameraDirty", true);
                        Next(11); break;
                    case 11:
                        if (age < .4) return;
                        var cave = Get<CaveRoom>("cavePreview");
                        Check(cave != null && cave.Image != null && cave.Image.IsCreated(),
                            "The real workbench renders the restored third-person CAVE preview");
                        Set("previewView", 2); Set("previewDirty", true); Next(12); break;
                    case 12:
                        if (age < .4) return;
                        var atlas = Get<RenderTexture>("atlasPreview");
                        Check(atlas != null && atlas.width == 2688 && atlas.height == 3840,
                            "The real workbench renders the venue atlas after switching from CAVE");
                        Check(atlas.filterMode == FilterMode.Point,
                            "Venue output preview preserves sharp QR edges when scaled in the workbench");
                        var refreshFixture = ScriptableObject.CreateInstance<ScanLevel>();
                        refreshFixture.venue = fixture.venue;
                        refreshFixturePath = AssetDatabase.GenerateUniqueAssetPath("Assets/ScanPro/LevelDrafts/_WorkbenchLayoutRefresh.asset");
                        AssetDatabase.CreateAsset(refreshFixture, refreshFixturePath);
                        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                        Next(13); break;
                    case 13:
                        if (age < .4) return;
                        Check(Get<ScanLevel>("level") == fixture,
                            "A draft import refresh keeps the active workbench selection valid");
                        Set("previewView", 1); Set("previewDirty", true); Set("caveCameraDirty", true);
                        if (!string.IsNullOrEmpty(refreshFixturePath))
                        {
                            AssetDatabase.DeleteAsset(refreshFixturePath);
                            refreshFixturePath = null;
                            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                        }
                        Next(14); break;
                    case 14:
                        if (age < .5) return;
                        Set("previewView", 0); Set("previewDirty", true); Next(15); break;
                    case 15:
                        if (age < .4) return;
                        Finish(null); break;
                }
            }
            catch (Exception error) { Finish(error); }
        }
        static void BeginShutterScanCheck(bool trialMode)
        {
            Set("trial", trialMode); Call("ResetPreview");
            var game = Get<GameSession>("trialGame");
            var switchItem = fixture.items[4];
            var point = game.Position(switchItem) + (Vector2)switchItem.size / 2;
            var now = EditorApplication.timeSinceStartup * 1000;
            var accepted = game.TryScanAt(point, "QA-SHUTTER", now, out var first);
            var duplicate = game.TryScanAt(point, "QA-SHUTTER", now + 1, out var second);
            Set("previewDirty", true);
            window.Repaint();
            Check(accepted && first.Disposition == ScanDisposition.Accepted && duplicate &&
                  second.Disposition == ScanDisposition.Duplicate &&
                  Get<GameSession>("trialGame").States[fixture.items[2].id].Disabled,
                (trialMode ? "Trial" : "Authoring") + " duplicate scan does not advance a second frame");
        }
        static void SimulateMouseScan()
        {
            var game = Get<GameSession>("trialGame");
            var switchItem = fixture.items[4];
            var point = game.Position(switchItem) + (Vector2)switchItem.size / 2;
            if (!game.TryScanAt(point, "QA-SHUTTER", EditorApplication.timeSinceStartup * 1000, out var result) ||
                result.Disposition != ScanDisposition.Accepted)
                throw new InvalidOperationException("Mouse scan fixture was not accepted.");
            Set("previewDirty", true);
            window.Repaint();
        }
        static void VerifyShutterPreview(bool secondFrame, string label)
        {
            var red = (Color32)GameColorSettings.HazardColor;
            Check(((Color32)PixelAtCell(8, 10)).Equals(red) == !secondFrame &&
                  ((Color32)PixelAtCell(16, 10)).Equals(red) == secondFrame &&
                  Get<GameSession>("trialGame").States[fixture.items[2].id].Disabled == secondFrame &&
                  Get<GameSession>("trialGame").States[fixture.items[3].id].Disabled == !secondFrame,
                label + ": the real workbench repaint displays only the selected frame");
        }
        static void Finish(Exception error)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= CaptureLayoutError;
            if (error == null)
            {
                try { Check(layoutErrors.Count == 0,
                    "Repeated flat/CAVE/atlas repaints and draft refresh emit no EndLayoutGroup errors"); }
                catch (Exception failure) { error = failure; }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath)));
            File.WriteAllText(reportPath,string.Join("\n",checks)+"\n"+(error==null ? $"TOTAL: {checks.Count} passed\n" : "FAIL "+error+"\n"));
            if (window != null) window.Close();
            if (!string.IsNullOrEmpty(refreshFixturePath)) AssetDatabase.DeleteAsset(refreshFixturePath);
            if (!string.IsNullOrEmpty(fixturePath)) AssetDatabase.DeleteAsset(fixturePath);
            else if (fixture != null) UnityEngine.Object.DestroyImmediate(fixture);
            fixturePath = null; fixture = null; window = null;
            if (error==null) Debug.Log($"SCANPRO_WORKBENCH_CHECKS_OK: {checks.Count} checks passed.");
            else Debug.LogException(error);
            if (exitWhenDone) EditorApplication.Exit(error==null ? 0 : 1);
        }

        static void CaptureLayoutError(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error && condition.IndexOf("EndLayoutGroup", StringComparison.OrdinalIgnoreCase) >= 0)
                layoutErrors.Add(condition + "\n" + stackTrace);
        }
    }
}
