using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ScanPro704
{
    public sealed partial class PrototypeGame : MonoBehaviour
    {
        public ScanLevel[] levels;
        public OutputProfile output;
        public Shader outputShader, caveShader;
        public Camera outputCamera;
        public bool autoAdvance = true;
        GameSession game;
        GameCanvasRenderer canvas;
        Material outputMaterial;
        RenderTexture atlas;
        CaveRoom cave;
        RawScannerInput scanners;
        TouchServiceClient touchClient;
        RuntimeDashboard dashboard;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        NativeLockGridWindow nativeLockGridWindow;
        IntPtr operatorWindowHandle;
        float cursorVisibilityClock;
        bool cursorHidden;
#endif
        readonly TouchState touchState = new TouchState();
        int levelIndex, view;
        bool paused, outputOnly;
        bool selfTesting;
#if UNITY_EDITOR
        Font uiFont;
        Vector2 panelScroll;
        bool mouseScanCaptured;
        Rect field;
#endif
        string scanStatus = "等待掃碼";
        float renderClock;

        IEnumerator Start()
        {
            if (levels == null || levels.Length == 0)
            {
                Debug.LogWarning("SCANPRO_NO_LEVELS: no output levels are assigned; import a level and include it in game output before playing or building.");
                yield break;
            }
            Application.runInBackground = true; Application.targetFrameRate = 60;
#if UNITY_EDITOR
            uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft JhengHei", "Microsoft JhengHei UI", "Arial Unicode MS", "Arial" }, 18);
#endif
            canvas = new GameCanvasRenderer();
            outputMaterial = new Material(outputShader);
            atlas = new RenderTexture(output.outputWidth, output.outputHeight, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point }; atlas.Create();
            var camera = outputCamera != null ? outputCamera : FindObjectOfType<Camera>();
            if (camera == null) throw new InvalidOperationException("704 output camera is missing.");
            outputCamera = camera;
#if UNITY_EDITOR
            cave = new CaveRoom(canvas.Texture, caveShader);
            outputOnly = Environment.GetCommandLineArgs().Contains("--output");
#else
            outputOnly = false;
            view = 2;
#if UNITY_STANDALONE_WIN
            // Keep the operator HWND before the secondary display is created or raised topmost.
            operatorWindowHandle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
#endif
            yield return ConfigureDualDisplay(camera);
            Cursor.visible = true;
            camera.rect = new Rect(0,0,1,1);
            camera.backgroundColor = Color.black;
            var presenter = camera.GetComponent<VenueOutputPresenter>();
            if (presenter == null) presenter = camera.gameObject.AddComponent<VenueOutputPresenter>();
            if (output != null && output.presentationShader != null)
                presenter.presentationShader = output.presentationShader;
            presenter.Source = atlas;
            presenter.HorizontalOffsetFraction = output.fullscreenHorizontalOffset;
            presenter.enabled = true;
#endif
            scanners = new RawScannerInput();
            touchClient = new TouchServiceClient(CommandOption("--touch-uri", "ws://127.0.0.1:20111/"));
            game = new GameSession(Environment.TickCount); StartLevel(0);
#if !UNITY_EDITOR
            dashboard = GetComponent<RuntimeDashboard>();
            if (dashboard == null) dashboard = gameObject.AddComponent<RuntimeDashboard>();
#if UNITY_STANDALONE_WIN
            nativeLockGridWindow = new NativeLockGridWindow();
            nativeLockGridWindow.StartWindow(operatorWindowHandle);
            dashboard.Bind(game, levels, () => levelIndex, StartLevel, () => touchClient.Status, () => scanners.Status,
                ShowLockGridWindow, PublishLockGridSnapshot);
            PublishLockGridSnapshot();
#else
            dashboard.Bind(game, levels, () => levelIndex, StartLevel, () => touchClient.Status, () => scanners.Status, null, null);
#endif
#endif
            selfTesting = Environment.GetCommandLineArgs().Contains("--self-test");
            if (Environment.GetCommandLineArgs().Contains("--qa") || selfTesting) autoAdvance = false;
            if (selfTesting) StartCoroutine(RunSmokeTest());
            yield break;
        }
#if !UNITY_EDITOR
        IEnumerator ConfigureDualDisplay(Camera camera)
        {
            var displayInfo = new List<DisplayInfo>();
            Screen.GetDisplayLayout(displayInfo);
            var hasPrimary = displayInfo.Count > 0;
            var primary = hasPrimary ? displayInfo[0] : default;
            if (Display.displays.Length > 1)
            {
                Display.displays[1].Activate();
                camera.targetDisplay = 1;
                Debug.Log("SCANPRO_DISPLAY: dashboard on primary display; venue output on secondary display.");
            }
            else
            {
                camera.targetDisplay = 0;
                Debug.LogWarning("SCANPRO_DISPLAY: no secondary display detected; the venue output and operator dashboard will share the primary display.");
            }

            var windowWidth = hasPrimary ? Mathf.Clamp(Mathf.RoundToInt(primary.width * .47f), 720, 980) : 920;
            var windowHeight = hasPrimary ? Mathf.Clamp(Mathf.RoundToInt(primary.height * .76f), 600, 960) : 820;
            Screen.fullScreenMode = FullScreenMode.Windowed;
            Screen.fullScreen = false;
            Screen.SetResolution(windowWidth, windowHeight, FullScreenMode.Windowed);
            var windowWaitFrames = 0;
            while ((Screen.fullScreen || Screen.fullScreenMode != FullScreenMode.Windowed ||
                Screen.width != windowWidth || Screen.height != windowHeight) && windowWaitFrames++ < 120)
                yield return null;
            if (hasPrimary)
            {
                var move = Screen.MoveMainWindowTo(primary, new Vector2Int(32, 32));
                if (move != null) yield return move;
            }
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (Display.displays.Length > 1)
            {
                var primaryWindow = operatorWindowHandle;
                var venueWindowPinned = false;
                var reason = "Unity secondary display window is not ready.";
                for (var attempt = 0; attempt < 120; attempt++)
                {
                    if (NativeVenueDisplayWindow.TryKeepOnTopWithoutActivating(primaryWindow, out reason))
                    {
                        venueWindowPinned = true;
                        break;
                    }
                    yield return null;
                }
                if (venueWindowPinned)
                    Debug.Log("SCANPRO_DISPLAY: venue output is kept above normal windows without taking operator focus.");
                else
                    Debug.LogWarning("SCANPRO_DISPLAY: could not keep the venue output visible: " + reason);
            }
#endif
            Debug.Log($"SCANPRO_DISPLAY_READY: operator window {Screen.width}x{Screen.height}; fullscreen={Screen.fullScreen}/{Screen.fullScreenMode}; venue display={camera.targetDisplay}; atlas {output.outputWidth}x{output.outputHeight}, aspect fit.");
            if (Screen.fullScreen || Screen.fullScreenMode != FullScreenMode.Windowed)
                Debug.LogError("SCANPRO_DISPLAY: primary operator window did not enter resizable windowed mode.");
        }
#endif
        void StartLevel(int index)
        {
            if (levels == null || levels.Length == 0 || game == null) return;
            levelIndex = Mathf.Clamp(index, 0, levels.Length - 1); paused = false;
#if UNITY_EDITOR
            mouseScanCaptured = false;
#endif
            game.Start(levels[levelIndex], Environment.TickCount);
            canvas.PrepareQrScale(game.Level, game.Codes);
            touchState.Reset(); scanStatus = "等待掃碼";
            Render();
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            PublishLockGridSnapshot();
#endif
        }
        void Update()
        {
            if (game == null) return;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            ProcessLockGridClicks();
            cursorVisibilityClock += Time.unscaledDeltaTime;
            if (cursorVisibilityClock >= .1f)
            {
                cursorVisibilityClock = 0;
                var hide = Display.displays.Length > 1 &&
                    NativeLockGridWindow.IsCursorOnDifferentDisplayThanMainWindow(operatorWindowHandle);
                if (hide != cursorHidden)
                {
                    cursorHidden = hide;
                    Cursor.visible = !hide;
                }
            }
#endif
            if (selfTesting) return;
#if UNITY_EDITOR
            if (Input.GetKeyDown(KeyCode.F1)) { view = (view + 1) % 3; outputOnly = false; }
            if (Input.GetKeyDown(KeyCode.Space)) paused = !paused;
            if (Input.GetKeyDown(KeyCode.R)) StartLevel(levelIndex);
#endif
            var list = new List<GameTouch>(touchState.Points);
#if UNITY_EDITOR
            var mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            if (Input.GetMouseButtonUp(0)) mouseScanCaptured = false;
            if (Input.GetMouseButtonDown(0) && view == 0 && field.Contains(mouse))
            {
                mouseScanCaptured = paused;
                if (!paused && game.TryScanAt(
                    new Vector2((mouse.x - field.x) / field.width * game.Level.venue.Width,
                        (mouse.y - field.y) / field.height * game.Level.venue.Height),
                    "A", Time.realtimeSinceStartupAsDouble * 1000, out var simulatedScan))
                {
                    mouseScanCaptured = true;
                    var code = game.Codes.TryGetValue(simulatedScan.TargetId, out var targetCode) ? targetCode : "????";
                    scanStatus = $"滑鼠模擬 A/{code} → {simulatedScan.Disposition}";
                    Render();
                }
            }
            if (!paused && view == 0 && Input.GetMouseButton(0) && !mouseScanCaptured && field.Contains(mouse))
                list.Add(new GameTouch(-1, new Vector2((mouse.x - field.x) / field.width * game.Level.venue.Width,
                    (mouse.y - field.y) / field.height * game.Level.venue.Height)));
#endif
            if (!paused) game.Tick(Time.deltaTime, list);
            var count = 0;
            while (touchClient.Messages.TryDequeue(out var json) && count++ < 128)
            {
                if (json == "__RESET__") touchState.Reset();
                else if (touchState.Apply(json, output) && !paused) game.Tick(0, touchState.Points);
            }
            while (scanners.Frames.TryDequeue(out var frame))
            {
                if (paused) continue;
                var result = game.Scan(frame.Prefix, frame.Code, Time.realtimeSinceStartupAsDouble * 1000);
                scanStatus = frame.Prefix + "/" + frame.Code + " → " + result.Disposition;
                if (result.Disposition == ScanDisposition.Accepted) Render();
            }
            if (autoAdvance && game.Outcome.HasValue && game.TransitionAge > 2.4)
            {
                if (game.Outcome == LevelOutcome.Success && levelIndex < levels.Length - 1) StartLevel(levelIndex + 1);
                else if (game.Outcome != LevelOutcome.Success) StartLevel(levelIndex);
            }
            renderClock += Time.deltaTime;
            if (renderClock >= 1f / 30) { renderClock = 0; Render(); }
        }
        void Render()
        {
            var textOrientation = TextOrientationForView(view, outputOnly);
            canvas.Draw(game.Level, game.Elapsed, game, game.Codes, view == 0, -1, textOrientation);
            if (view == 2 || outputOnly) OutputCompositor.Blit(canvas.Texture, atlas, outputMaterial);
#if UNITY_EDITOR
            if (view == 1) { cave.SetTexture(canvas.Texture); cave.Render(); }
#endif
        }
        public static CanvasTextOrientation TextOrientationForView(int view, bool outputOnly) =>
            view == 1 ? CanvasTextOrientation.CavePreview :
            view == 2 || outputOnly ? CanvasTextOrientation.VenueOutput : CanvasTextOrientation.FlatEditor;
#if UNITY_EDITOR
        void OnGUI()
        {
            if (game == null) return;
            var scale = Mathf.Clamp(Screen.width / 1440f, .8f, 1.6f);
            if (uiFont != null) GUI.skin.font = uiFont;
            GUI.skin.label.fontSize = Mathf.RoundToInt(15 * scale); GUI.skin.button.fontSize = Mathf.RoundToInt(14 * scale);
            GUI.backgroundColor = new Color(.22f,.30f,.44f);
            if (outputOnly) { GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height), atlas, ScaleMode.ScaleToFit); return; }
            var sidebar = 320 * scale;
            var area = new Rect(20, 65 * scale, Screen.width - sidebar - 45, Screen.height - 90 * scale);
            var texture = view == 0 ? (Texture)canvas.Texture : view == 1 ? cave.Image : atlas;
            var imageScale = Mathf.Min(area.width / texture.width, area.height / texture.height);
            field = new Rect(area.center.x - texture.width * imageScale / 2, area.center.y - texture.height * imageScale / 2, texture.width * imageScale, texture.height * imageScale);
            GUI.Label(new Rect(24,16,Screen.width - 40,40 * scale), $"704 掃碼 Pro  /  {game.Level.DisplayTitle}");
            GUI.DrawTexture(field, texture, ScaleMode.StretchToFill);
            GUILayout.BeginArea(new Rect(Screen.width - sidebar, 65 * scale, sidebar - 20, Screen.height - 90 * scale), GUI.skin.box);
            panelScroll = GUILayout.BeginScrollView(panelScroll);
            GUILayout.Label("原型操作台");
            view = GUILayout.Toolbar(view, new[] { "平面", "CAVE", "輸出" });
            GUILayout.Space(12);
            GUILayout.Label($"心數  {Math.Max(0,game.Hearts)}");
            GUILayout.Label($"剩餘時間  {Math.Max(0,game.Level.timeLimitSeconds-game.Elapsed):0.0}s");
            GUILayout.Label($"待完成目標  {game.Remaining}");
            if (game.Outcome.HasValue) GUILayout.Label(game.Outcome == LevelOutcome.Success ? "完成！" : game.Outcome == LevelOutcome.Timeout ? "時間到" : "心數耗盡");
            if (GUILayout.Button(paused ? "繼續" : "暫停")) paused = !paused;
            if (GUILayout.Button("重新開始本關")) StartLevel(levelIndex);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("上一關")) StartLevel((levelIndex + levels.Length - 1) % levels.Length);
            if (GUILayout.Button("下一關")) StartLevel((levelIndex + 1) % levels.Length);
            GUILayout.EndHorizontal();
            if (game.Pro.IsPaired && GUILayout.Button("桌面模擬：下一組雙點")) SimulatePair();
            autoAdvance = GUILayout.Toggle(autoAdvance,"自動續關／失敗重試");
            GUILayout.Label($"最近掃碼  {scanStatus}");
            GUILayout.Space(12);
            GUILayout.Label(touchClient.Status); GUILayout.Label(touchState.Status); GUILayout.Label(scanners.Status);
            GUILayout.FlexibleSpace();
            GUILayout.Label("F1 切換視圖｜空白 暫停｜R 重試\n平面視圖可點藍色 QR 模擬掃碼；藍格可用滑鼠觸摸。\n20 cm² 的實體面積仍待場域校正。");
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
        void SimulatePair()
        {
            if (paused || game.Outcome.HasValue) return;
            var next = game.Pro.Questions.Values.Where(q => !q.Completed).OrderBy(q => q.Answer).Take(2).ToArray();
            if (next.Length != 2 || next[0].Answer != next[1].Answer) return;
            game.Tick(0, next.Select((q, i) => new GameTouch(-101 - i, game.Position(game.States[q.Id].Item) + (Vector2)game.States[q.Id].Item.size / 2)).ToArray());
            Render();
        }
