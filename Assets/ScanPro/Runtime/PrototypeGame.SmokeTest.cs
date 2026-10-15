using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ScanPro704
{
    public sealed partial class PrototypeGame
    {
        readonly List<string> smokeChecks = new List<string>();
        string smokeDirectory;
        [Serializable] public sealed class QrCapture
        {
            public int atlasWidth,atlasHeight;
            public List<QrTargetCapture> targets = new List<QrTargetCapture>();
        }
        [Serializable] public sealed class QrTargetCapture { public string code; public Rect atlasRect; }
        IEnumerator RunSmokeTest()
        {
            smokeDirectory = Path.GetFullPath(CommandOption("--qa-dir", Path.Combine(Application.dataPath, "..", "QA")));
            Directory.CreateDirectory(smokeDirectory);
            var routine = SmokeTestSteps();
            while (true)
            {
                object next;
                try { if (!routine.MoveNext()) break; next = routine.Current; }
                catch (Exception exception)
                {
                    File.WriteAllText(Path.Combine(smokeDirectory, "runtime-checks.txt"), string.Join("\n", smokeChecks) + "\nFAIL " + exception);
                    Debug.LogException(exception); Application.Quit(1); yield break;
                }
                yield return next;
            }
            File.WriteAllText(Path.Combine(smokeDirectory, "runtime-checks.txt"), string.Join("\n", smokeChecks) + $"\nTOTAL: {smokeChecks.Count} passed\nPhysical scanners / venue projection: NOT TESTED\n");
            Debug.Log("SCANPRO_RUNTIME_CHECKS_OK: " + smokeChecks.Count); Application.Quit(0);
        }
        void SmokeCheck(bool condition, string description)
        {
            if (!condition) throw new Exception(description);
            smokeChecks.Add("PASS " + description);
        }
        int CountWhitePixels(RectInt canvasRegion)
        {
            var renderScale = canvas.RenderScale;
            var x0 = canvasRegion.x * renderScale; var x1 = canvasRegion.xMax * renderScale;
            var y0 = canvas.Texture.height - canvasRegion.yMax * renderScale;
            var y1 = canvas.Texture.height - canvasRegion.y * renderScale;
            var pixels = canvas.Texture.GetPixels32();
            var count = 0;
            for (var y = y0; y < y1; y++) for (var x = x0; x < x1; x++)
            {
                var pixel = pixels[y * canvas.Texture.width + x];
                if (pixel.r > 245 && pixel.g > 245 && pixel.b > 245) count++;
            }
            return count;
        }
        void CaptureSmokeFrame(string name)
        {
            var screenshot = ScreenCapture.CaptureScreenshotAsTexture();
            SmokeCheck(screenshot != null && screenshot.width > 500, "Rendered player screenshot: " + name);
            var pixels = screenshot.GetPixels32();
            var previous = RenderTexture.active; RenderTexture.active = atlas;
            var expectedImage = new Texture2D(atlas.width,atlas.height,TextureFormat.RGBA32,false);
            expectedImage.ReadPixels(new Rect(0,0,atlas.width,atlas.height),0,0); expectedImage.Apply();
            RenderTexture.active = previous;
            if(name=="player-704-output")
            {
                File.WriteAllBytes(Path.Combine(smokeDirectory,"player-704-atlas.png"),expectedImage.EncodeToPNG());
                SmokeCheck(expectedImage.width==2688 && expectedImage.height==3840,"Full-resolution venue atlas capture is available for independent QR decoding");
            }
            var expected = expectedImage.GetPixels32(); var mismatches = 0; var samples = 0;
            // Independent CPU reference: one scale for both axes, complete image, black margins.
            var scale = Math.Min(screenshot.width/(double)atlas.width,screenshot.height/(double)atlas.height);
            var left = (screenshot.width-atlas.width*scale)/2 + output.fullscreenHorizontalOffset*(screenshot.width-atlas.width*scale);
            var bottom = (screenshot.height-atlas.height*scale)/2;
            for (var y = 12; y < screenshot.height - 12; y += 11)
                for (var x = 12; x < screenshot.width - 12; x += 11)
                {
                    var sx=(int)Math.Floor((x+.5-left)/scale); var sy=(int)Math.Floor((y+.5-bottom)/scale);
                    var reference=sx>=0 && sy>=0 && sx<atlas.width && sy<atlas.height ? expected[sy*atlas.width+sx] : new Color32(0,0,0,255);
                    var pixel=pixels[y*screenshot.width+x]; samples++;
                    if (Math.Abs(pixel.r-reference.r)>3 || Math.Abs(pixel.g-reference.g)>3 || Math.Abs(pixel.b-reference.b)>3) mismatches++;
                }
            Destroy(expectedImage);
            SmokeCheck(mismatches==0,$"Fullscreen output matches independent aspect-fit reference with black margins: {name} ({samples} sampled pixels, {mismatches} mismatches)");
            File.WriteAllBytes(Path.Combine(smokeDirectory, name + ".png"), screenshot.EncodeToPNG()); Destroy(screenshot);
            if(name=="player-704-output")
            {
                var capture=new QrCapture{atlasWidth=atlas.width,atlasHeight=atlas.height};
                foreach(var item in game.Level.items.Where(i=>i.kind==TileKind.ScanBlue))
                {
                    output.TryMap((game.Position(item)+(Vector2)item.size/2)*game.Level.venue.pixelsPerCell,out var center);
                    var size=new Vector2(item.size.y,item.size.x)*game.Level.venue.pixelsPerCell*6.4f;
                    capture.targets.Add(new QrTargetCapture{code=game.Codes[item.id],atlasRect=new Rect(center-size/2,size)});
                }
                File.WriteAllText(Path.Combine(smokeDirectory,"player-qr-targets.json"),JsonUtility.ToJson(capture,true));
            }
        }
        IEnumerator SmokeTestSteps()
        {
            yield return new WaitForSecondsRealtime(1);
            yield return new WaitForEndOfFrame();
            SmokeCheck(levels.Length > 0 && levels.SequenceEqual(levels.OrderBy(level => level.levelNumber)),
                "Player packages the saved level set in ascending level-number order");
            SmokeCheck(levels.All(level=>level.items.All(item=>!level.venue.TouchesEntrance(item))),"Every level keeps the same entrance free of tiles and movement paths");
            SmokeCheck(outputShader != null && outputShader.isSupported, "704 output atlas shader is included and supported");
            SmokeCheck(output.Validate(out _) && atlas.width == 2688 && atlas.height == 3840, "Player allocates separate 2688x3840 output");
            SmokeCheck(scanners.Status.Contains("已啟動"), "Windows Raw Input registration starts successfully");
            var runtimeDashboard = GetComponent<RuntimeDashboard>();
            SmokeCheck(runtimeDashboard != null && view == 2, "Packaged player starts the venue view and includes a separate operator control window");
            SmokeCheck(outputCamera != null, "Packaged player resolves and retains its venue camera reference");
            SmokeCheck(outputCamera.targetDisplay == (Display.displays.Length > 1 ? 1 : 0),
                "Venue camera routes to the secondary display when present, with single-display fallback");
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (Display.displays.Length > 1)
                SmokeCheck(NativeVenueDisplayWindow.IsSecondaryOutputTopmost(
                    operatorWindowHandle),
                    "Venue output stays above normal windows on the secondary display without activating it");
#endif
            SmokeCheck(!Screen.fullScreen && Screen.fullScreenMode == FullScreenMode.Windowed,
                "Operator Player window starts in resizable windowed mode");
            SmokeCheck(!Debug.isDebugBuild, "Release player has no Development Build overlay");
            var presenter=outputCamera.GetComponent<VenueOutputPresenter>();
            SmokeCheck(presenter.presentationShader!=null && presenter.presentationShader.isSupported,"Aspect-fit presentation shader is included and supported in the player");
            var content=presenter.ContentRect;
            SmokeCheck(presenter.PresentationWidth > 0 && presenter.PresentationHeight > 0 &&
                Math.Abs(content.width * presenter.PresentationWidth / (content.height * presenter.PresentationHeight) - 2688.0 / 3840) < .0001,
                "Venue output preserves the 2688:3840 aspect ratio on its assigned display");
            var members=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
            SmokeCheck(GetType().GetMethod("OnGUI",members)==null && GetType().GetMethod("SimulatePair",members)==null,
                "Editor simulation controls are excluded from the player; packaged dashboard is a separate runtime component");
            CheckNativeWindowedDashboard(
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                nativeLockGridWindow.OperatorWindowHandle
#else
                System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle
#endif
            );
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            SmokeCheck(nativeLockGridWindow != null && nativeLockGridWindow.IsReady && nativeLockGridWindow.HasResizableTopLevelStyle,
                "Lock grid runs in a separate movable and resizable top-level Windows window");
            SmokeCheck(nativeLockGridWindow.Handle != nativeLockGridWindow.OperatorWindowHandle,
                "Control dashboard and lock grid use distinct native window handles");
            var priorDpiContext = SetThreadDpiAwarenessContext(new IntPtr(-4));
            SmokeCheck(priorDpiContext != IntPtr.Zero,
                "Player smoke test can inspect physical display bounds in per-monitor DPI-aware coordinates");
            try
            {
                var operatorMonitor = MonitorFromWindow(nativeLockGridWindow.OperatorWindowHandle, 2);
                var lockMonitor = MonitorFromWindow(nativeLockGridWindow.Handle, 2);
                var monitorInfo = new MonitorInfo { size = Marshal.SizeOf(typeof(MonitorInfo)) };
                var lockBounds = new NativeRect();
                var hasBounds = operatorMonitor != IntPtr.Zero && GetMonitorInfo(operatorMonitor, ref monitorInfo) &&
                    GetWindowRect(nativeLockGridWindow.Handle, out lockBounds);
                SmokeCheck(hasBounds && lockMonitor == operatorMonitor,
                    "Lock grid stays on the same primary display as the operator dashboard");
                SmokeCheck(hasBounds && lockBounds.left >= monitorInfo.monitor.left &&
                    lockBounds.top >= monitorInfo.monitor.top && lockBounds.right <= monitorInfo.monitor.right &&
                    lockBounds.bottom <= monitorInfo.monitor.bottom,
                    "Lock-grid window is fully contained within the primary display in physical pixels");
            }
            finally
            {
                SetThreadDpiAwarenessContext(priorDpiContext);
            }
            var testCell = FindAllowedUnlockedCell();
            SmokeCheck(testCell != null, "Lock-grid smoke test finds an allowed venue cell");
            if (testCell != null)
            {
                nativeLockGridWindow.PostCellClickForTesting(testCell.Value.X, testCell.Value.Y);
                for (var attempt = 0; attempt < 60 && !game.Controls.IsLocked(new Vector2Int(testCell.Value.X, testCell.Value.Y)); attempt++)
                    yield return null;
                var lockedCell = new Vector2Int(testCell.Value.X, testCell.Value.Y);
                SmokeCheck(game.Controls.IsLocked(lockedCell), "Native lock-grid click reaches Unity and locks the selected game cell");
                for (var attempt = 0; attempt < 60 && !nativeLockGridWindow.SnapshotShowsLock(testCell.Value.X, testCell.Value.Y); attempt++)
                    yield return null;
                SmokeCheck(nativeLockGridWindow.SnapshotShowsLock(testCell.Value.X, testCell.Value.Y),
                    "Updated lock state is published back to the native grid view");
                nativeLockGridWindow.PostCellClickForTesting(testCell.Value.X, testCell.Value.Y);
                for (var attempt = 0; attempt < 60 && game.Controls.IsLocked(lockedCell); attempt++) yield return null;
                SmokeCheck(!game.Controls.IsLocked(lockedCell), "Native lock-grid click unlocks the same game cell");
            }
            var originalScreenWidth = Screen.width;
            var originalScreenHeight = Screen.height;
            SmokeCheck(CanMoveResizeAndRestore(nativeLockGridWindow.OperatorWindowHandle),
                "Control dashboard window accepts independent move and resize and restores its original bounds");
            var rendersBeforeGridResize = nativeLockGridWindow.BufferRenderCount;
            SmokeCheck(CanMoveResizeAndRestore(nativeLockGridWindow.Handle),
                "Lock-grid window accepts independent move and resize and restores its original bounds");
            for (var attempt = 0; attempt < 60 && nativeLockGridWindow.BufferRenderCount == rendersBeforeGridResize; attempt++)
                yield return null;
            SmokeCheck(nativeLockGridWindow.BufferRenderCount > rendersBeforeGridResize,
                "Lock-grid window invalidates and redraws its cached frame after resize");
            for (var attempt = 0; attempt < 60 &&
                (Screen.width != originalScreenWidth || Screen.height != originalScreenHeight); attempt++) yield return null;
            SmokeCheck(Screen.width == originalScreenWidth && Screen.height == originalScreenHeight,
                "Control dashboard returns to its original render size after the resize check");
            yield return new WaitForSecondsRealtime(.25f);
            var redrawsBeforeStablePeriod = nativeLockGridWindow.RedrawMessageCount;
            var bufferRendersBeforeStablePeriod = nativeLockGridWindow.BufferRenderCount;
            var unchangedSnapshotsRejected = 0;
            for (var attempt = 0; attempt < 60; attempt++)
            {
                if (!nativeLockGridWindow.Publish(CreateLockGridSnapshot())) unchangedSnapshotsRejected++;
                yield return new WaitForSecondsRealtime(1f / 60f);
            }
            SmokeCheck(unchangedSnapshotsRejected == 60 &&
                nativeLockGridWindow.RedrawMessageCount == redrawsBeforeStablePeriod &&
                nativeLockGridWindow.BufferRenderCount == bufferRendersBeforeStablePeriod,
                "Sixty unchanged lock-grid snapshots over one second schedule no redraw or back-buffer rerender");
#endif
            runtimeDashboard.enabled = false;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            nativeLockGridWindow.Hide();
#endif
            outputCamera.targetDisplay = 0;
            yield return null;
            paused = true; Render();
            SmokeCheck(CountWhitePixels(VenueLayout.ScoreboardProgressPixels) > 0 &&
                       CountWhitePixels(VenueLayout.ScoreboardCountdownPixels) > 0,
                "Venue output renders group progress and remaining-time text in the fixed scoreboard face");
            yield return new WaitForEndOfFrame(); CaptureSmokeFrame("player-704-output");
            var previous = RenderTexture.active; RenderTexture.active = atlas;
            var band = new Texture2D(2688, 512, TextureFormat.RGB24, false);
            band.ReadPixels(new Rect(0, 0, 2688, 512), 0, 0); band.Apply();
            SmokeCheck(band.GetPixels32().All(p => p.r < 2 && p.g < 2 && p.b < 2), "Player output preserves the entire 512px black bottom band");
            Destroy(band); RenderTexture.active = previous;
            for (var index = 0; index < levels.Length; index++)
            {
                StartLevel(index); paused = true;
                while (game.Pro.Questions.Values.Any(q => !q.Completed))
                {
                    if (game.Pro.IsPaired) SimulatePairForSmoke();
                    else
                    {
                        var question = game.Pro.Questions.Values.Where(q => !q.Completed).OrderBy(q => q.Answer).First();
                        var item = game.States[question.Id].Item;
                        game.Tick(.01, new[] { new GameTouch(-20, game.Position(item) + (Vector2)item.size / 2) });
                        SmokeCheck(game.States[item.id].Completed, "Player sorted Pro target completes in order");
                        game.Tick(0, Array.Empty<GameTouch>());
                    }
                }
                var simpleTouchTargets = levels[index].items.Where(i => i.kind == TileKind.TouchBlue && !i.proTarget);
                if (levels[index].proMechanic == ProMechanic.None && levels[index].orderedTouchMode == OrderedTouchMode.Ascending)
                    simpleTouchTargets = simpleTouchTargets.OrderBy(i => TouchNumberContent.TryGetAnswer(i, out var answer) ? answer : int.MaxValue);
                foreach (var item in simpleTouchTargets)
                    game.Tick(.01, new[] { new GameTouch(-21, game.Position(item) + (Vector2)item.size / 2) });
                foreach (var item in levels[index].items.Where(i => i.kind == TileKind.ScanBlue))
                    SmokeCheck(game.Scan("QA-A", game.Codes[item.id], 10000 + index * 1000).Disposition == ScanDisposition.Accepted, $"Level {index + 1} accepts its QR target");
                SmokeCheck(game.Outcome == LevelOutcome.Success && game.Remaining == 0, $"Level {index + 1} completes using actual touch and scan rules");
            }
            game.Tick(.45, Array.Empty<GameTouch>()); Render(); yield return new WaitForEndOfFrame(); CaptureSmokeFrame("player-success");
            if (levels.Any(level => level.levelNumber == 3))
            {
                StartLevel(Array.FindIndex(levels, level => level.levelNumber == 3)); paused = true;
                var purple = game.Level.items.First(i => i.kind == TileKind.HoldPurple);
                var holding = new[] { new GameTouch(-30, game.Position(purple) + (Vector2)purple.size / 2) };
                game.Tick(.01, holding); game.Tick(game.Level.purpleHoldSeconds + .01, holding);
                SmokeCheck(game.Level.items.Where(i => i.kind == TileKind.HazardRed && purple.targetGroups.Contains(i.group)).All(i => game.States[i.id].Disabled), "Player purple long press opens linked hazards");
                var cyan = game.Level.items.First(i => i.kind == TileKind.SwitchCyan);
                SmokeCheck(game.Scan("QA-A", game.Codes[cyan.id], 30000).Disposition == ScanDisposition.Accepted && game.Scan("QA-B", game.Codes[cyan.id], 30001).Disposition == ScanDisposition.Duplicate, "Player deduplicates simultaneous cyan scans across prefixes");
            }
            StartLevel(0); paused = true;
            var red = game.Level.items.First(i => i.kind == TileKind.HazardRed);
            while (!game.Outcome.HasValue)
            {
                game.Tick(.01, new[] { new GameTouch(-40, game.Position(red) + (Vector2)red.size / 2) });
                game.Tick(.01, Array.Empty<GameTouch>());
            }
            SmokeCheck(game.Outcome == LevelOutcome.HeartsDepleted, "Player red contacts exhaust hearts");
            game.Tick(.30, Array.Empty<GameTouch>()); Render(); yield return new WaitForEndOfFrame(); CaptureSmokeFrame("player-hearts-depleted");
            StartLevel(0); paused = true; game.Tick(game.Level.timeLimitSeconds, Array.Empty<GameTouch>()); Render();
            var renderScale = canvas.RenderScale;
            SmokeCheck(game.Outcome == LevelOutcome.Timeout &&
                       canvas.Texture.GetPixel(12 * 10 * renderScale, canvas.Texture.height - 1 - 12 * 10 * renderScale) == TilePalette.ColorOf(TileKind.HazardRed),
                "Player timeout fills distant field red immediately");
            yield return new WaitForEndOfFrame(); CaptureSmokeFrame("player-timeout");
        }
        void SimulatePairForSmoke()
        {
            var next = game.Pro.Questions.Values.Where(q => !q.Completed).OrderBy(q => q.Answer).Take(2).ToArray();
            var count = game.Remaining;
            game.Tick(.01, next.Select((q, i) => new GameTouch(-101 - i, game.Position(game.States[q.Id].Item) + (Vector2)game.States[q.Id].Item.size / 2)).ToArray());
            SmokeCheck(game.Remaining == count - 2, "Player two-touch pair completes together");
            game.Tick(0, Array.Empty<GameTouch>());
        }
        [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int left,top,right,bottom; }
        [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int size; public NativeRect monitor,work; public uint flags; }
        [DllImport("user32.dll",EntryPoint="GetWindowLongW")] static extern int WindowStyle(IntPtr window,int index);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out NativeRect rect);
        [DllImport("user32.dll", EntryPoint="SetThreadDpiAwarenessContext", SetLastError=true)] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);
        [DllImport("user32.dll", EntryPoint="SetWindowPos")] static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter,
            int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
        [DllImport("user32.dll",CharSet=CharSet.Auto)] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
        void CheckNativeWindowedDashboard(IntPtr handle)
        {
            var style = handle == IntPtr.Zero ? 0 : WindowStyle(handle, -16);
            SmokeCheck(handle != IntPtr.Zero && (style & 0x00C40000) == 0x00C40000 && (style & 0x40000000) == 0,
                "Operator Player window has a title bar, resize border, and top-level window style");
            var info=new MonitorInfo {size=Marshal.SizeOf(typeof(MonitorInfo))};
            var valid=GetWindowRect(handle,out var bounds) && GetMonitorInfo(MonitorFromWindow(handle,2),ref info);
            SmokeCheck(valid && bounds.left >= info.work.left && bounds.top >= info.work.top &&
                bounds.right <= info.work.right && bounds.bottom <= info.work.bottom,
                "Operator Player window starts within the primary display work area");
        }
        static bool CanMoveResizeAndRestore(IntPtr handle)
        {
            if (handle == IntPtr.Zero || !GetWindowRect(handle, out var original)) return false;
            var width = original.right - original.left;
            var height = original.bottom - original.top;
            if (width < 720 || height < 560) return false;
            var changed = false;
            try
            {
                var x = original.left + 12;
                var y = original.top + 12;
                var resizedWidth = width > 760 ? width - 40 : width + 40;
                var resizedHeight = height > 600 ? height - 40 : height + 40;
                changed = SetWindowPos(handle, IntPtr.Zero, x, y, resizedWidth, resizedHeight, 0x0004 | 0x0010) &&
                    GetWindowRect(handle, out var resized) && resized.left == x && resized.top == y &&
                    resized.right - resized.left == resizedWidth && resized.bottom - resized.top == resizedHeight;
            }
            finally
            {
                SetWindowPos(handle, IntPtr.Zero, original.left, original.top, width, height, 0x0004 | 0x0010);
            }
            return changed && GetWindowRect(handle, out var restored) &&
                restored.left == original.left && restored.top == original.top &&
                restored.right == original.right && restored.bottom == original.bottom;
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        NativeLockGridWindow.Cell? FindAllowedUnlockedCell()
        {
            if (game == null || game.Level.venue == null) return null;
            for (var y = 0; y < game.Controls.BoardHeight; y++)
            for (var x = 0; x < game.Controls.BoardWidth; x++)
            {
                var cell = new Vector2Int(x, y);
                if (game.Level.venue.IsAllowed(cell) && !game.Controls.IsLocked(cell))
                    return new NativeLockGridWindow.Cell(x, y);
            }
            return null;
        }
#endif
    }
}
