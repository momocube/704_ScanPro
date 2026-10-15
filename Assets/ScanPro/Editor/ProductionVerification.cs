using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZXing;

namespace ScanPro704.Editor
{
    public static class ProductionVerification
    {
        static readonly List<string> passed = new List<string>();
        static void Check(bool condition, string description)
        { if (!condition) throw new Exception("CHECK FAILED: " + description); passed.Add(description); }
        static LevelItem Item(TileKind kind, int x, int y, string group = "") => new LevelItem { kind = kind, position = new Vector2Int(x,y), size = new Vector2Int(4,4), group = group };
        static LevelMaskFrame ClearMask(params Vector2Int[] cells)
        {
            var minX = cells.Min(cell => cell.x); var minY = cells.Min(cell => cell.y);
            var maxX = cells.Max(cell => cell.x); var maxY = cells.Max(cell => cell.y);
            var width = maxX - minX + 1; var height = maxY - minY + 1;
            var bits = new BitArray(width * height);
            foreach (var cell in cells) bits[(cell.y - minY) * width + cell.x - minX] = true;
            var bytes = new byte[(bits.Length + 7) / 8]; bits.CopyTo(bytes, 0);
            return new LevelMaskFrame { offset = new Vector2Int(minX, minY), width = width, height = height,
                packedPixels = Convert.ToBase64String(bytes) };
        }
        static ScanLevel Fixture(params LevelItem[] items)
        {
            var level = ScriptableObject.CreateInstance<ScanLevel>(); level.items.AddRange(items);
            level.venue = AssetDatabase.LoadAssetAtPath<VenueLayout>("Assets/ScanPro/Data/704_Venue.asset"); return level;
        }
        public static void RunColorLayerRegressions()
        {
            passed.Clear();
            VerifyMinimumQrSizes();
            VerifyRequestedRuntimeRules();
            VerifyWorkbenchAuthoringRules();
            VerifyPurpleHoldFeedback();
            VerifyAnimatedPurpleShape();
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "--qa-dir");
            var directory = index >= 0 && index + 1 < args.Length ? args[index + 1] : "TestResults/ColorLayer";
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "production-checks.txt"),
                string.Join("\n", passed.Select(check => "PASS " + check)) + $"\nTOTAL: {passed.Count} passed\n");
            Debug.Log($"SCANPRO_COLOR_LAYER_REGRESSIONS_OK: {passed.Count} checks passed.");
        }
        [MenuItem("704/驗證/QR 最小尺寸與預設圖層")]
        public static void RunMinimumQrChecks()
        {
            passed.Clear();
            VerifyMinimumQrSizes();
            Directory.CreateDirectory("TestResults");
            File.WriteAllText("TestResults/qr-minimum-checks.txt",
                string.Join("\n", passed.Select(check => "PASS " + check)) + $"\nTOTAL: {passed.Count} passed\n");
            Debug.Log($"SCANPRO_QR_MINIMUM_CHECKS_OK: {passed.Count} checks passed.");
        }
        // Retain the old batch entry point while enforcing the latest scan-per-frame rule.
        public static void RunShutterColorVerification() => ShutterFrameVerification.Run();
        [MenuItem("704/完整原型驗證")]
        public static void Run()
        {
            passed.Clear(); PrototypeBuild.Prepare(); ProjectSetup.ValidatePrototype();
            Check(ScanLevel.FormatLevelIdentifier(1) == "1-1" && ScanLevel.FormatLevelIdentifier(5) == "1-5" &&
                  ScanLevel.FormatLevelIdentifier(6) == "2-1" && ScanLevel.FormatLevelIdentifier(10) == "2-5" &&
                  ScanLevel.FormatLevelIdentifier(50) == "10-5",
                "Level numbering displays as five levels per group: 1-1 through 1-5, then 2-1 through 10-5");
            var editorMethods = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            Check(typeof(PrototypeGame).GetMethod("OnGUI",editorMethods)!=null && typeof(PrototypeGame).GetMethod("SimulatePair",editorMethods)!=null,
                "Unity Editor retains the operator GUI and desktop simulation controls");
            VerifyTextOrientation();
            VerifyVenueScoreboardHud();
            VerifyMinimumQrSizes();
            var currentLevels=AssetDatabase.FindAssets("t:ScanLevel",new[]{"Assets/ScanPro/Levels"})
                .Select(g=>AssetDatabase.LoadAssetAtPath<ScanLevel>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(l=>l!=null && l.levelNumber>=1 && l.levelNumber<=5).ToArray();
            Check(currentLevels.Length==5 && currentLevels.All(ScanProWorkbench.CanEditAsset),
                "The five current formal levels are editable and saveable in the workbench and Inspector");
            var levelSix=ScriptableObject.CreateInstance<ScanLevel>(); levelSix.levelNumber=6;
            Check(!ScanProWorkbench.CanEditAsset(levelSix),"Formal levels beyond the current five remain read-only");
            UnityEngine.Object.DestroyImmediate(levelSix);
            var preparedGame = UnityEngine.Object.FindObjectOfType<PrototypeGame>();
            Check(preparedGame!=null && preparedGame.outputCamera!=null && !preparedGame.outputCamera.GetComponent<VenueOutputPresenter>().enabled,
                "Prepared scene leaves fullscreen camera presentation disabled for Editor previews");
            var profile = AssetDatabase.LoadAssetAtPath<OutputProfile>(PrototypeBuild.ProfilePath);
            Check(profile.presentationShader == Shader.Find("704/Fullscreen Fit"),
                "Output profile keeps a direct reference to the runtime fullscreen-fit shader");
            VerifyEntrance(profile);
            VerifySafeGreenFloor();
            FullscreenVerification.Verify(profile,Check);
            Check(profile.Validate(out _) && profile.outputHeight == 3840 && profile.surfaces.Count == 7, "Seven-face 2688x3840 profile validates");
            foreach (var surface in profile.surfaces.Where(s => s.faceIndex != 5 && s.faceIndex != 6))
            {
                var source = new Vector2(surface.sourcePixels.center.x,surface.sourcePixels.center.y);
                Check(profile.TryMap(source,out var mapped) && profile.TryUnmap(surface.faceIndex,mapped,out var back) && Vector2.Distance(source,back) < .01, "Roundtrip mapping: " + surface.name);
            }
            var scoreboardCenter = new Vector2(VenueLayout.ScoreboardPixels.center.x,VenueLayout.ScoreboardPixels.center.y);
            Check(profile.TryMap(scoreboardCenter,out var scoreboardOutput) &&
                OutputProfile.ScoreboardOutputPixels.Contains(Vector2Int.FloorToInt(scoreboardOutput)) &&
                !profile.TryUnmap(6,scoreboardOutput,out _),
                "Fixed scoreboard maps to its output face for rendering but rejects touch input");
            Check(!profile.TryUnmap(1,new Vector2(1000,3500),out _), "Unused 512px bottom band is not a touch area");
            Check(!profile.TryUnmap(5,new Vector2(2200,2100),out _), "Entrance is visual-only, not a game touch target");
            var maskTexture = new Texture2D(20,20,TextureFormat.RGBA32,false);
            var cachedVenue = ScriptableObject.CreateInstance<VenueLayout>(); cachedVenue.background = maskTexture;
            maskTexture.SetPixels32(new Color32[400]); maskTexture.SetPixel(5,14,Color.white); maskTexture.Apply();
            Check(cachedVenue.IsAllowed(Vector2Int.zero),"Cached venue mask reads the supplied pixel center");
            var maskRevision = cachedVenue.MaskRevision;
            maskTexture.SetPixel(5,14,Color.black); maskTexture.Apply();
            Check(!cachedVenue.IsAllowed(Vector2Int.zero) && cachedVenue.MaskRevision > maskRevision,"Texture updates invalidate the venue mask cache");
            maskTexture.SetPixel(10,9,Color.white); maskTexture.Apply(); cachedVenue.pixelsPerCell=20;
            Check(cachedVenue.Width==1 && cachedVenue.IsAllowed(Vector2Int.zero),"Changing grid size invalidates cached mask geometry");
            var maskLevel=ScriptableObject.CreateInstance<ScanLevel>(); maskLevel.venue=cachedVenue;
            using(var maskRenderer=new GameCanvasRenderer())
            {
                maskRenderer.Draw(maskLevel,0,grid:true);
                var before=maskRenderer.Texture.GetPixel(2,maskRenderer.Texture.height-3);
                maskTexture.SetPixel(10,9,Color.black);maskTexture.Apply();maskRenderer.Draw(maskLevel,0,grid:true);
                var after=maskRenderer.Texture.GetPixel(2,maskRenderer.Texture.height-3);
                Check(before!=Color.black && after==Color.black,"Static preview background cache refreshes when source mask changes");
            }
            UnityEngine.Object.DestroyImmediate(maskLevel);UnityEngine.Object.DestroyImmediate(cachedVenue);UnityEngine.Object.DestroyImmediate(maskTexture);
            var fullPool=new ScanRegistry(704);
            var allCodes=fullPool.BeginRound(Enumerable.Range(0,10000).Select(i=>new ScanTarget("pool-"+i,false)));
            Check(allCodes.Values.Distinct().Count()==10000,"Integer scanner pool produces every four-digit code exactly once");
            var exhausted=false;try{fullPool.BeginRound(new[]{new ScanTarget("extra",false)});}catch(InvalidOperationException){exhausted=true;}
            Check(exhausted && fullPool.Submit(fullPool.Round,"QA",allCodes["pool-0"],1).Disposition==ScanDisposition.Accepted,"Exhausted scanner pool rejects a new round without corrupting the active round");
            Check(profile.TryUnmap(2,new Vector2(2048,2688),out var edge) && profile.editorLayout.IsAllowed(Vector2Int.FloorToInt(edge/10)), "TouchService far-edge px clamps safely inside face");

            var blue = Item(TileKind.TouchBlue,12,12); var scan = Item(TileKind.ScanBlue,30,20);
            var level = Fixture(blue,scan); var game = new GameSession(); game.Start(level);
            game.Tick(.01,new[] {new GameTouch(1,new Vector2(13,13))});
            Check(game.Remaining == 1, "Touch target completes once");
            game.Scan("A",game.Codes[scan.id],1000);
            Check(game.Outcome == LevelOutcome.Success && game.LastPosition == new Vector2(32,22), "Final scan wins and blue expansion starts at scan target");
            game.Start(level); game.Tick(level.timeLimitSeconds+1,Array.Empty<GameTouch>());
            Check(game.Outcome == LevelOutcome.Timeout && !TransitionRules.For(game.Outcome.Value,game.LastPosition).Expands, "Level timeout uses full red without expansion");

            var red = Item(TileKind.HazardRed,20,20); var safe = Item(TileKind.SafeGreen,20,20);
            level = Fixture(red,Item(TileKind.TouchBlue,12,12)); level.initialHearts = 2; game.Start(level);
            game.Tick(.01,new[] {new GameTouch(7,new Vector2(21,21))}); game.Tick(1,new[] {new GameTouch(7,new Vector2(21,21))});
            Check(game.Hearts == 1, "Holding one red contact does not deduct hearts each frame");
            game.Tick(.01,Array.Empty<GameTouch>()); game.Tick(.01,new[] {new GameTouch(7,new Vector2(21,21))});
            Check(game.Outcome == LevelOutcome.HeartsDepleted, "Leaving and re-entering red consumes the next heart");
            level = Fixture(red,safe,Item(TileKind.TouchBlue,12,12)); level.initialHearts = 3; game.Start(level);
            game.Tick(.1,new[] {new GameTouch(1,new Vector2(21,21))});
            Check(game.Hearts == 3, "The default green top layer protects a red hazard underneath it");
            level = Fixture(red,safe,Item(TileKind.TouchBlue,12,12)); level.initialHearts = 3;
            level.layerOrder = new List<TileLayer> { TileLayer.Blue, TileLayer.SafeGreen, TileLayer.HazardRed, TileLayer.HoldPurple, TileLayer.SwitchCyan };
            game.Start(level); game.Tick(.1,new[] {new GameTouch(1,new Vector2(21,21))});
            Check(game.Hearts == 3, "Saved red-above-green data cannot override the fixed green top layer");
            var groupedA = Item(TileKind.HazardRed,20,20,"overlap"); var groupedB = Item(TileKind.HazardRed,21,20,"overlap");
            level = Fixture(groupedA,groupedB,Item(TileKind.TouchBlue,12,12)); level.initialHearts = 3; game.Start(level);
            game.Tick(.01,new[]{new GameTouch(10,new Vector2(22,21))});
            Check(game.Hearts == 2,"Overlapping hazards in one group deduct only one heart per touch");
            game.Tick(.01,new[]{new GameTouch(10,new Vector2(20.5f,21))});
            Check(game.Hearts == 2,"Sliding inside one hazard group does not repeatedly deduct hearts");
            game.Tick(.01,Array.Empty<GameTouch>()); game.Tick(.01,new[]{new GameTouch(10,new Vector2(22,21))});
            Check(game.Hearts == 1,"Leaving the entire hazard group re-arms damage");
            var pathAcrossEntrance = Item(TileKind.TouchBlue,28,2); pathAcrossEntrance.size=Vector2Int.one; pathAcrossEntrance.waypoints.Add(new Vector2Int(38,10));
            Check(level.venue.IsAllowed(pathAcrossEntrance.position,pathAcrossEntrance.size) && level.venue.IsAllowed(pathAcrossEntrance.waypoints[0],pathAcrossEntrance.size) && !level.venue.PathIsAllowed(pathAcrossEntrance),"Valid path endpoints cannot hide a segment through the entrance black gap");
            var pathInFloor = Item(TileKind.TouchBlue,12,12); pathInFloor.waypoints.Add(new Vector2Int(30,25));
            Check(level.venue.PathIsAllowed(pathInFloor),"Complete diagonal path and return segment inside floor are allowed");
            var tinyQr=Item(TileKind.ScanBlue,12,12); tinyQr.size=new Vector2Int(2,2);
            Check(!LevelValidation.ContentErrors(tinyQr).Any(e=>e.Contains("QR 區域")),"A static 2×2 QR meets the minimum scan size");
            foreach (var size in new[] { new Vector2Int(1,1), new Vector2Int(1,2), new Vector2Int(2,1) })
            {
                tinyQr.size = size;
                Check(LevelValidation.ContentErrors(tinyQr).Any(e=>e.Contains("QR 區域")),$"A {size.x}×{size.y} QR is rejected below the 2×2 minimum");
            }
            tinyQr.size = new Vector2Int(2,2);

            var hold = Item(TileKind.HoldPurple,12,20); red.group="A"; hold.targetGroups.Add("A");
            level=Fixture(red,hold,Item(TileKind.TouchBlue,12,12)); game.Start(level);
            game.Tick(.1,new[]{new GameTouch(1,new Vector2(13,21))}); game.Tick(.9,new[]{new GameTouch(1,new Vector2(13,21))});
            Check(game.IsPurpleClearTarget(red) && !game.States[red.id].Disabled,
                "Purple long press activates its linked clear mask without disabling the whole red group");
            VerifyPurpleHoldFeedback();
            var redB=Item(TileKind.HazardRed,26,20,"B"); var toggle=Item(TileKind.SwitchCyan,34,20); toggle.targetGroups.AddRange(new[]{"A","B"});
            level=Fixture(red,redB,toggle,Item(TileKind.TouchBlue,12,12)); game.Start(level);
            Check(!game.States[red.id].Disabled && game.States[redB.id].Disabled,"Switch initially selects one red group");
            Check(!game.IsInactiveShutterHazard(red) && game.IsInactiveShutterHazard(redB),
                "The shutter session identifies only the unselected red group as inactive");
            using (var shutterRenderer = new GameCanvasRenderer(1))
            {
                shutterRenderer.Draw(level, game.Elapsed, game);
                var redColor = (Color32)TilePalette.ColorOf(TileKind.HazardRed);
                Check(CanvasPixel(shutterRenderer.Texture, redB.position.x * 10 + 5, redB.position.y * 10 + 5, shutterRenderer.RenderScale).Equals((Color32)Color.black) &&
                      CanvasPixel(shutterRenderer.Texture, red.position.x * 10 + 5, red.position.y * 10 + 5, shutterRenderer.RenderScale).Equals(redColor),
                    "Only the selected shutter frame is visible in the initial game state");
            }
            var code=game.Codes[toggle.id]; game.Scan("A",code,2000);
            Check(game.States[red.id].Disabled && !game.States[redB.id].Disabled,"Cyan scan changes safe-zone group");
            Check(game.IsInactiveShutterHazard(red) && !game.IsInactiveShutterHazard(redB),
                "The old shutter group becomes inactive after scanning");
            using (var shutterRenderer = new GameCanvasRenderer(1))
            {
                shutterRenderer.Draw(level, game.Elapsed, game);
                var redColor = (Color32)TilePalette.ColorOf(TileKind.HazardRed);
                Check(CanvasPixel(shutterRenderer.Texture, red.position.x * 10 + 5, red.position.y * 10 + 5, shutterRenderer.RenderScale).Equals((Color32)Color.black) &&
                      CanvasPixel(shutterRenderer.Texture, redB.position.x * 10 + 5, redB.position.y * 10 + 5, shutterRenderer.RenderScale).Equals(redColor),
                    "Scanning hides the old shutter frame and shows only the next one");
            }
            Check(game.Scan("B",code,2100).Disposition == ScanDisposition.Duplicate,"Concurrent other device does not toggle again");
            Check(game.Scan("B",code,2600).Disposition == ScanDisposition.Accepted && !game.States[red.id].Disabled,"Cyan re-arms after quiet interval");
            level.switchQuietIntervalMs=800; game.Start(level); code=game.Codes[toggle.id];
            game.Scan("A",code,4000);
            Check(game.Scan("B",code,4600).Disposition==ScanDisposition.Duplicate && game.Scan("A",code,5400).Disposition==ScanDisposition.Accepted,"Level-specific cyan quiet interval is applied");

            var sequence = new ProSequence(ProMechanic.Numbers7,Enumerable.Range(1,7).Select(i=>i.ToString()),704);
            var first=sequence.Questions.Values.First(q=>q.Answer==1); var second=sequence.Questions.Values.First(q=>q.Answer==2);
            Check(!sequence.Press(second.Id,1,0).Any(),"Wrong Pro order is ignored without completion");
            Check(sequence.Press(first.Id,1,0).Single()==first.Id,"Correct Pro order completes current number");
            var pair=new ProSequence(ProMechanic.Pairs7,new[]{"left","right"},704);
            pair.Press("left",1,1); Check(!pair.Press("right",1,1.1).Any(),"One touch ID cannot complete both paired buttons");
            Check(pair.Press("right",2,1.199).Count()==2,"Two touches within 0.20s complete a pair");
            var pairLeft=Item(TileKind.TouchBlue,12,20); var pairRight=Item(TileKind.TouchBlue,30,20); pairLeft.proTarget=true; pairRight.proTarget=true;
            var pairLevel=Fixture(pairLeft,pairRight,Item(TileKind.ScanBlue,34,12)); pairLevel.proMechanic=ProMechanic.Pairs7; game.Start(pairLevel);
            game.Tick(.01,new[]{new GameTouch(1,new Vector2(13,21))}); game.Tick(.01,Array.Empty<GameTouch>());
            game.Tick(.10,new[]{new GameTouch(2,new Vector2(31,21))});
            Check(!game.States[pairLeft.id].Completed && !game.States[pairRight.id].Completed,"Released first touch cannot complete a pair even within 0.20s");
            game.Tick(.01,Array.Empty<GameTouch>());
            game.Tick(.01,new[]{new GameTouch(1,new Vector2(13,21)),new GameTouch(2,new Vector2(31,21))});
            Check(game.States[pairLeft.id].Completed && game.States[pairRight.id].Completed,"Two concurrently held pair contacts complete together");
            var late=new ProSequence(ProMechanic.Pairs7,new[]{"left","right"},704);
            late.Press("left",1,1); Check(!late.Press("right",2,1.201).Any(),"Pair outside 0.20s is not accepted");
            var countdown=new ProSequence(ProMechanic.Countdown,new[]{"clock"},704);
            Check(countdown.Tick(31).Count()==1 && !countdown.Tick(32).Any() && countdown.Press("clock",1,32).Any(),"Expired countdown penalizes once and still requires pressing");

            var framer=new ScannerFramer(); var frames=new List<ScannerFrame>(); framer.Frame+=frames.Add;
            var a="A/0000"; var b="B/0001";
            for(var i=0;i<a.Length;i++){framer.Push("USB-A",a[i],i*10);framer.Push("USB-B",b[i],i*10);}
            Check(frames.Count==2 && frames.Any(f=>f.Prefix=="A"&&f.Code=="0000") && frames.Any(f=>f.Prefix=="B"&&f.Code=="0001"),"Interleaved physical keyboards yield intact A/0000 and B/0001 frames");
            framer.Push("USB-A",'\r',100);framer.Push("USB-B",'\n',100);
            Check(frames.Count==2,"Optional scanner terminators do not duplicate a frame");

            var touches=new TouchState();
            string Frame(long seq,TouchEvent[] events,int[] alive)=>JsonUtility.ToJson(new TouchFrame {v=1,seq=seq,venue="704",canvas=new TouchCanvas{w=2688,h=3840},events=events,alive=alive});
            Check(touches.Apply(Frame(1,new[]{new TouchEvent{id=9,phase="down",face=2,px=new[]{1344f,1664f}}},new[]{9}),profile) && touches.Points.Count==1,"TouchService v1 JSON produces a mapped touch");
            Check(touches.Points[0].CellPosition==new Vector2(26,21),"TouchService output px maps to editor cells, not sensor-cell indices");
            touches.Apply(Frame(2,Array.Empty<TouchEvent>(),Array.Empty<int>()),profile);
            Check(touches.Points.Count==0,"alive snapshot removes lost-up ghost touch");
            Check(!touches.Apply(Frame(1,Array.Empty<TouchEvent>(),new[]{9}),profile),"Out-of-order frame is rejected");
            touches.Reset(); Check(touches.Apply(Frame(1,Array.Empty<TouchEvent>(),Array.Empty<int>()),profile),"Reconnect resets sequence tracking");

            Directory.CreateDirectory("TestResults");
            var sample=AssetDatabase.LoadAssetAtPath<ScanLevel>("Assets/ScanPro/Levels/Level_01.asset");
            game.Start(sample);
            using(var renderer=new GameCanvasRenderer())
            {
                renderer.Draw(sample,0,game,game.Codes);
                File.WriteAllBytes("TestResults/flat-preview.png",renderer.Texture.EncodeToPNG());
                foreach(var item in sample.items.Where(i=>i.IsScan))
                {
                    var renderScale=renderer.RenderScale;
                    var rect=new RectInt(item.position.x*10*renderScale,item.position.y*10*renderScale,
                        item.size.x*10*renderScale,item.size.y*10*renderScale);
                    var bytes=new byte[rect.width*rect.height*3]; var image=renderer.Texture.GetPixels32();
                    for(var y=0;y<rect.height;y++)for(var x=0;x<rect.width;x++)
                    {var p=image[(renderer.Texture.height-1-rect.y-y)*renderer.Texture.width+rect.x+x];var j=(y*rect.width+x)*3;bytes[j]=p.r;bytes[j+1]=p.g;bytes[j+2]=p.b;}
                    var decoded=new BarcodeReaderGeneric().Decode(new RGBLuminanceSource(bytes,rect.width,rect.height,RGBLuminanceSource.BitmapFormat.RGB24));
                    Check(decoded!=null && decoded.Text==game.Codes[item.id],"Independent QR decoding matches four-digit target: "+game.Codes[item.id]);
                }
                var material=new Material(Shader.Find("704/Output Atlas"));var target=new RenderTexture(2688,3840,0);target.Create();
                var previousTarget=RenderTexture.active;
                renderer.Draw(sample,0,game,game.Codes,false,-1,CanvasTextOrientation.VenueOutput);
                OutputCompositor.Blit(renderer.Texture,target,material);
                Check(RenderTexture.active==previousTarget,"Atlas rendering restores active target for IMGUI");
                WorkbenchExportService.WriteRenderTexture(target,"TestResults/output-preview.png");
                using(var cave=new CaveRoom(target,Shader.Find("704/Cave Surface"))) {cave.Render();WorkbenchExportService.WriteRenderTexture(cave.Image,"TestResults/cave-preview.png");}
                target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(material);
            }
            var wallText=Fixture(
                new LevelItem {kind=TileKind.TouchBlue,position=new Vector2Int(1,14),size=new Vector2Int(4,4),label="26"},
                new LevelItem {kind=TileKind.TouchBlue,position=new Vector2Int(44,14),size=new Vector2Int(4,4),label="59"},
                new LevelItem {kind=TileKind.TouchBlue,position=new Vector2Int(18,34),size=new Vector2Int(4,4),label="25"},
                new LevelItem {kind=TileKind.TouchBlue,position=new Vector2Int(18,1),size=new Vector2Int(4,4),label="69"});
            try { WriteWallOrientationPreviews(wallText); }
            finally { UnityEngine.Object.DestroyImmediate(wallText); }
            using(var renderer=new GameCanvasRenderer(GameCanvasRenderer.RequiredQrScale(sample,game.Codes,1)))
            using(var gif=new AnimatedGif("TestResults/movement-preview.gif",renderer.Texture.width,renderer.Texture.height))
                for(var i=0;i<20;i++){renderer.Draw(sample,i*.1,null,game.Codes);gif.Add(renderer.Texture.GetPixels32());}
            File.WriteAllText("TestResults/production-checks.txt",string.Join("\n",passed.Select(p=>"PASS "+p))+$"\nTOTAL: {passed.Count} passed\nPhysical venue/scanners: NOT TESTED\n");
            Debug.Log($"SCANPRO_PRODUCTION_CHECKS_OK: {passed.Count} checks passed.");
        }
        [MenuItem("704/驗證/紫色長按顯示")]
        public static void RunPurpleHoldVerification()
        {
            passed.Clear();
            Directory.CreateDirectory("TestResults/PurpleHold_20261012");
            VerifyPurpleHoldFeedback();
            VerifyPurpleHoldDefaults();
            VerifyImportedPurpleFramesTransparent();
            File.WriteAllLines("TestResults/PurpleHold_20261012/purple-hold-checks.txt",
                passed.Select((check, index) => $"{index + 1:D2}. PASS  {check}")
                    .Concat(new[] { "", "Scope: isolated Unity Editor verification; no Player, scanner, TouchService, or venue hardware test." }));
            Debug.Log($"SCANPRO_PURPLE_HOLD_CHECKS_OK: {passed.Count} checks passed.");
        }
        public static void VerifyVenueScoreboardHudOnly()
        {
            passed.Clear();
            VerifyTextOrientation();
            VerifyVenueScoreboardHud();
            Directory.CreateDirectory("TestResults");
            File.WriteAllText("TestResults/venue-scoreboard-checks.txt",
                string.Join("\n", passed.Select(p => "PASS " + p)) + $"\nTOTAL: {passed.Count} passed\n");
            Debug.Log("SCANPRO_VENUE_SCOREBOARD_CHECKS_OK: " + passed.Count);
        }
        public static void VerifyCurrentChanges()
        {
            passed.Clear();
            var args = Environment.GetCommandLineArgs();
            var outputArg = Array.IndexOf(args, "--qa-dir");
            var resultDirectory = outputArg >= 0 && outputArg + 1 < args.Length
                ? args[outputArg + 1] : "TestResults/current-changes";
            VerifyTextOrientation();
            VerifyShapesAndMirroredDelete();
            VerifyRequestedRuntimeRules();
            VerifyWorkbenchAuthoringRules();
            var currentProfile = AssetDatabase.LoadAssetAtPath<OutputProfile>(PrototypeBuild.ProfilePath);
            Check(currentProfile != null && currentProfile.presentationShader != null && currentProfile.presentationShader.isSupported,
                "Current output profile includes a supported fullscreen-fit shader for the packaged player");
            var currentLevels=AssetDatabase.FindAssets("t:ScanLevel",new[]{"Assets/ScanPro/Levels"})
                .Select(g=>AssetDatabase.LoadAssetAtPath<ScanLevel>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(l=>l!=null && l.levelNumber>=1 && l.levelNumber<=5).ToArray();
            Check(currentLevels.Length==5 && currentLevels.All(ScanProWorkbench.CanEditAsset),
                "The five current formal levels are editable and saveable in the workbench and Inspector");
            var levelSix=ScriptableObject.CreateInstance<ScanLevel>(); levelSix.levelNumber=6;
            Check(!ScanProWorkbench.CanEditAsset(levelSix),"Formal levels beyond the current five remain read-only");
            UnityEngine.Object.DestroyImmediate(levelSix);
            var wallText=Fixture(
                new LevelItem {kind=TileKind.TouchBlue,position=new Vector2Int(1,14),size=new Vector2Int(4,4),label="26"},
                new LevelItem {kind=TileKind.TouchBlue,position=new Vector2Int(44,14),size=new Vector2Int(4,4),label="59"},
                new LevelItem {kind=TileKind.TouchBlue,position=new Vector2Int(18,34),size=new Vector2Int(4,4),label="25"},
                new LevelItem {kind=TileKind.TouchBlue,position=new Vector2Int(18,1),size=new Vector2Int(4,4),label="69"});
            try { WriteWallOrientationPreviews(wallText, resultDirectory); }
            finally { UnityEngine.Object.DestroyImmediate(wallText); }
            Directory.CreateDirectory(resultDirectory);
            File.WriteAllText(Path.Combine(resultDirectory, "current-change-checks.txt"),string.Join("\n",passed.Select(p=>"PASS "+p))+$"\nTOTAL: {passed.Count} passed\n");
            Debug.Log($"SCANPRO_CURRENT_CHANGES_OK: {passed.Count} checks passed; wall preview captures generated.");
        }
        static void VerifyShapesAndMirroredDelete()
        {
            var circle = new LevelItem { kind = TileKind.HazardRed, shape = TileShape.Circle,
                position = new Vector2Int(10, 10), size = new Vector2Int(4, 4) };
            Check(TileShapeUtility.Contains(circle, circle.position, new Vector2(12, 12)) &&
                  !TileShapeUtility.Contains(circle, circle.position, new Vector2(10.1f, 10.1f)),
                "Circular objects accept touches in the disc and reject their bounding-box corners");
            circle.shape = TileShape.Square;
            Check(TileShapeUtility.Contains(circle, circle.position, new Vector2(10.1f, 10.1f)),
                "Square objects retain rectangular contact coverage");

            var left = new LevelItem { kind = TileKind.HazardRed, shape = TileShape.Circle,
                position = new Vector2Int(8, 12), size = new Vector2Int(4, 4) };
            var right = new LevelItem { kind = TileKind.TouchBlue, shape = TileShape.Circle,
                position = new Vector2Int(40, 12), size = new Vector2Int(4, 4) };
            var mirrored = new[] { left, right };
            Check(ScanProWorkbench.FindMirroredDeleteIndex(mirrored, 0, 26) == -1,
                "Mirror erasing does not remove an unrelated tile kind at the reflected position");
            right.kind = left.kind;
            Check(ScanProWorkbench.FindMirroredDeleteIndex(mirrored, 0, 26) == 1,
                "Mirror erasing finds a same-kind object at the exact reflected footprint");
            right.position = new Vector2Int(39, 12);
            Check(ScanProWorkbench.FindMirroredDeleteIndex(mirrored, 0, 26) == -1,
                "Mirror erasing leaves an object outside the exact reflected footprint untouched");
            left.position = new Vector2Int(24, 12);
            Check(ScanProWorkbench.FindMirroredDeleteIndex(mirrored, 0, 26) == -1,
                "Mirror erasing does not try to delete the same object twice when it lies on the axis");

            var renderLevel = Fixture(new LevelItem { kind = TileKind.HazardRed, shape = TileShape.Circle,
                position = new Vector2Int(10, 10), size = new Vector2Int(4, 4) });
            try
            {
                using (var renderer = new GameCanvasRenderer(2))
                {
                    renderer.Draw(renderLevel, 0);
                    var center = renderer.Texture.GetPixel(240, renderer.Texture.height - 1 - 240);
                    var corner = renderer.Texture.GetPixel(201, renderer.Texture.height - 1 - 201);
                    Check(center.r > .9f && center.g < .3f && corner == Color.black,
                        "Runtime renderer paints circular tiles with transparent bounding-box corners");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(renderLevel); }

            var circularQr = new LevelItem { kind = TileKind.ScanBlue, shape = TileShape.Circle,
                position = new Vector2Int(10, 10), size = new Vector2Int(6, 6) };
            var qrLevel = Fixture(circularQr);
            try
            {
                var game = new GameSession(704);
                game.Start(qrLevel);
                using (var renderer = new GameCanvasRenderer(2))
                {
                    renderer.Draw(qrLevel, 0, game, game.Codes);
                    var renderScale = renderer.RenderScale;
                    var side = circularQr.size.x * 10 * renderScale;
                    var rgb = new byte[side * side * 3];
                    var pixels = renderer.Texture.GetPixels32();
                    var x0 = circularQr.position.x * 10 * renderScale;
                    var y0 = circularQr.position.y * 10 * renderScale;
                    for (var y = 0; y < side; y++) for (var x = 0; x < side; x++)
                    {
                        var pixel = pixels[(renderer.Texture.height - 1 - (y0 + y)) * renderer.Texture.width + x0 + x];
                        var offset = (y * side + x) * 3;
                        rgb[offset] = pixel.r; rgb[offset + 1] = pixel.g; rgb[offset + 2] = pixel.b;
                    }
                    var decoded = new BarcodeReaderGeneric().Decode(new RGBLuminanceSource(rgb, side, side,
                        RGBLuminanceSource.BitmapFormat.RGB24));
                    Check(decoded != null && decoded.Text == game.Codes[circularQr.id],
                        "Circular scan tiles keep a complete, independently decodable square QR card");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(qrLevel); }

            var calibratedVenue = ScriptableObject.CreateInstance<VenueLayout>();
            calibratedVenue.pixelsPerCell = 10; calibratedVenue.cellWidthCm = 2; calibratedVenue.cellHeightCm = 3;
            var circleArea = TileShapeUtility.BarcodeAreaCm2(calibratedVenue, circularQr);
            Check(circleArea > 0 && circleArea < calibratedVenue.AreaCm2(circularQr.size) &&
                  Mathf.Abs(circleArea - 42 * 42 * 6f / 100) < .01f,
                "Circle QR area uses its inscribed square card, not the larger circular tile bounds");
            UnityEngine.Object.DestroyImmediate(calibratedVenue);
        }

        static void VerifyMinimumQrSizes()
        {
            Check(TileLayerOrder.CreateDefaultOrder().SequenceEqual(new[]
                { TileLayer.Blue, TileLayer.SwitchCyan, TileLayer.HoldPurple, TileLayer.HazardRed, TileLayer.SafeGreen }),
                "Default layer order draws blue, cyan, purple, red, then green from bottom to top");
            var orderLevel = Fixture(Item(TileKind.TouchBlue, 10, 10), Item(TileKind.HazardRed, 14, 10),
                Item(TileKind.HoldPurple, 18, 10), Item(TileKind.SwitchCyan, 22, 10), Item(TileKind.SafeGreen, 26, 10));
            try
            {
                Check(orderLevel.OrderedItems().Select(item => item.kind).SequenceEqual(new[]
                    { TileKind.TouchBlue, TileKind.SwitchCyan, TileKind.HoldPurple, TileKind.HazardRed, TileKind.SafeGreen }),
                    "The default draw order follows the requested color stack from blue bottom to green top");
            }
            finally { UnityEngine.Object.DestroyImmediate(orderLevel); }

            var square = new LevelItem { id = "minimum-square-qr", kind = TileKind.ScanBlue,
                position = new Vector2Int(10, 12), size = new Vector2Int(2, 2) };
            var circle = new LevelItem { id = "minimum-circle-qr", kind = TileKind.ScanBlue, shape = TileShape.Circle,
                position = new Vector2Int(14, 12), size = new Vector2Int(2, 2) };
            var switchQr = new LevelItem { id = "minimum-switch-qr", kind = TileKind.SwitchCyan,
                position = new Vector2Int(18, 12), size = new Vector2Int(2, 2) };
            var animated = new LevelItem { id = "minimum-animated-qr", kind = TileKind.ScanBlue,
                position = new Vector2Int(22, 12), size = new Vector2Int(2, 2),
                animationFrames = new List<LevelMaskFrame>
                {
                    new LevelMaskFrame { width = 2, height = 2, packedPixels = Convert.ToBase64String(new byte[] { 0x0f }) }
                } };
            var level = Fixture(square, circle, switchQr, animated, Item(TileKind.TouchBlue, 28, 12));
            try
            {
                Check(!LevelValidation.Errors(level).Any(error => error.Contains("QR 區域")),
                    "Square, circular, switch, and animated QR objects accept a complete 2×2 active area");
                level.importWarnings = "舊版 QR 至少 4×4 格警告\n來源 yellow 資料夾未支援";
                Check(ScanProWorkbench.ImportWarningsForDisplay(level) == "來源 yellow 資料夾未支援",
                    "Import display removes obsolete QR-size warnings while preserving unrelated source warnings");
                square.size = new Vector2Int(1, 2);
                var currentWarnings = ScanProWorkbench.ImportWarningsForDisplay(level);
                Check(currentWarnings.Contains("QR 區域至少 2×2 格") && !currentWarnings.Contains("4×4"),
                    "Import display recomputes and shows the current QR minimum warning");
                square.size = new Vector2Int(2, 2);
                foreach (var size in new[] { new Vector2Int(1, 1), new Vector2Int(1, 2), new Vector2Int(2, 1) })
                {
                    square.size = size;
                    Check(LevelValidation.ContentErrors(square).Any(error => error.Contains("QR 區域")),
                        $"A static {size.x}×{size.y} QR fails the minimum-size check");
                }
                square.size = new Vector2Int(2, 2);

                var shortFrame = new LevelItem { id = "short-animated-qr", kind = TileKind.ScanBlue,
                    position = new Vector2Int(24, 12), size = new Vector2Int(2, 2),
                    animationFrames = new List<LevelMaskFrame>
                    {
                        new LevelMaskFrame { width = 2, height = 2, packedPixels = Convert.ToBase64String(new byte[] { 0x03 }) }
                    } };
                Check(LevelValidation.ContentErrors(shortFrame).Any(error => error.Contains("QR 區域")),
                    "An animated QR frame below 2×2 is rejected even when the item union is 2×2");

                var inspectorLevel = ScriptableObject.CreateInstance<ScanLevel>();
                inspectorLevel.venue = level.venue;
                inspectorLevel.items.Add(new LevelItem { id = "inspector-animated-qr", kind = TileKind.ScanBlue,
                    position = new Vector2Int(24, 12), size = new Vector2Int(2, 2),
                    animationFrames = new List<LevelMaskFrame>
                    {
                        new LevelMaskFrame { width = 2, height = 2, packedPixels = Convert.ToBase64String(new byte[] { 0x0f }) }
                    } });
                try
                {
                    var serialized = new SerializedObject(inspectorLevel);
                    serialized.Update();
                    serialized.FindProperty("items").GetArrayElementAtIndex(0)
                        .FindPropertyRelative("animationFrames").GetArrayElementAtIndex(0)
                        .FindPropertyRelative("width").intValue = 1;
                    Check(!LevelPlacementGuard.Apply(serialized, out var guardMessage) && guardMessage.Contains("QR 區域"),
                        "The native Inspector guard rejects animated QR frames below the 2×2 minimum");
                }
                finally { UnityEngine.Object.DestroyImmediate(inspectorLevel); }

                var game = new GameSession(704);
                game.Start(level);
                var squareCodes = new Dictionary<string, string> { [square.id] = game.Codes[square.id] };
                var circleCodes = new Dictionary<string, string> { [circle.id] = game.Codes[circle.id] };
                Check(GameCanvasRenderer.RequiredQrScale(level, squareCodes, 2) >= 5 &&
                      GameCanvasRenderer.RequiredQrScale(level, circleCodes, 2) >= 7,
                    "The renderer raises minimum 2×2 square and circle QR output to preserve at least three pixels per module");
                using (var renderer = new GameCanvasRenderer(2))
                {
                    renderer.Draw(level, 0, game, game.Codes);
                    Check(renderer.RenderScale >= 7 && DecodeRenderedQr(renderer, square) == game.Codes[square.id] &&
                          DecodeRenderedQr(renderer, circle) == game.Codes[circle.id] &&
                          DecodeRenderedQr(renderer, switchQr) == game.Codes[switchQr.id] &&
                          DecodeRenderedQr(renderer, animated) == game.Codes[animated.id],
                        "Independent ZXing decoding succeeds for crisp square, circle, switch, and animated 2×2 QR targets");
                    renderer.Draw(level, 0, game, game.Codes, textOrientation: CanvasTextOrientation.VenueOutput);
                    Check(DecodeVenueOutputQr(renderer, square) == game.Codes[square.id],
                        "Independent ZXing decoding succeeds for a minimum-size QR after venue-atlas mapping");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(level); }
        }

        static string DecodeRenderedQr(GameCanvasRenderer renderer, LevelItem item)
        {
            var frameIndex = item.AnimationFrameIndex(0, 350);
            if (!item.TryGetQrContentBounds(item.position, frameIndex, out var bounds)) return null;
            // Decode the complete visible content area, including its background.
            // Do not duplicate the renderer's QR matrix placement to crop it away.
            var width = Mathf.RoundToInt(bounds.width * 10) * renderer.RenderScale;
            var height = Mathf.RoundToInt(bounds.height * 10) * renderer.RenderScale;
            var originX = Mathf.RoundToInt(bounds.x * 10) * renderer.RenderScale;
            var originY = Mathf.RoundToInt(bounds.y * 10) * renderer.RenderScale;
            var pixels = renderer.Texture.GetPixels32();
            var rgb = new byte[width * height * 3];
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            {
                var pixel = pixels[(renderer.Texture.height - 1 - originY - y) * renderer.Texture.width + originX + x];
                var offset = (y * width + x) * 3;
                rgb[offset] = pixel.r; rgb[offset + 1] = pixel.g; rgb[offset + 2] = pixel.b;
            }
            return DecodeQrAtAnyQuarterTurn(rgb, width, height);
        }

        static string DecodeVenueOutputQr(GameCanvasRenderer renderer, LevelItem item)
        {
            const float outputScale = 6.4f;
            var sourceX = item.position.x * 10;
            var sourceY = item.position.y * 10;
            var sourceWidth = item.size.x * 10;
            var sourceHeight = item.size.y * 10;
            var atlasRect = new RectInt(
                Mathf.RoundToInt((420 - sourceY - sourceHeight) * outputScale),
                Mathf.RoundToInt(sourceX * outputScale),
                Mathf.RoundToInt(sourceHeight * outputScale),
                Mathf.RoundToInt(sourceWidth * outputScale));
            var material = new Material(Shader.Find("704/Output Atlas"));
            var target = new RenderTexture(2688, 3840, 0, RenderTextureFormat.ARGB32);
            var capture = new Texture2D(2688, 3840, TextureFormat.RGBA32, false);
            target.Create();
            try
            {
                OutputCompositor.Blit(renderer.Texture, target, material);
                var previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    capture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                    capture.Apply(false);
                }
                finally { RenderTexture.active = previous; }
                var image = capture.GetPixels32();
                var rgb = new byte[atlasRect.width * atlasRect.height * 3];
                var previewPixels = new Color32[atlasRect.width * atlasRect.height];
                for (var y = 0; y < atlasRect.height; y++) for (var x = 0; x < atlasRect.width; x++)
                {
                    var pixel = image[(capture.height - 1 - atlasRect.y - y) * capture.width + atlasRect.x + x];
                    var offset = (y * atlasRect.width + x) * 3;
                    rgb[offset] = pixel.r; rgb[offset + 1] = pixel.g; rgb[offset + 2] = pixel.b;
                    previewPixels[(atlasRect.height - 1 - y) * atlasRect.width + x] = pixel;
                }
                var qrPreview = new Texture2D(atlasRect.width, atlasRect.height, TextureFormat.RGBA32, false);
                try
                {
                    qrPreview.SetPixels32(previewPixels); qrPreview.Apply(false);
                    var args = Environment.GetCommandLineArgs();
                    var qaIndex = Array.IndexOf(args, "--qa-dir");
                    var qaDirectory = qaIndex >= 0 && qaIndex + 1 < args.Length ? args[qaIndex + 1] : "TestResults";
                    Directory.CreateDirectory(qaDirectory);
                    File.WriteAllBytes(Path.Combine(qaDirectory, "minimum-2x2-qr-venue-output.png"), qrPreview.EncodeToPNG());
                }
                finally { UnityEngine.Object.DestroyImmediate(qrPreview); }
                return DecodeQrAtAnyQuarterTurn(rgb, atlasRect.width, atlasRect.height);
            }
            finally
            {
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(capture);
            }
        }

        static string DecodeQrAtAnyQuarterTurn(byte[] pixels, int width, int height)
        {
            var reader = new BarcodeReaderGeneric(); var current = pixels;
            var currentWidth = width; var currentHeight = height;
            for (var turn = 0; turn < 4; turn++)
            {
                var result = reader.Decode(new RGBLuminanceSource(current, currentWidth, currentHeight,
                    RGBLuminanceSource.BitmapFormat.RGB24));
                if (result != null) return result.Text;
                var rotated = new byte[current.Length]; var nextWidth = currentHeight; var nextHeight = currentWidth;
                for (var y = 0; y < currentHeight; y++) for (var x = 0; x < currentWidth; x++)
                {
                    var sourceIndex = (y * currentWidth + x) * 3;
                    var nextX = currentHeight - 1 - y; var nextY = x;
                    var destination = (nextY * nextWidth + nextX) * 3;
                    rotated[destination] = current[sourceIndex];
                    rotated[destination + 1] = current[sourceIndex + 1];
                    rotated[destination + 2] = current[sourceIndex + 2];
                }
                current = rotated; currentWidth = nextWidth; currentHeight = nextHeight;
            }
            return null;
        }

        static void VerifyRequestedRuntimeRules()
        {
            var movement = new LevelItem
            {
                kind = TileKind.TouchBlue, position = new Vector2Int(10, 10), size = Vector2Int.one,
                startHoldSeconds = .5f
            };
            movement.waypoints.Add(new Vector2Int(20, 10));
            movement.waypointHoldSeconds.Add(2f);
            Check(movement.PositionAt(.25, 1000) == new Vector2(10, 10) &&
                  movement.PositionAt(1, 1000) == new Vector2(15, 10) &&
                  movement.PositionAt(2.5, 1000) == new Vector2(20, 10),
                "Path movement supports separate start/waypoint dwell times while keeping the segment speed fixed");
            Check(PathTimingEditor.FramesForSeconds(1.25f, 24) == 30 &&
                  Mathf.Approximately(PathTimingEditor.SecondsForFrames(30, 24), 1.25f),
                "Path dwell input converts seconds and frame counts using the level FPS setting");

            var blue = Item(TileKind.TouchBlue, 20, 20); blue.label = "1";
            var red = Item(TileKind.HazardRed, 20, 20);
            var spare = Item(TileKind.TouchBlue, 12, 12); spare.label = "2";
            var layered = Fixture(blue, red, spare);
            layered.layerOrder = new List<TileLayer> { TileLayer.Blue, TileLayer.SafeGreen, TileLayer.HazardRed, TileLayer.HoldPurple, TileLayer.SwitchCyan };
            var game = new GameSession(); game.Start(layered);
            game.Tick(.01, new[] { new GameTouch(1, new Vector2(20.5f, 20.5f)) });
            Check(game.Hearts == 14 && !game.States[blue.id].Completed,
                "A red top layer deducts one heart and blocks completion of the hidden blue tile");

            var lockLevel = Fixture(Item(TileKind.HazardRed, 20, 20), Item(TileKind.TouchBlue, 12, 12));
            game.Start(lockLevel);
            var lockedCell = new Vector2Int(20, 20);
            game.Controls.SetLock(lockedCell, true);
            var held = new[] { new GameTouch(7, new Vector2(20.5f, 20.5f)) };
            game.Tick(.01, held);
            Check(game.Hearts == 15 && game.Controls.IsLocked(lockedCell),
                "A locked venue cell suppresses sensor touches before red-hit evaluation");
            game.Controls.SetLock(lockedCell, false);
            game.Tick(.01, held);
            Check(game.Hearts == 15,
                "Unlocking a cell under an existing touch does not reactivate that touch until it lifts");
            game.Tick(.01, Array.Empty<GameTouch>()); game.Tick(.01, held);
            Check(game.Hearts == 14, "A fresh touch after lifting is accepted after the cell unlocks");

            var hazardLevel = Fixture(Item(TileKind.HazardRed, 20, 20), Item(TileKind.TouchBlue, 12, 12));
            game.Start(hazardLevel); game.Controls.Invulnerable = true;
            game.Tick(.01, held);
            Check(game.Hearts == 15, "Invulnerability prevents red contact damage");
            game.Controls.Invulnerable = false;
            game.Tick(.01, held);
            Check(game.Hearts == 15, "Turning invulnerability off does not retroactively damage a held red contact");
            game.Tick(.01, Array.Empty<GameTouch>()); game.Tick(.01, held);
            Check(game.Hearts == 14, "A new red contact after invulnerability is disabled deducts a heart");

            var numbers = Fixture(
                new LevelItem { kind = TileKind.TouchBlue, position = new Vector2Int(10, 10), size = new Vector2Int(4, 4), label = "1" },
                new LevelItem { kind = TileKind.TouchBlue, position = new Vector2Int(20, 10), size = new Vector2Int(4, 4), label = "2" },
                new LevelItem { kind = TileKind.TouchBlue, position = new Vector2Int(30, 10), size = new Vector2Int(4, 4), label = "3" });
            game.Start(numbers);
            var ordered = numbers.items.OrderBy(i => TouchNumberContent.TryGetAnswer(i, out var answer) ? answer : int.MaxValue).ToArray();
            game.Tick(.01, new[] { new GameTouch(20, new Vector2(21, 11)) });
            Check(!game.States[ordered[1].id].Completed && game.Remaining == 3,
                "Legacy Free numeric-blue settings still ignore a wrong first touch without side effects");
            game.Tick(.01, Array.Empty<GameTouch>());
            game.Tick(.01, new[] { new GameTouch(21, new Vector2(11, 11)) });
            game.Tick(.01, Array.Empty<GameTouch>());
            game.Tick(.01, new[] { new GameTouch(22, new Vector2(31, 11)) });
            Check(!game.States[ordered[2].id].Completed,
                "Every numeric blue tile waits for the smallest remaining answer, regardless of legacy ordering mode");

            var proA = Item(TileKind.TouchBlue, 10, 20); var proB = Item(TileKind.TouchBlue, 30, 20);
            proA.proTarget = proB.proTarget = true;
            var proLevel = Fixture(proA, proB); proLevel.proMechanic = ProMechanic.Numbers7; game.Start(proLevel);
            var proOrder = game.Pro.Questions.Values.OrderBy(question => question.Answer).ToArray();
            var secondItem = game.States[proOrder[1].Id].Item;
            game.Tick(.01, new[] { new GameTouch(30, game.Position(secondItem) + (Vector2)secondItem.size * .5f) });
            Check(!game.States[secondItem.id].Completed,
                "Pro numeric blue targets also ignore out-of-order touches");

            var countdownItem = Item(TileKind.TouchBlue, 12, 12); countdownItem.proTarget = true;
            var countdownLevel = Fixture(countdownItem); countdownLevel.proMechanic = ProMechanic.Countdown;
            game.Start(countdownLevel); game.Controls.Invulnerable = true;
            game.Tick(31, Array.Empty<GameTouch>());
            game.Controls.Invulnerable = false; game.Tick(.01, Array.Empty<GameTouch>());
            Check(game.Hearts == 15 && !game.Outcome.HasValue,
                "Invulnerability suppresses Pro countdown heart loss but leaves the level timer and game state active");
            game.Tick(countdownLevel.timeLimitSeconds, Array.Empty<GameTouch>());
            Check(game.Outcome == LevelOutcome.Timeout,
                "Invulnerability does not cancel the level-wide timeout");
        }

        static void VerifyWorkbenchAuthoringRules()
        {
            var newLevel = ScriptableObject.CreateInstance<ScanLevel>();
            Check(newLevel.initialHearts == 15, "New level assets default to fifteen initial hearts");
            UnityEngine.Object.DestroyImmediate(newLevel);

            var rectangle = WorkbenchShapeTool.DragBounds(new Vector2Int(20, 20), new Vector2Int(12, 14), TileShape.Rectangle);
            var square = WorkbenchShapeTool.DragBounds(new Vector2Int(20, 20), new Vector2Int(12, 14), TileShape.Square);
            var circle = WorkbenchShapeTool.DragBounds(new Vector2Int(20, 20), new Vector2Int(12, 14), TileShape.Circle);
            Check(rectangle.position == new Vector2Int(12, 14) && rectangle.size == new Vector2Int(9, 7),
                "Rectangle drag creates one normalized area matching both pointer endpoints");
            Check(square.size == new Vector2Int(9, 9) && circle.size == new Vector2Int(9, 9),
                "Square and circle drag retain equal dimensions from either drag direction");

            var legacy = new[]
            {
                Item(TileKind.HazardRed, 20, 20), Item(TileKind.TouchBlue, 10, 10), Item(TileKind.SafeGreen, 15, 15)
            };
            Check(TileLayerOrder.OrderedItems(legacy, null).Select(item => item.kind).SequenceEqual(
                new[] { TileKind.TouchBlue, TileKind.HazardRed, TileKind.SafeGreen }),
                "Legacy levels use the fixed template even without layer data");
            var explicitOrder = TileLayerOrder.OrderedItems(legacy,
                new[] { TileLayer.Blue, TileLayer.SafeGreen, TileLayer.HazardRed });
            Check(explicitOrder.Select(item => item.kind).SequenceEqual(new[] { TileKind.TouchBlue, TileKind.HazardRed, TileKind.SafeGreen }),
                "Stored custom order cannot override the fixed template");
            Check(TileLayerOrder.CompleteOrder(new[] { TileLayer.HazardRed, TileLayer.HazardRed }).Distinct().Count() == 5,
                "Incomplete or duplicated layer data resolves to all five unique layer controls");

            var mirroredPath = Item(TileKind.TouchBlue, 10, 10);
            mirroredPath.waypoints.Add(new Vector2Int(16, 12));
            mirroredPath.waypointHoldSeconds.Add(.75f);
            var variants = WorkbenchMirrorUtility.Variants(mirroredPath, true, true, 26, 21);
            Check(variants.Count == 3 && variants.Any(item => item.position == new Vector2Int(38, 10)) &&
                  variants.Any(item => item.position == new Vector2Int(10, 28)) &&
                  variants.Any(item => item.position == new Vector2Int(38, 28)) &&
                  variants.All(item => item.waypoints.Count == 1 && Mathf.Approximately(item.waypointHoldSeconds[0], .75f)),
                "Vertical, horizontal, and combined mirrors copy waypoint geometry and dwell timing");
        }
        static void VerifyPurpleHoldFeedback()
        {
            var red = Item(TileKind.HazardRed, 20, 20, "purple-shared");
            var underlying = Item(TileKind.TouchBlue, 20, 20);
            var circle = Item(TileKind.HoldPurple, 12, 20);
            circle.shape = TileShape.Circle;
            circle.label = "hidden label";
            circle.targetGroups.Add("purple-shared");
            circle.purpleClearMask = ClearMask(new Vector2Int(20, 20));
            var second = Item(TileKind.HoldPurple, 30, 20);
            second.label = "hidden label";
            second.targetGroups.Add("purple-shared");
            second.purpleClearMask = ClearMask(new Vector2Int(20, 20));
            var level = Fixture(red, circle, second, underlying, Item(TileKind.TouchBlue, 12, 12));
            var game = new GameSession();
            game.Start(level);
            Check(Mathf.Approximately(level.purpleHoldSeconds, .1f),
                "New levels use a 0.1-second purple hold threshold");

            var firstTouch = new GameTouch(1, (Vector2)circle.position + new Vector2(2, 2));
            var secondTouch = new GameTouch(2, (Vector2)second.position + new Vector2(2, 2));
            var circleState = game.States[circle.id];
            var secondState = game.States[second.id];
            var purpleColor = (Color32)TilePalette.ColorOf(TileKind.HoldPurple);
            var circleBounds = new RectInt(circle.position.x * 10, circle.position.y * 10,
                circle.size.x * 10, circle.size.y * 10);
            var secondBounds = new RectInt(second.position.x * 10, second.position.y * 10,
                second.size.x * 10, second.size.y * 10);
            using (var renderer = new GameCanvasRenderer(1))
            {
                game.Tick(.01, new[] { firstTouch });
                game.Tick(.099, new[] { firstTouch });
                renderer.Draw(level, game.Elapsed, game);
                Check(!circleState.PurpleHoldActive && !game.States[red.id].Disabled,
                    "A purple contact below 0.1 seconds does not activate the linked group");
                Check(CanvasPixel(renderer.Texture, circle.position.x * 10 + 20,
                          circle.position.y * 10 + 20, renderer.RenderScale).Equals(purpleColor) &&
                      CanvasPixel(renderer.Texture, circle.position.x * 10, circle.position.y * 10,
                          renderer.RenderScale).Equals(new Color32(0, 0, 0, 255)),
                    "An inactive circular purple region preserves its original outline");
                Check(CanvasRegionHasColor(renderer.Texture, secondBounds, renderer.RenderScale, purpleColor),
                    "An inactive purple region hides its label and contains only purple pixels");

                game.Tick(.002, new[] { firstTouch });
                renderer.Draw(level, game.Elapsed, game);
                Check(circleState.PurpleHoldActive && !secondState.PurpleHoldActive &&
                      !game.States[red.id].Disabled && game.IsPurpleClearedAt(red, new Vector2(20.5f, 20.5f)),
                    "A purple contact activates at 0.1 seconds and clears only the linked mask cells");
                var blueColor = (Color32)TilePalette.ColorOf(TileKind.TouchBlue);
                Check(CanvasPixel(renderer.Texture, red.position.x * 10 + 5, red.position.y * 10 + 5,
                          renderer.RenderScale).Equals(blueColor),
                    "Only the purple-mask overlap becomes transparent and reveals the blue layer below");
                Check(CanvasPixel(renderer.Texture, circle.position.x * 10 + 20,
                          circle.position.y * 10 + 20, renderer.RenderScale).Equals(new Color32(255, 255, 255, 255)) &&
                      CanvasPixel(renderer.Texture, circle.position.x * 10, circle.position.y * 10,
                          renderer.RenderScale).Equals(new Color32(0, 0, 0, 255)),
                    "Holding a circular purple region turns only its actual shape white");

                game.Tick(.01, new[] { firstTouch, secondTouch });
                game.Tick(.002, new[] { firstTouch, secondTouch });
                Check(circleState.PurpleHoldActive && !secondState.PurpleHoldActive,
                    "A newly added touch has an independent purple hold timer");
                game.Tick(.01, new[] { secondTouch });
                Check(!circleState.PurpleHoldActive && !secondState.PurpleHoldActive &&
                      !game.States[red.id].Disabled && !game.IsPurpleClearedAt(red, new Vector2(20.5f, 20.5f)),
                    "Releasing a mature touch restores its clear mask while another touch is below threshold");
                game.Tick(.09, new[] { secondTouch });
                Check(secondState.PurpleHoldActive && game.IsPurpleClearedAt(red, new Vector2(20.5f, 20.5f)),
                    "A continuously held second purple region activates after its own threshold");
                game.Tick(.001, new[] { firstTouch, secondTouch });
                game.Tick(.101, new[] { firstTouch, secondTouch });
                Check(circleState.PurpleHoldActive && secondState.PurpleHoldActive,
                    "Separate purple regions can activate together");
                game.Tick(.01, new[] { firstTouch });
                Check(circleState.PurpleHoldActive && !secondState.PurpleHoldActive &&
                      game.IsPurpleClearedAt(red, new Vector2(20.5f, 20.5f)),
                    "Releasing one purple region preserves another active region's clear mask");
                game.Tick(.01, Array.Empty<GameTouch>());
                Check(!circleState.PurpleHoldActive && !secondState.PurpleHoldActive &&
                      !game.IsPurpleClearedAt(red, new Vector2(20.5f, 20.5f)),
                    "Releasing all purple contacts restores the previously masked red cells");

                game.Tick(.01, new[] { firstTouch });
                game.Tick(.05, new[] { firstTouch });
                game.Tick(.01, Array.Empty<GameTouch>());
                game.Tick(.01, new[] { firstTouch });
                game.Tick(.05, new[] { firstTouch });
                Check(!circleState.PurpleHoldActive,
                    "A released short touch does not accumulate time toward the next hold");
                game.Tick(.051, new[] { firstTouch });
                Check(circleState.PurpleHoldActive,
                    "A new continuous hold can activate after its own 0.1-second threshold");
                game.Start(level);
                Check(!game.States[circle.id].PurpleHoldActive && !game.States[second.id].PurpleHoldActive &&
                      !game.States[red.id].Disabled,
                    "Restarting a level resets purple visuals and linked groups");
            }
            UnityEngine.Object.DestroyImmediate(level);
            VerifyAnimatedPurpleShape();
            VerifyPurplePartialFramesTransparent();
            VerifyPurpleTransparentHitPassThrough();
        }

        static void VerifyPurplePartialFramesTransparent()
        {
            var red = Item(TileKind.HazardRed, 24, 15, "purple-frames");
            red.size = new Vector2Int(4, 4);
            red.animationFrames.Add(new LevelMaskFrame
                { offset = Vector2Int.zero, width = 2, height = 1, packedPixels = Convert.ToBase64String(new byte[] { 0x03 }) });
            red.animationFrames.Add(new LevelMaskFrame
                { offset = new Vector2Int(1, 2), width = 2, height = 1, packedPixels = Convert.ToBase64String(new byte[] { 0x03 }) });
            var purple = Item(TileKind.HoldPurple, 12, 12);
            purple.targetGroups.Add("purple-frames");
            purple.purpleClearMask = ClearMask(new Vector2Int(24, 15), new Vector2Int(25, 17));
            var blue = Item(TileKind.TouchBlue, 24, 15);
            blue.size = new Vector2Int(4, 4);
            var level = Fixture(red, purple, blue);
            var game = new GameSession();
            game.Start(level);
            var touch = new GameTouch(91, new Vector2(13, 13));
            var redColor = (Color32)TilePalette.ColorOf(TileKind.HazardRed);
            var blueColor = (Color32)TilePalette.ColorOf(TileKind.TouchBlue);
            using (var renderer = new GameCanvasRenderer(1))
            {
                renderer.Draw(level, 0, game);
                Check(CanvasPixel(renderer.Texture, 245, 155, renderer.RenderScale).Equals(redColor),
                    "An enabled frames component is visible on its first animation frame");
                renderer.Draw(level, .35, game);
                Check(CanvasPixel(renderer.Texture, 255, 175, renderer.RenderScale).Equals(redColor),
                    "An enabled frames component is visible on its next animation frame");

                game.Tick(.01, new[] { touch });
                game.Tick(.101, new[] { touch });
                Check(!game.States[red.id].Disabled && game.IsPurpleClearTarget(red) &&
                      game.IsPurpleClearedAt(red, new Vector2(24.5f, 15.5f)) &&
                      !game.IsPurpleClearedAt(red, new Vector2(25.5f, 15.5f)),
                    "A purple hold masks only the clearwrong cells inside its linked animated frames group");
                renderer.Draw(level, 0, game);
                Check(CanvasPixel(renderer.Texture, 245, 155, renderer.RenderScale).Equals(blueColor),
                    "Frame-one artwork under clearwrong becomes transparent and reveals the underlying layer");
                Check(CanvasPixel(renderer.Texture, 255, 155, renderer.RenderScale).Equals(redColor),
                    "Frame-one artwork outside clearwrong remains visible");
                renderer.Draw(level, .35, game);
                Check(CanvasPixel(renderer.Texture, 255, 175, renderer.RenderScale).Equals(blueColor),
                    "Frame-two artwork under clearwrong becomes transparent across animation changes");
                Check(CanvasPixel(renderer.Texture, 265, 175, renderer.RenderScale).Equals(redColor),
                    "Frame-two artwork outside clearwrong remains visible");

                game.Tick(.01, Array.Empty<GameTouch>());
                renderer.Draw(level, .35, game);
                Check(!game.States[red.id].Disabled && !game.IsPurpleClearTarget(red) &&
                      CanvasPixel(renderer.Texture, 255, 175, renderer.RenderScale).Equals(redColor),
                    "Releasing the purple hold restores both cleared and uncovered frame artwork");
            }
            UnityEngine.Object.DestroyImmediate(level);
        }

        static void VerifyPurpleTransparentHitPassThrough()
        {
            var red = Item(TileKind.HazardRed, 24, 15, "purple-pass-through");
            red.size = new Vector2Int(2, 1);
            var blue = Item(TileKind.TouchBlue, 24, 15);
            blue.size = new Vector2Int(2, 1);
            var purple = Item(TileKind.HoldPurple, 12, 12);
            purple.targetGroups.Add("purple-pass-through");
            purple.purpleClearMask = ClearMask(new Vector2Int(24, 15));
            var level = Fixture(red, blue, purple);
            var game = new GameSession();
            game.Start(level);
            var purpleTouch = new GameTouch(101, new Vector2(13, 13));
            var blueTouch = new GameTouch(102, new Vector2(24.5f, 15.5f));
            var redTouch = new GameTouch(103, new Vector2(25.5f, 15.5f));
            game.Tick(.01, new[] { purpleTouch });
            game.Tick(.101, new[] { purpleTouch });
            Check(game.IsPurpleClearedAt(red, blueTouch.CellPosition) &&
                  !game.IsPurpleClearedAt(red, redTouch.CellPosition) && !game.States[red.id].Disabled,
                "Only the clear-mask overlap is transparent; the remaining red region stays active");
            game.Tick(.01, new[] { purpleTouch, blueTouch, redTouch });
            Check(game.States[blue.id].Completed && game.Hearts == GameSession.MaximumHearts - 1,
                "Touch passes through the cleared portion to the blue target while uncovered red still deducts a heart");
            UnityEngine.Object.DestroyImmediate(level);
        }

        static void VerifyImportedPurpleFramesTransparent()
        {
            var imported = AssetDatabase.LoadAssetAtPath<ScanLevel>("Assets/ScanPro/LevelDrafts/Imported_1-3 (100ms).asset");
            Check(imported != null, "The imported 1-3 draft is available for purple-frame transparency verification");
            var frameGroups = imported.items.Where(item => item.kind == TileKind.HoldPurple)
                .SelectMany(item => item.targetGroups)
                .Where(group => group.StartsWith("import:frames:", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var targetFrames = imported.items.Where(item => item.kind == TileKind.HazardRed &&
                frameGroups.Contains(item.group, StringComparer.OrdinalIgnoreCase) && item.HasAnimation).ToArray();
            Check(frameGroups.Length > 0 && targetFrames.Length > 0,
                $"The imported purple masks link to {targetFrames.Length} animated frames groups");

            const double activationTime = .111;
            var probeCell = (from y in Enumerable.Range(0, imported.venue.Height)
                             from x in Enumerable.Range(0, imported.venue.Width)
                             let point = new Vector2(x + .5f, y + .5f)
                             where !imported.items.Any(item => TileShapeUtility.Contains(item,
                                 item.PositionAt(activationTime, imported.movementSegmentMs), point,
                                 item.AnimationFrameIndex(activationTime, imported.animationSpeedMilliseconds)))
                             select new Vector2Int(x, y)).First();
            var probe = Item(TileKind.HoldPurple, probeCell.x, probeCell.y);
            probe.size = Vector2Int.one;
            probe.targetGroups.AddRange(frameGroups);
            var activeItems = imported.items.Concat(new[] { probe }).ToArray();
            var activeLevel = Fixture(activeItems);
            activeLevel.animationSpeedMilliseconds = imported.animationSpeedMilliseconds;
            activeLevel.movementSegmentMs = imported.movementSegmentMs;
            activeLevel.purpleHoldSeconds = imported.purpleHoldSeconds;
            var activeGame = new GameSession();
            var referenceGame = new GameSession();
            activeGame.Start(activeLevel);
            referenceGame.Start(activeLevel);
            var touch = new GameTouch(92, new Vector2(probeCell.x + .5f, probeCell.y + .5f));
            activeGame.Tick(.01, new[] { touch });
            activeGame.Tick(.101, new[] { touch });

            try
            {
                using (var activeRenderer = new GameCanvasRenderer(1))
                using (var referenceRenderer = new GameCanvasRenderer(1))
                {
                    foreach (var item in targetFrames)
                    {
                        Check(!activeGame.States[item.id].Disabled && activeGame.IsPurpleClearTarget(item),
                            "Purple hold keeps imported frames group active outside its local clear mask " + item.group);
                        for (var frameIndex = 0; frameIndex < item.animationFrames.Count; frameIndex++)
                        {
                            var frame = item.animationFrames[frameIndex];
                            var seconds = frameIndex * Math.Max(1, imported.animationSpeedMilliseconds) / 1000.0;
                            activeRenderer.Draw(activeLevel, seconds, activeGame, activeGame.Codes);
                            referenceRenderer.Draw(activeLevel, seconds, referenceGame, referenceGame.Codes);
                            var activePixels = activeRenderer.Texture.GetPixels32();
                            var referencePixels = referenceRenderer.Texture.GetPixels32();
                            var width = activeRenderer.Texture.width;
                            var height = activeRenderer.Texture.height;
                            var scale = activeRenderer.RenderScale;
                            var mask = frame.Pixels;
                            var visibleCellCount = 0;
                            var preservedOutsideLegacyMask = true;
                            for (var my = 0; my < frame.height; my++)
                                for (var mx = 0; mx < frame.width; mx++)
                                {
                                    if (!mask[my * frame.width + mx]) continue;
                                    visibleCellCount++;
                                    var canvasX = (item.position.x + frame.offset.x + mx) * 10 + 5;
                                    var canvasY = (item.position.y + frame.offset.y + my) * 10 + 5;
                                    var pixelX = canvasX * scale;
                                    var pixelY = height - 1 - canvasY * scale;
                                    var pixelIndex = pixelY * width + pixelX;
                                    if (!activePixels[pixelIndex].Equals(referencePixels[pixelIndex]))
                                        preservedOutsideLegacyMask = false;
                                }
                            Check(visibleCellCount > 0 && preservedOutsideLegacyMask,
                                $"Legacy imported frame group {item.group}, frame {frameIndex + 1}, remains visible away from its purple artwork");
                        }
                    }
                    activeRenderer.Draw(activeLevel, .25, activeGame, activeGame.Codes);
                    File.WriteAllBytes("TestResults/PurpleHold_20261012/imported-1-3-purple-transparent.png",
                        activeRenderer.Texture.EncodeToPNG());
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(activeLevel);
            }
        }

        static void VerifyAnimatedPurpleShape()
        {
            var purple = Item(TileKind.HoldPurple, 38, 20);
            purple.label = "hidden animation label";
            purple.animationFrames.Add(new LevelMaskFrame
                { offset = new Vector2Int(1, 1), width = 2, height = 2, packedPixels = Convert.ToBase64String(new byte[] { 0x0f }) });
            purple.animationFrames.Add(new LevelMaskFrame
                { offset = new Vector2Int(2, 0), width = 1, height = 2, packedPixels = Convert.ToBase64String(new byte[] { 0x03 }) });
            var level = Fixture(purple, Item(TileKind.TouchBlue, 12, 12));
            var game = new GameSession();
            game.Start(level);
            var touch = new GameTouch(7, (Vector2)purple.position + new Vector2(1.5f, 1.5f));
            game.Tick(.01, new[] { touch });
            game.Tick(.101, new[] { touch });
            Check(game.States[purple.id].PurpleHoldActive,
                "An animated purple mask can trigger its hold using the current frame's hit area");
            using (var renderer = new GameCanvasRenderer(1))
            {
                renderer.Draw(level, game.Elapsed, game);
                Check(CanvasRegionHasColor(renderer.Texture, new RectInt(390, 210, 20, 20), renderer.RenderScale,
                          new Color32(255, 255, 255, 255)) &&
                      CanvasPixel(renderer.Texture, 380, 200, renderer.RenderScale).Equals(new Color32(0, 0, 0, 255)),
                    "The first animated purple mask turns white only across its visible cells");
                renderer.Draw(level, .36, game);
                Check(CanvasRegionHasColor(renderer.Texture, new RectInt(400, 200, 10, 20), renderer.RenderScale,
                          new Color32(255, 255, 255, 255)) &&
                      CanvasPixel(renderer.Texture, 400, 220, renderer.RenderScale).Equals(new Color32(0, 0, 0, 255)),
                    "The next animated purple frame keeps its own mask, position, and hidden label");
            }
            UnityEngine.Object.DestroyImmediate(level);
        }

        static void VerifyPurpleHoldDefaults()
        {
            var drafts = AssetDatabase.FindAssets("t:ScanLevel", new[] { "Assets/ScanPro/LevelDrafts" })
                .Select(guid => AssetDatabase.LoadAssetAtPath<ScanLevel>(AssetDatabase.GUIDToAssetPath(guid)))
                .ToArray();
            Check(drafts.Length > 0 && drafts.All(level => Mathf.Approximately(level.purpleHoldSeconds, .1f)),
                "All existing imported drafts use the 0.1-second purple hold threshold");

            var venue = AssetDatabase.LoadAssetAtPath<VenueLayout>("Assets/ScanPro/Data/704_Venue.asset");
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var fixturePath = Path.Combine(projectRoot, "TestResults", "AsepriteImport_20261008",
                "ScanPro_Importer_Fixture (350ms).zip");
            var imported = AsepriteZipImporter.Read(fixturePath, venue);
            try
            {
                Check(imported.Succeeded && Mathf.Approximately(imported.Level.purpleHoldSeconds, .1f),
                    "New ZIP imports inherit the 0.1-second purple hold threshold");
            }
            finally
            {
                if (imported.Level != null) UnityEngine.Object.DestroyImmediate(imported.Level);
            }
        }

        static bool CanvasRegionHasColor(Texture2D texture, RectInt region, int scale, Color32 expected)
        {
            var pixels = texture.GetPixels32();
            var x0 = region.x * scale; var x1 = region.xMax * scale;
            var y0 = texture.height - region.yMax * scale; var y1 = texture.height - region.y * scale;
            for (var y = y0; y < y1; y++) for (var x = x0; x < x1; x++)
                if (!pixels[y * texture.width + x].Equals(expected)) return false;
            return true;
        }

        static Color32 CanvasPixel(Texture2D texture, int x, int y, int scale)
        {
            var pixels = texture.GetPixels32();
            var pixelX = x * scale; var pixelY = texture.height - 1 - y * scale;
            return pixels[pixelY * texture.width + pixelX];
        }

        static void VerifyTextOrientation()
        {
            Check(PrototypeGame.TextOrientationForView(0,true)==CanvasTextOrientation.VenueOutput &&
                  PrototypeGame.TextOrientationForView(2,false)==CanvasTextOrientation.VenueOutput &&
                  PrototypeGame.TextOrientationForView(1,false)==CanvasTextOrientation.CavePreview &&
                  PrototypeGame.TextOrientationForView(0,false)==CanvasTextOrientation.FlatEditor,
                "EXE output, atlas preview, CAVE preview, and flat editing select their matching text orientation modes");
            Check(GameCanvasRenderer.ResolveTextRotation(CanvasTextOrientation.FlatEditor,new Vector2(50,210))==TextQuarterTurn.None,
                "Flat editor text remains in its original orientation");
            Check(GameCanvasRenderer.ResolveTextRotation(CanvasTextOrientation.CavePreview,new Vector2(50,210))==TextQuarterTurn.CounterClockwise90 &&
                  GameCanvasRenderer.ResolveTextRotation(CanvasTextOrientation.CavePreview,new Vector2(470,210))==TextQuarterTurn.Clockwise90 &&
                  GameCanvasRenderer.ResolveTextRotation(CanvasTextOrientation.CavePreview,new Vector2(260,370))==TextQuarterTurn.HalfTurn180 &&
                  GameCanvasRenderer.ResolveTextRotation(CanvasTextOrientation.CavePreview,new Vector2(260,210))==TextQuarterTurn.None &&
                  GameCanvasRenderer.ResolveTextRotation(CanvasTextOrientation.CavePreview,new Vector2(340,50))==TextQuarterTurn.None,
                "CAVE rotates only blue-cell labels for the corresponding wall regions; floor and entrance stay upright");
            Check(GameCanvasRenderer.ResolveTextRotation(CanvasTextOrientation.VenueOutput,new Vector2(260,370))==TextQuarterTurn.CounterClockwise90 &&
                  GameCanvasRenderer.ResolveTextRotation(CanvasTextOrientation.VenueOutput,new Vector2(470,210))==TextQuarterTurn.CounterClockwise90 &&
                  GameCanvasRenderer.ResolveTextRotation(CanvasTextOrientation.VenueOutput,new Vector2(50,210))==TextQuarterTurn.CounterClockwise90 &&
                  GameCanvasRenderer.ResolveTextRotation(CanvasTextOrientation.VenueOutput,new Vector2(200,50))==TextQuarterTurn.CounterClockwise90 &&
                  GameCanvasRenderer.ResolveTextRotation(CanvasTextOrientation.VenueOutput,new Vector2(400,50))==TextQuarterTurn.CounterClockwise90 &&
                  GameCanvasRenderer.ResolveTextRotation(CanvasTextOrientation.VenueOutput,new Vector2(260,210))==TextQuarterTurn.None &&
                  GameCanvasRenderer.ResolveTextRotation(CanvasTextOrientation.VenueOutput,new Vector2(340,50))==TextQuarterTurn.None,
                "Non-blue venue labels, including the fixed scoreboard, retain their established output rotation");
            var blueTileCenters = new[]
            {
                new Vector2(50,210), new Vector2(470,210), new Vector2(260,370),
                new Vector2(200,50), new Vector2(400,50), new Vector2(340,50),
                new Vector2(260,210), new Vector2(340,210)
            };
            Check(blueTileCenters.All(center =>
                    GameCanvasRenderer.ResolveBlueCellTextRotation(CanvasTextOrientation.CavePreview,center) ==
                    GameCanvasRenderer.ResolveBlueCellTextRotation(CanvasTextOrientation.VenueOutput,center)),
                "Blue-cell numbers and underlines use the same rotation in CAVE preview and venue output");
            VerifyVenueWallAtlasDirection();
        }

        static void VerifyVenueWallAtlasDirection()
        {
            var positions = new[]
            {
                new Vector2Int(25,36), // Face 0 / Wall Left
                new Vector2Int(46,18), // Face 1 / Wall Button (near wall)
                new Vector2Int(2,18),  // Face 3 / Wall Top (far wall)
                new Vector2Int(18,3)   // Face 4 / Wall Right Big
            };
            var ids = new[] { "direction-face-0", "direction-face-1", "direction-face-3", "direction-face-4" };
            var markers = positions.Select((position, i) => new LevelItem
            {
                id = ids[i], kind = TileKind.TouchBlue, position = position,
                size = new Vector2Int(4,4), label = "12"
            }).ToArray();
            var level = Fixture(markers);
            var outputShader = Shader.Find("704/Output Atlas");
            var caveShader = Shader.Find("704/Cave Surface");
            Check(outputShader != null && caveShader != null, "Preview and atlas verification finds the production shaders");
            var material = new Material(outputShader);
            var atlas = new RenderTexture(2688,3840,0,RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            atlas.Create();
            var capture = new Texture2D(2688,3840,TextureFormat.RGBA32,false);
            var caveImage = new Texture2D(640,448,TextureFormat.RGBA32,false);
            try
            {
                using (var renderer = new GameCanvasRenderer(2))
                {
                    renderer.Draw(level,0,null,null,false,-1,CanvasTextOrientation.CavePreview);
                    var caveCanvasPixels = renderer.Texture.GetPixels32();
                    var args = Environment.GetCommandLineArgs();
                    var qaIndex = Array.IndexOf(args,"--qa-dir");
                    var qaDirectory = qaIndex >= 0 && qaIndex + 1 < args.Length
                        ? args[qaIndex + 1] : "TestResults";
                    Directory.CreateDirectory(qaDirectory);
                    File.WriteAllBytes(Path.Combine(qaDirectory,"cave-blue-tile-canvas.png"),renderer.Texture.EncodeToPNG());

                    var leftWallFullyFramed = false;
                    var projectedLeftWallHeight = 0f;
                    using (var cave = new CaveRoom(renderer.Texture,caveShader,640,448))
                    {
                        cave.Render();
                        var previous = RenderTexture.active;
                        try
                        {
                            RenderTexture.active = cave.Image;
                            caveImage.ReadPixels(new Rect(0,0,caveImage.width,caveImage.height),0,0);
                            caveImage.Apply();
                        }
                        finally { RenderTexture.active = previous; }
                        var leftWallCorners = new[]
                        {
                            cave.Root.transform.TransformPoint(new Vector3(-16,0,11)),
                            cave.Root.transform.TransformPoint(new Vector3(-16,10,11)),
                            cave.Root.transform.TransformPoint(new Vector3(-16,0,-11)),
                            cave.Root.transform.TransformPoint(new Vector3(-16,10,-11))
                        }.Select(cave.Camera.WorldToScreenPoint).ToArray();
                        leftWallFullyFramed = leftWallCorners.All(point => point.z > 0 && point.x >= 8 &&
                            point.x < caveImage.width - 8 && point.y >= 8 && point.y < caveImage.height - 8);
                        projectedLeftWallHeight = Math.Min(Vector2.Distance(leftWallCorners[0], leftWallCorners[1]),
                            Vector2.Distance(leftWallCorners[2], leftWallCorners[3]));
                    }
                    File.WriteAllBytes(Path.Combine(qaDirectory,"cave-preview-left-wall.png"),caveImage.EncodeToPNG());
                    var warmPixels = caveImage.GetPixels32();
                    var warmPoints = new List<Vector2Int>();
                    for (var y = 0; y < caveImage.height; y++)
                    for (var x = 0; x < caveImage.width; x++)
                    {
                        var pixel = warmPixels[y * caveImage.width + x];
                        if (pixel.r > pixel.g * 1.2f && pixel.r > pixel.b * 1.2f && pixel.r < 120)
                            warmPoints.Add(new Vector2Int(x,y));
                    }
                    Check(warmPoints.Count > 250 && leftWallFullyFramed && projectedLeftWallHeight >= 75,
                        "Default third-person CAVE keeps the full left wall in frame at a readable, upright scale");

                    renderer.Draw(level,0,null,null,false,-1,CanvasTextOrientation.VenueOutput);
                    var venueCanvasPixels = renderer.Texture.GetPixels32();
                    File.WriteAllBytes(Path.Combine(qaDirectory,"venue-output-blue-tile-canvas.png"),renderer.Texture.EncodeToPNG());
                    const int sourcePixelsPerCell = 10;
                    const int markerSide = 4 * sourcePixelsPerCell;
                    for (var i = 0; i < positions.Length; i++)
                    {
                        var sourceRect = new RectInt(positions[i].x * sourcePixelsPerCell,
                            positions[i].y * sourcePixelsPerCell,markerSide,markerSide);
                        var cavePatch = CropCanvasPatch(caveCanvasPixels,renderer.Texture.width,renderer.Texture.height,2,sourceRect);
                        var venuePatch = CropCanvasPatch(venueCanvasPixels,renderer.Texture.width,renderer.Texture.height,2,sourceRect);
                        Check(PatchesEqual(cavePatch,venuePatch),
                            "Blue tile number and underline on source wall " + (i + 1) + " match the CAVE preview rotation");
                    }
                    OutputCompositor.Blit(renderer.Texture,atlas,material);
                    var previousTarget = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = atlas;
                        capture.ReadPixels(new Rect(0,0,2688,3840),0,0);
                        capture.Apply();
                    }
                    finally { RenderTexture.active = previousTarget; }
                    File.WriteAllBytes(Path.Combine(qaDirectory,"venue-wall-directions-atlas.png"),capture.EncodeToPNG());

                    var pixels = capture.GetPixels32();
                    for (var i = 0; i < positions.Length; i++)
                    {
                        var sourceX = positions[i].x * sourcePixelsPerCell;
                        var sourceY = positions[i].y * sourcePixelsPerCell;
                        var outputX = Mathf.RoundToInt((420 - sourceY - markerSide) * 6.4f);
                        var outputTop = Mathf.RoundToInt(sourceX * 6.4f);
                        var outputSide = Mathf.RoundToInt(markerSide * 6.4f);
                        var outputPatch = CropAtlasPatch(pixels,2688,3840,
                            new RectInt(outputX,outputTop,outputSide,outputSide));
                        Check(outputPatch.Count(pixel => pixel.b > 150 && pixel.b > pixel.r * 2) > 100,
                            "Final atlas preserves the blue number tile on mapped wall " + (i + 1));
                    }
                }
            }
            finally
            {
                atlas.Release(); UnityEngine.Object.DestroyImmediate(atlas);
                UnityEngine.Object.DestroyImmediate(capture); UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(caveImage);
                UnityEngine.Object.DestroyImmediate(level);
            }
        }

        static Color32[] CropCanvasPatch(Color32[] pixels, int width, int height, int scale, RectInt topLeftRect)
        {
            var patchWidth = topLeftRect.width * scale;
            var patchHeight = topLeftRect.height * scale;
            var result = new Color32[patchWidth * patchHeight];
            var bottom = height - topLeftRect.yMax * scale;
            var left = topLeftRect.x * scale;
            for (var y = 0; y < patchHeight; y++)
                Array.Copy(pixels,(bottom + y) * width + left,result,y * patchWidth,patchWidth);
            return result;
        }

        static Color32[] CropAtlasPatch(Color32[] pixels, int width, int height, RectInt topLeftRect)
        {
            var result = new Color32[topLeftRect.width * topLeftRect.height];
            var bottom = height - topLeftRect.yMax;
            for (var y = 0; y < topLeftRect.height; y++)
                Array.Copy(pixels,(bottom + y) * width + topLeftRect.x,result,y * topLeftRect.width,topLeftRect.width);
            return result;
        }

        static bool PatchesEqual(Color32[] left, Color32[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            for (var i = 0; i < left.Length; i++) if (!left[i].Equals(right[i])) return false;
            return true;
        }

        static int CountWhitePixels(Texture2D texture, RectInt region, int scale)
        {
            var x0 = region.x * scale; var x1 = region.xMax * scale;
            var y0 = texture.height - region.yMax * scale; var y1 = texture.height - region.y * scale;
            var pixels = texture.GetPixels32(); var count = 0;
            for (var y = y0; y < y1; y++) for (var x = x0; x < x1; x++)
            {
                var pixel = pixels[y * texture.width + x];
                if (pixel.r > 245 && pixel.g > 245 && pixel.b > 245) count++;
            }
            return count;
        }
        static void VerifyVenueScoreboardHud()
        {
            var filenameLevel = Fixture(); filenameLevel.levelNumber = 2;
            filenameLevel.displayName = "1-2 (250ms)";
            filenameLevel.importedPackageName = "1-2 (250ms).zip";
            Check(VenueScoreboardHud.ProgressLabel(filenameLevel) == "1-2",
                "Venue scoreboard matches the package filename and omits its frame-rate suffix");
            filenameLevel.displayName = "自訂名稱";
            Check(VenueScoreboardHud.ProgressLabel(filenameLevel) == "1-2",
                "Venue scoreboard falls back to the imported filename when the display title is edited");
            filenameLevel.importedPackageName = string.Empty;
            Check(VenueScoreboardHud.ProgressLabel(filenameLevel) == "1-2",
                "Venue scoreboard falls back to the assigned level identifier when no package filename exists");
            UnityEngine.Object.DestroyImmediate(filenameLevel);
            Check(VenueScoreboardHud.RemainingWholeSeconds(60, 0) == 60 &&
                  VenueScoreboardHud.RemainingWholeSeconds(60, .01) == 60 &&
                  VenueScoreboardHud.RemainingWholeSeconds(60, 1) == 59 &&
                  VenueScoreboardHud.RemainingWholeSeconds(60, 59.01) == 1 &&
                  VenueScoreboardHud.RemainingWholeSeconds(60, 60) == 0 &&
                  VenueScoreboardHud.RemainingWholeSeconds(60, double.NaN) == 0 &&
                  VenueScoreboardHud.CountdownLabel(60, 0) == "60s" &&
                  VenueScoreboardHud.CountdownLabel(60, 60) == "00s",
                "Venue scoreboard countdown rounds up whole seconds, clamps at zero, and handles invalid elapsed time");

            var level = Fixture(); level.levelNumber = 2; level.timeLimitSeconds = 60;
            level.displayName = "1-2 (250ms)";
            level.importedPackageName = "1-2 (250ms).zip";
            var session = new GameSession(); session.Start(level);
            using (var renderer = new GameCanvasRenderer(2))
            {
                Directory.CreateDirectory("TestResults");
                renderer.Draw(level, 0, session, textOrientation: CanvasTextOrientation.FlatEditor);
                var flat = renderer.Texture.GetPixels32();
                renderer.Draw(level, 0, session, textOrientation: CanvasTextOrientation.VenueOutput);
                var renderScale = renderer.RenderScale;
                var progressPixels = CountWhitePixels(renderer.Texture, VenueLayout.ScoreboardProgressPixels, renderScale);
                var countdownPixels = CountWhitePixels(renderer.Texture, VenueLayout.ScoreboardCountdownPixels, renderScale);
                var separatorPixels = CountWhitePixels(renderer.Texture, new RectInt(380,45,40,10), renderScale);
                var rotated = renderer.Texture.GetPixels32(); var outsideChanges = 0;
                for (var y = 0; y < renderer.Texture.height; y++) for (var x = 0; x < renderer.Texture.width; x++)
                {
                    var index = y * renderer.Texture.width + x;
                    if (flat[index].Equals(rotated[index])) continue;
                    var sourcePoint = new Vector2Int(x / renderScale, (renderer.Texture.height - 1 - y) / renderScale);
                    if (!VenueLayout.ScoreboardPixels.Contains(sourcePoint)) outsideChanges++;
                }
                Check(progressPixels > 0 && countdownPixels > 0 && separatorPixels == 0 && outsideChanges == 0,
                    "Venue HUD draws separated progress and countdown labels only inside their fixed scoreboard face regions");
                renderer.Draw(level, 0, session, textOrientation: CanvasTextOrientation.VenueOutput);
                File.WriteAllBytes("TestResults/venue-scoreboard-hud-preview.png", renderer.Texture.EncodeToPNG());
            }
            session.Tick(60, Array.Empty<GameTouch>());
            using (var renderer = new GameCanvasRenderer(2))
            {
                renderer.Draw(level, session.Elapsed, session, textOrientation: CanvasTextOrientation.VenueOutput);
                Check(session.Outcome == LevelOutcome.Timeout &&
                      CountWhitePixels(renderer.Texture, VenueLayout.ScoreboardCountdownPixels, renderer.RenderScale) > 0,
                    "Venue scoreboard stays visible and reaches zero during the timeout transition");
            }
            UnityEngine.Object.DestroyImmediate(level);
        }
        static void WriteWallOrientationPreviews(ScanLevel wallText)
            => WriteWallOrientationPreviews(wallText, "TestResults");

        static void WriteWallOrientationPreviews(ScanLevel wallText, string resultDirectory)
        {
            Directory.CreateDirectory(resultDirectory);
            using(var renderer=new GameCanvasRenderer())
            {
                renderer.Draw(wallText,0,textOrientation:CanvasTextOrientation.FlatEditor);
                File.WriteAllBytes(Path.Combine(resultDirectory,"wall-text-flat.png"),renderer.Texture.EncodeToPNG());
                renderer.Draw(wallText,0,textOrientation:CanvasTextOrientation.VenueOutput);
                var material=new Material(Shader.Find("704/Output Atlas"));var target=new RenderTexture(2688,3840,0);target.Create();
                try
                {
                    OutputCompositor.Blit(renderer.Texture,target,material);
                    WorkbenchExportService.WriteRenderTexture(target,Path.Combine(resultDirectory,"wall-text-output.png"));
                    using(var cave=new CaveRoom(target,Shader.Find("704/Cave Surface")))
                    {cave.Render();WorkbenchExportService.WriteRenderTexture(cave.Image,Path.Combine(resultDirectory,"wall-text-cave.png"));}
                }
                finally { target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(material); }
            }
        }
        static void VerifyEntrance(OutputProfile profile)
        {
            var texture=new Texture2D(520,420,TextureFormat.RGBA32,false);
            texture.SetPixels32(Enumerable.Repeat(new Color32(255,255,255,255),520*420).ToArray());texture.Apply();
            var venue=ScriptableObject.CreateInstance<VenueLayout>();venue.background=texture;
            Check(!venue.IsAllowed(new Vector2Int(30,2)) && venue.IsAllowed(new Vector2Int(29,2)),"Fixed entrance remains blocked even when the entire background is white");
            Check(Enum.GetValues(typeof(TileKind)).Cast<TileKind>().All(kind=>
            {
                var invalid=Fixture(Item(kind,30,2));invalid.venue=venue;
                var rejected=LevelValidation.Errors(invalid).Any(error=>error.Contains("入口"));UnityEngine.Object.DestroyImmediate(invalid);return rejected;
            }),"All six tile kinds are rejected inside the entrance");
            var tile=Item(TileKind.TouchBlue,28,2);tile.size=Vector2Int.one;tile.waypoints.Add(new Vector2Int(39,2));
            Check(venue.TouchesEntrance(tile) && !venue.PathIsAllowed(tile),"Movement between valid endpoints cannot cross the entrance");
            tile.position=new Vector2Int(28,10);tile.waypoints[0]=new Vector2Int(39,10);
            Check(!venue.TouchesEntrance(tile) && venue.PathIsAllowed(tile),"A path exactly beside the entrance boundary remains available");
            tile.position=new Vector2Int(29,2);tile.waypoints.Clear();
            var level=Fixture(tile);level.venue=venue;
            var serialized=new SerializedObject(level);var item=serialized.FindProperty("items").GetArrayElementAtIndex(0);
            item.FindPropertyRelative("position").vector2IntValue=new Vector2Int(30,2);
            Check(!LevelPlacementGuard.Apply(serialized,out var message) && message!=null && tile.position==new Vector2Int(29,2),"Inspector rejects an entrance position without modifying the level");
            item=serialized.FindProperty("items").GetArrayElementAtIndex(0);item.FindPropertyRelative("size").vector2IntValue=new Vector2Int(2,1);
            Check(!LevelPlacementGuard.Apply(serialized,out message) && tile.size==Vector2Int.one,"Resizing from an allowed cell cannot overlap the entrance");
            item=serialized.FindProperty("items").GetArrayElementAtIndex(0);var path=item.FindPropertyRelative("waypoints");path.arraySize=1;path.GetArrayElementAtIndex(0).vector2IntValue=new Vector2Int(39,2);
            Check(!LevelPlacementGuard.Apply(serialized,out message) && tile.waypoints.Count==0,"Inspector rejects an entrance-crossing route without saving it");
            tile.position=new Vector2Int(28,12);serialized.Update();item=serialized.FindProperty("items").GetArrayElementAtIndex(0);path=item.FindPropertyRelative("waypoints");path.arraySize=1;path.GetArrayElementAtIndex(0).vector2IntValue=new Vector2Int(39,12);
            Check(LevelPlacementGuard.Apply(serialized,out message) && level.items[0].waypoints.Count==1,"Inspector still accepts routes outside the entrance");
            var inspector=UnityEditor.Editor.CreateEditor(level);
            Check(inspector is ScanLevelInspector,"Unity uses the guarded Inspector for level assets");UnityEngine.Object.DestroyImmediate(inspector);
            var output=UnityEngine.Object.Instantiate(profile);output.surfaces.First(surface=>surface.faceIndex==5).sourcePixels=new RectInt(301,0,80,100);
            Check(!output.Validate(out _),"Output profile validation rejects moving the fixed entrance");
            output.Configure704();var outputSerialized=new SerializedObject(output);var entrance=outputSerialized.FindProperty("surfaces").GetArrayElementAtIndex(5);
            entrance.FindPropertyRelative("outputPixels").rectIntValue=new RectInt(2048,1921,640,512);
            Check(!LevelPlacementGuard.ApplyProfile(outputSerialized,out message) && output.surfaces[5].outputPixels.Equals(OutputProfile.EntranceOutputPixels),"Output Inspector rejects moving the entrance in the venue output");
            var surfaceArray=outputSerialized.FindProperty("surfaces");surfaceArray.DeleteArrayElementAtIndex(5);
            Check(!LevelPlacementGuard.ApplyProfile(outputSerialized,out message) && output.surfaces.Count==7,"Output Inspector cannot remove the entrance mapping");
            output.editorLayout=venue;
            Check(!output.TryUnmap(5,OutputProfile.EntranceOutputPixels.center,out _),"Entrance touch input is always rejected regardless of mask colors");
            UnityEngine.Object.DestroyImmediate(output);UnityEngine.Object.DestroyImmediate(level);UnityEngine.Object.DestroyImmediate(venue);UnityEngine.Object.DestroyImmediate(texture);
        }

        static void VerifySafeGreenFloor()
        {
            var texture=new Texture2D(520,420,TextureFormat.RGBA32,false);
            texture.SetPixels32(Enumerable.Repeat(new Color32(255,255,255,255),520*420).ToArray());texture.Apply();
            var venue=ScriptableObject.CreateInstance<VenueLayout>();venue.background=texture;

            var floorSafe=Item(TileKind.SafeGreen,12,12);
            Check(venue.PathIsOnFloor(floorSafe),"SafeGreen footprint fully inside Floor face is accepted");
            var wallSafe=Item(TileKind.SafeGreen,12,2);
            Check(!venue.PathIsOnFloor(wallSafe),"SafeGreen placement on a wall face is rejected");
            var crossingSafe=Item(TileKind.SafeGreen,40,12);
            Check(!venue.PathIsOnFloor(crossingSafe),"SafeGreen footprint crossing the Floor boundary is rejected");

            var movingSafe=Item(TileKind.SafeGreen,12,12);
            movingSafe.size=new Vector2Int(2,2);movingSafe.waypoints.Add(new Vector2Int(40,30));
            Check(venue.PathIsOnFloor(movingSafe),"SafeGreen route whose full footprint stays on Floor is accepted");
            movingSafe.waypoints[0]=new Vector2Int(40,31);
            Check(!venue.PathIsOnFloor(movingSafe),"SafeGreen route leaving Floor is rejected");

            var invalidLevel=Fixture(wallSafe);invalidLevel.venue=venue;
            Check(LevelValidation.Errors(invalidLevel).Any(error=>error.Contains("只能放在地板")),
                "Level validation reports SafeGreen placed outside Floor");
            UnityEngine.Object.DestroyImmediate(invalidLevel);

            var existingLevels=AssetDatabase.FindAssets("t:ScanLevel",new[]{"Assets/ScanPro/Levels"})
                .Select(g=>AssetDatabase.LoadAssetAtPath<ScanLevel>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(l=>l!=null && l.levelNumber>=1 && l.levelNumber<=5).ToArray();
            var existingSafeItems=existingLevels.SelectMany(level=>level.items
                .Where(item=>item.kind==TileKind.SafeGreen).Select(item=>(level,item))).ToArray();
            Check(existingLevels.Length==5 && existingSafeItems.Length>0 &&
                existingSafeItems.All(entry=>entry.level.venue.PathIsOnFloor(entry.item)),
                "All existing SafeGreen placements in the five formal levels remain on Floor");

            var inspectorTile=Item(TileKind.SafeGreen,12,12);
            var inspectorLevel=Fixture(inspectorTile);inspectorLevel.venue=venue;
            var serialized=new SerializedObject(inspectorLevel);
            var serializedItem=serialized.FindProperty("items").GetArrayElementAtIndex(0);
            serializedItem.FindPropertyRelative("position").vector2IntValue=new Vector2Int(12,2);
            Check(!LevelPlacementGuard.Apply(serialized,out var message) &&
                message==LevelPlacementGuard.SafeGreenFloorRejection && inspectorTile.position==new Vector2Int(12,12),
                "Inspector rejects a wall SafeGreen placement without changing the saved item");

            serialized.Update();serializedItem=serialized.FindProperty("items").GetArrayElementAtIndex(0);
            var path=serializedItem.FindPropertyRelative("waypoints");path.arraySize=1;
            path.GetArrayElementAtIndex(0).vector2IntValue=new Vector2Int(12,2);
            Check(!LevelPlacementGuard.Apply(serialized,out message) &&
                message==LevelPlacementGuard.SafeGreenFloorRejection && inspectorTile.waypoints.Count==0,
                "Inspector rejects a SafeGreen route leaving Floor without saving the waypoint");

            UnityEngine.Object.DestroyImmediate(inspectorLevel);
            UnityEngine.Object.DestroyImmediate(venue);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