#endif
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        void ProcessLockGridClicks()
        {
            if (nativeLockGridWindow == null || game == null) return;
            var processed = 0;
            while (nativeLockGridWindow.TryTakeClick(out var cell) && processed++ < 64)
            {
                var position = new Vector2Int(cell.X, cell.Y);
                if (game.Level.venue == null || !game.Level.venue.IsAllowed(position))
                {
                    dashboard?.SetMessage("該格位於場域不可互動區，無需鎖定。");
                    continue;
                }
                var locked = game.Controls.ToggleLock(position);
                dashboard?.SetMessage($"格子 ({position.x + 1}, {position.y + 1}) 已{(locked ? "鎖定" : "解鎖")}。");
                PublishLockGridSnapshot();
            }
        }

        void PublishLockGridSnapshot()
        {
            if (nativeLockGridWindow == null || game == null || game.Controls == null) return;
            nativeLockGridWindow.Publish(CreateLockGridSnapshot());
        }

        NativeLockGridWindow.GridSnapshot CreateLockGridSnapshot()
        {
            var width = game.Controls.BoardWidth;
            var height = game.Controls.BoardHeight;
            var allowed = new bool[width * height];
            var locked = new bool[width * height];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                var cell = new Vector2Int(x, y);
                allowed[index] = game.Level.venue != null && game.Level.venue.IsAllowed(cell);
                locked[index] = game.Controls.IsLocked(cell);
            }
            return new NativeLockGridWindow.GridSnapshot(width, height, allowed, locked);
        }

        void ShowLockGridWindow() { nativeLockGridWindow?.Show(); }
#endif
        static string CommandOption(string key, string fallback)
        {
            var args = Environment.GetCommandLineArgs(); var index = Array.IndexOf(args, key);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }
        void OnDestroy()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            nativeLockGridWindow?.Dispose();
            Cursor.visible = true;
#endif
            scanners?.Dispose(); touchClient?.Dispose(); cave?.Dispose(); canvas?.Dispose();
            if (atlas != null) { atlas.Release(); Destroy(atlas); }
            if (outputMaterial != null) Destroy(outputMaterial);
#if UNITY_EDITOR
            if (uiFont != null) Destroy(uiFont);
#endif
        }
    }
}
