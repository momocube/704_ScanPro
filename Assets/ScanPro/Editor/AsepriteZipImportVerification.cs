using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using QRCoder;
using ZXing;
using UnityEngine.SceneManagement;

namespace ScanPro704.Editor
{
    public static class AsepriteZipImportVerification
    {
        readonly struct Mark
        {
            public readonly int X, Y;
            public readonly Color32 Color;
            public Mark(int x, int y, Color32 color) { X = x; Y = y; Color = color; }
        }

        static readonly Color32[] BaseColors =
        {
            new Color32(0,0,0,255), new Color32(50,50,50,255), new Color32(100,100,100,255),
            new Color32(162,161,160,255), new Color32(255,255,255,255)
        };
        static readonly Color32 Blue = new Color32(0, 70, 255, 255);
        static readonly Color32 Green = new Color32(0, 255, 60, 255);
        static readonly Color32 Yellow = new Color32(255, 235, 0, 255);
        static readonly Color32 Red = new Color32(255, 0, 0, 255);
        static readonly Color32 Purple = new Color32(190, 40, 255, 255);
        static readonly Color32 Cyan = new Color32(0, 210, 255, 255);
        static readonly List<string> passed = new List<string>();

        static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("CHECK FAILED: " + description);
            passed.Add(description);
        }

        [MenuItem("704/驗證/Aseprite ZIP 匯入")]
        public static void Run()
        {
            passed.Clear();
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var testRoot = Path.Combine(projectRoot, "TestResults", "AsepriteImport_20261008");
            Directory.CreateDirectory(testRoot);
            var fixturePath = Path.Combine(testRoot, "ScanPro_Importer_Fixture (350ms).zip");
            var invalidPath = Path.Combine(testRoot, "ScanPro_Importer_Fixture_NoSpeed.zip");
            var invalidContentPath = Path.Combine(testRoot, "ScanPro_Importer_Fixture_InvalidBlueContent (350ms).zip");
            var draftFolder = "Assets/ScanPro/LevelDrafts";
            EnsureAssetFolder(draftFolder);
            var temporaryAsset = AssetDatabase.GenerateUniqueAssetPath(draftFolder + "/_AsepriteImportVerification.asset");
            var venue = AssetDatabase.LoadAssetAtPath<VenueLayout>("Assets/ScanPro/Data/704_Venue.asset");
            if (venue == null) throw new InvalidOperationException("Cannot load 704 venue fixture.");
            VerifyAnimatedContentAndPackageNaming(venue, Path.Combine(projectRoot, "TestResults", "AnimatedContent_20261008"));
            var formalHashes = GetFormalLevelHashes();
            var result = default(AsepriteImportResult);
            var assetCreated = false;
            try
            {
                WriteFixture(fixturePath, true);
                VerifyImportPackageMetadata(fixturePath, venue, testRoot);
                var scanLevelScript = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/ScanPro/Runtime/ScanLevel.cs");
                Check(scanLevelScript != null && AssetDatabase.GetAssetPath(scanLevelScript) == "Assets/ScanPro/Runtime/ScanLevel.cs",
                    "Unity imports the ScanLevel source script used by generated drafts");
                result = AsepriteZipImporter.Read(fixturePath, venue);
                Check(result.Succeeded, "The complete 370×270 test ZIP imports without errors: " + result.Error);
                Check(result.Level.displayName == Path.GetFileNameWithoutExtension(fixturePath),
                    "Imported levels keep the complete ZIP filename as their display name");
                Check(result.Level.animationSpeedMilliseconds == 350 && result.Level.initialHearts == 15,
                    "ZIP speed metadata and the imported-level 15-heart default are applied");
                Check(Mathf.Approximately(result.Level.purpleHoldSeconds, .1f),
                    "ZIP imports use the default 0.1-second purple hold threshold");
                Check(result.Level.importedPackageName == Path.GetFileName(fixturePath) &&
                      result.Level.importedAtLocal.EndsWith("(Asia/Taipei)", StringComparison.Ordinal),
                    "The original ZIP name and Taipei import timestamp are recorded");
                Check(result.Level.items.Any(item => item.kind == TileKind.TouchBlue && item.label == "1") &&
                      result.Level.items.Any(item => item.kind == TileKind.TouchBlue && item.label == "2+1"),
                    "blue_count folder names become editable numeric and arithmetic content");
                Check(result.Level.items.Count(item => item.kind == TileKind.ScanBlue) == 1,
                    "blue_scan remains a QR objective, independent of marker color");
                Check(result.Level.items.Count(item => item.kind == TileKind.SafeGreen && !item.allowSafeGreenOutsideFloor) == 3,
                    "basic markers use the neutral palette exclusion, diagonal contact separates regions, and alpha does not define a marker");
                var importedPurple = result.Level.items.Single(item => item.kind == TileKind.HoldPurple);
                Check(importedPurple.purpleClearMask != null &&
                      importedPurple.purpleClearMask.Contains(22, 20) && importedPurple.purpleClearMask.Contains(23, 20) &&
                      !importedPurple.purpleClearMask.Contains(24, 20),
                    "Purple trigger imports the exact to(clearwrong).png mask instead of only its linked group names");
                Check(result.Level.layerOrder.IndexOf(TileLayer.SafeGreen) > result.Level.layerOrder.IndexOf(TileLayer.HazardRed),
                    "Imported basic safety regions are ordered above red hazards so overlapping safe zones stay visible");
                var basicSafe = result.Level.items.Single(item => item.kind == TileKind.SafeGreen &&
                    !item.allowSafeGreenOutsideFloor && item.position == new Vector2Int(12, 12));
                using (var preview = new GameCanvasRenderer(1))
                {
                    preview.Draw(result.Level, 0);
                    var safeColor = TilePalette.ColorOf(TileKind.SafeGreen);
                    var pixel = preview.Texture.GetPixel(125, preview.Texture.height - 1 - 125);
                    Check(pixel.r == safeColor.r && pixel.g == safeColor.g && pixel.b == safeColor.b,
                        "An overlapping basic.png safety region remains green in the converted workbench preview");
                }
                var overlapGame = new GameSession(32);
                overlapGame.Start(result.Level);
                overlapGame.Tick(.01, new[] { TouchAt(basicSafe, 99) });
                Check(overlapGame.Hearts == GameSession.MaximumHearts,
                    "The visible topmost basic safety region protects against an overlapping red hazard");
                VerifyStaticMultiRegionBlue(venue, testRoot);
                var importedShiftGreen = result.Level.items.Where(item => item.kind == TileKind.SafeGreen && item.allowSafeGreenOutsideFloor).ToArray();
                Check(importedShiftGreen.Length == 2,
                    $"shiftGreen states preserve their image locations without the floor-only rule (found {importedShiftGreen.Length}; warnings: {string.Join(" | ", result.Warnings)})");
                var outsideFloorSafe = result.Level.items.First(item => item.kind == TileKind.SafeGreen && item.allowSafeGreenOutsideFloor);
                Check(!VenueLayout.PathIsOnFloor(outsideFloorSafe, venue.pixelsPerCell) && venue.PathIsAllowed(outsideFloorSafe),
                    "An imported shiftGreen location outside Floor remains valid when the venue image allows it");
                Check(!LevelValidation.Errors(result.Level).Any(error => error.Contains("只能放在地板")),
                    "Level validation preserves the imported shiftGreen floor exception");
                VerifyInspectorShiftGreen(venue, outsideFloorSafe.position);
                Check(result.Warnings.Any(warning => warning.Contains("yellow")),
                    "A laser-example folder name is not assigned ScanPro meaning by its color or name");

                var animatedHazard = result.Level.items.Single(item => item.kind == TileKind.HazardRed &&
                    item.position == new Vector2Int(22, 20));
                Check(animatedHazard.animationFrames.Count == 3 &&
                      animatedHazard.AnimationFrameIndex(0, result.Level.animationSpeedMilliseconds) == 0 &&
                      animatedHazard.AnimationFrameIndex(.35, result.Level.animationSpeedMilliseconds) == 1 &&
                      animatedHazard.AnimationFrameIndex(.70, result.Level.animationSpeedMilliseconds) == 2,
                    "Numbered frames are sorted naturally and use the shared 350 ms clock");
                Check(TileShapeUtility.Contains(animatedHazard, animatedHazard.position,
                          new Vector2(22.5f, 20.5f), 0) &&
                      !TileShapeUtility.Contains(animatedHazard, animatedHazard.position,
                          new Vector2(22.5f, 20.5f), 1) &&
                      TileShapeUtility.Contains(animatedHazard, animatedHazard.position,
                          new Vector2(23.5f, 20.5f), 1),
                    "The visible frame mask also defines the touch hit area");

                AssetDatabase.CreateAsset(result.Level, temporaryAsset);
                assetCreated = true;
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(temporaryAsset, ImportAssetOptions.ForceUpdate);
                var reopened = AssetDatabase.LoadAssetAtPath<ScanLevel>(temporaryAsset);
                Check(reopened != null && reopened.importedPackageName == Path.GetFileName(fixturePath) &&
                      reopened.animationSpeedMilliseconds == 350 && reopened.importSummary == result.Summary &&
                      reopened.importWarnings.Contains("yellow") &&
                      reopened.layerOrder.IndexOf(TileLayer.SafeGreen) > reopened.layerOrder.IndexOf(TileLayer.HazardRed),
                    "Imported metadata, conversion summary, and warnings survive saving and reloading the draft asset");
                Check(reopened.items.Single(item => item.kind == TileKind.HazardRed && item.position == new Vector2Int(22, 20))
                          .animationFrames.Count == 3,
                    "Animation masks survive saving and reloading");
                var reopenedPurple = reopened.items.Single(item => item.kind == TileKind.HoldPurple);
                Check(reopenedPurple.purpleClearMask != null && reopenedPurple.purpleClearMask.Contains(23, 20) &&
                      !reopenedPurple.purpleClearMask.Contains(24, 20),
                    "The exact purple clear mask survives saving and reloading the imported draft");
                result.Level = null;

                VerifyPurpleRelease(reopened, animatedHazard.id);
                var original13Zip = Path.Combine(projectRoot, "TestResults", "PurpleMaskFix_20261012", "1-3 (100ms).zip");
                if (File.Exists(original13Zip)) VerifyOriginalPurpleClearMask(original13Zip, venue);
                VerifySwitchGroups(reopened);
                VerifyAscendingCompletion(reopened);
                VerifyHeartRestart(reopened, animatedHazard.id);

                var referenceZip = Path.Combine(testRoot, "4-1 (350ms)_Format_Reference.zip");
                if (File.Exists(referenceZip)) VerifyReferenceExample(referenceZip, venue);
                var userSampleZip = Path.Combine(testRoot, "1-1 (100ms)_Static_Reference.zip");
                if (File.Exists(userSampleZip)) VerifyStaticReferenceExample(userSampleZip, venue);

                WriteFixture(invalidPath, false);
                var failed = AsepriteZipImporter.Read(invalidPath, venue);
                Check(!failed.Succeeded && failed.Error.Contains("共用速度"),
                    "A package without speed metadata fails with a specific message before asset creation");
                WriteFixture(invalidContentPath, true, true);
                var invalidContent = AsepriteZipImporter.Read(invalidContentPath, venue);
                try
                {
                    Check(invalidContent.Succeeded &&
                          invalidContent.Warnings.Any(warning => warning.Contains("blue_count/not-a-number")) &&
                          !invalidContent.Level.items.Any(item => item.kind == TileKind.TouchBlue && item.label == "not-a-number"),
                        "An invalid blue_count folder name is skipped with a visible import warning");
                }
                finally
                {
                    if (invalidContent.Level != null) UnityEngine.Object.DestroyImmediate(invalidContent.Level);
                }
                Check(GetFormalLevelHashes().SequenceEqual(formalHashes),
                    "Failed imports and importer checks leave the existing formal level assets unchanged");
                VerifyLevelDeletion(draftFolder, "Assets/ScanPro/Levels");

                File.WriteAllLines(Path.Combine(testRoot, "importer-checks.txt"), passed.Select((line, index) => $"{index + 1:D2}. PASS  {line}")
                    .Concat(new[] { "", "Scope: Unity Editor data verification only; no Player, scanner, TouchService, or venue hardware test." }));
                Debug.Log($"ASEPRITE ZIP IMPORT VERIFICATION: {passed.Count} checks passed. Fixture: {fixturePath}");
            }
            finally
            {
                if (assetCreated) AssetDatabase.DeleteAsset(temporaryAsset);
                if (result != null && result.Level != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(result.Level)))
                    UnityEngine.Object.DestroyImmediate(result.Level);
                AssetDatabase.SaveAssets();
            }
        }

        static void VerifyPurpleRelease(ScanLevel level, string hazardId)
        {
            var game = new GameSession(19);
            game.Start(level);
            var purple = level.items.Single(item => item.kind == TileKind.HoldPurple);
            var hazard = game.States[hazardId];
            var press = new[] { new GameTouch(91, (Vector2)purple.position + new Vector2(.5f, .5f)) };
            game.Tick(.01, press);
            game.Tick(.81, press);
            Check(game.States[purple.id].PurpleHoldActive && !hazard.Disabled &&
                  game.IsPurpleClearedAt(hazard.Item, new Vector2(23.5f, 20.5f)) &&
                  !game.IsPurpleClearedAt(hazard.Item, new Vector2(24.5f, 20.5f)),
                "Holding the purple region clears only the to(clearwrong) overlap and leaves the rest of its hazard active");
            game.Tick(8.0, press);
            Check(game.States[purple.id].PurpleHoldActive &&
                  game.IsPurpleClearedAt(hazard.Item, new Vector2(23.5f, 20.5f)),
                "The local purple clear mask remains active during an arbitrarily long hold");
            game.Tick(.01, Array.Empty<GameTouch>());
            Check(!game.States[purple.id].PurpleHoldActive && !hazard.Disabled &&
                  !game.IsPurpleClearedAt(hazard.Item, new Vector2(23.5f, 20.5f)),
                "Releasing the purple region restores only its masked portion of the linked hazard");
        }

        static void VerifyOriginalPurpleClearMask(string zipPath, VenueLayout venue)
        {
            var imported = AsepriteZipImporter.Read(zipPath, venue);
            try
            {
                Check(imported.Succeeded, "The original 1-3 source ZIP imports with the current clear-mask schema: " + imported.Error);
                var purple = imported.Level.items.FirstOrDefault(item => item.kind == TileKind.HoldPurple &&
                    item.purpleClearMask != null && item.targetGroups.Any(group =>
                        group.StartsWith("import:frames:", StringComparison.OrdinalIgnoreCase)));
                Check(purple != null, "The original 1-3 purple triggers retain their to(clearwrong).png mask and linked frames groups");
                var frames = imported.Level.items.Where(item => item.kind == TileKind.HazardRed && item.HasAnimation &&
                    purple.targetGroups.Contains(item.group, StringComparer.OrdinalIgnoreCase)).ToArray();
                Check(frames.Length > 0, "The original 1-3 clear mask links to animated frames content");

                Vector2? coveredPoint = null, uncoveredPoint = null;
                foreach (var item in frames)
                for (var frameIndex = 0; frameIndex < item.animationFrames.Count; frameIndex++)
                {
                    var frame = item.animationFrames[frameIndex];
                    var mask = frame.Pixels;
                    for (var y = 0; y < frame.height; y++) for (var x = 0; x < frame.width; x++)
                    {
                        if (!mask[y * frame.width + x]) continue;
                        var cell = item.position + frame.offset + new Vector2Int(x, y);
                        if (purple.purpleClearMask.Contains(cell.x - purple.purpleClearMask.offset.x,
                                cell.y - purple.purpleClearMask.offset.y))
                            coveredPoint = new Vector2(cell.x + .5f, cell.y + .5f);
                        else
                            uncoveredPoint = new Vector2(cell.x + .5f, cell.y + .5f);
                    }
                }
                Check(coveredPoint.HasValue && uncoveredPoint.HasValue,
                    "The original 1-3 frames include content both inside and outside to(clearwrong)");

                Vector2Int? freeCell = null;
                for (var y = 0; y < imported.Level.venue.Height && !freeCell.HasValue; y++)
                for (var x = 0; x < imported.Level.venue.Width; x++)
                {
                    var point = new Vector2(x + .5f, y + .5f);
                    if (imported.Level.items.Any(item => TileShapeUtility.Contains(item,
                            item.PositionAt(0, imported.Level.movementSegmentMs), point,
                            item.AnimationFrameIndex(0, imported.Level.animationSpeedMilliseconds)))) continue;
                    freeCell = new Vector2Int(x, y);
                    break;
                }
                Check(freeCell.HasValue, "The original 1-3 source has an available test cell to activate a purple hold");

                var probe = new LevelItem
                {
                    kind = TileKind.HoldPurple,
                    position = freeCell.Value,
                    size = Vector2Int.one,
                    targetGroups = new List<string>(purple.targetGroups),
                    purpleClearMask = purple.purpleClearMask
                };
                imported.Level.items.Add(probe);
                var game = new GameSession();
                game.Start(imported.Level);
                var touch = new GameTouch(930, (Vector2)freeCell.Value + new Vector2(.5f, .5f));
                game.Tick(.01, new[] { touch });
                game.Tick(.101, new[] { touch });
                Check(game.States[probe.id].PurpleHoldActive && !game.States[frames[0].id].Disabled &&
                      game.IsPurpleClearedAt(frames[0], coveredPoint.Value) &&
                      !game.IsPurpleClearedAt(frames[0], uncoveredPoint.Value),
                    "The original 1-3 clear mask hides only overlapped frame cells and preserves uncovered content");
                var previewRoot = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                    "TestResults", "PurpleMaskFix_20261012");
                Directory.CreateDirectory(previewRoot);
                using (var renderer = new GameCanvasRenderer(1))
                {
                    renderer.Draw(imported.Level, .25, game, game.Codes);
                    File.WriteAllBytes(Path.Combine(previewRoot, "imported-1-3-clear-mask-preview.png"),
                        renderer.Texture.EncodeToPNG());
                }
            }
            finally
            {
                if (imported.Level != null) UnityEngine.Object.DestroyImmediate(imported.Level);
            }
        }

        static void VerifyReferenceExample(string zipPath, VenueLayout venue)
        {
            var reference = AsepriteZipImporter.Read(zipPath, venue);
            try
            {
                Check(reference.Succeeded && reference.Level.animationSpeedMilliseconds == 350,
                    "The provided laser folder example imports its 350 ms format metadata without inheriting its level name");
                Check(reference.Level.items.Count > 0 &&
                      reference.Warnings.Any(warning => warning.Contains("yellow")) &&
                      reference.Warnings.Any(warning => warning.Contains("shiftRed／shiftGreen")),
                    "The laser example validates marked regions while unsupported yellow and untyped shutter semantics stay explicit");
                Check(reference.Warnings.Any(warning => warning.Contains("沒有藍色目標")),
                    "The format-only laser example is identified as incomplete for ScanPro play testing");
            }
            finally
            {
                if (reference.Level != null) UnityEngine.Object.DestroyImmediate(reference.Level);
            }
        }

        static void VerifyStaticReferenceExample(string zipPath, VenueLayout venue)
        {
            var reference = AsepriteZipImporter.Read(zipPath, venue);
            try
            {
                Check(reference.Succeeded, "The provided 1-1 static image package imports: " + reference.Error);
                var countItems = reference.Level.items.Where(item => item.kind == TileKind.TouchBlue)
                    .OrderBy(item => item.position.y).ThenBy(item => item.position.x).ToArray();
                Check(countItems.Length > 1 && countItems.Select(item => item.label)
                      .SequenceEqual(Enumerable.Range(1, countItems.Length).Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture))),
                    "The provided single-frame count image numbers all " + countItems.Length +
                    " disconnected tiles from 1 through N, top-to-bottom and left-to-right");
                var scanItems = reference.Level.items.Where(item => item.kind == TileKind.ScanBlue).ToArray();
                var game = new GameSession(33);
                game.Start(reference.Level);
                var codes = scanItems.Select(item => game.Codes[item.id]).ToArray();
                Check(scanItems.Length > 1 && codes.Distinct().Count() == scanItems.Length &&
                      codes.All(code => code.Length == 4 && code.All(char.IsDigit)),
                    "The provided single-frame scan image imports " + scanItems.Length +
                    " independent objectives with unique four-digit QR codes");
                var beforeRemaining = game.Remaining;
                var scanTarget = scanItems[0];
                var scanCode = game.Codes[scanTarget.id];
                var result = game.Scan("A", scanCode, 1000);
                Check(result.Disposition == ScanDisposition.Accepted && game.States[scanTarget.id].Completed &&
                      game.Remaining == beforeRemaining - 1 && !game.Outcome.HasValue,
                    "A valid scan in the provided 1-1 package completes exactly one blue QR objective");
                using (var scanPreview = new GameCanvasRenderer(1))
                {
                    scanPreview.Draw(reference.Level, 0, game, game.Codes);
                    var scanBlue = TilePalette.ColorOf(TileKind.ScanBlue);
                    var bluePixels = CountColorInItem(scanPreview.Texture, scanTarget, scanBlue);
                    Check(bluePixels == 0,
                        "The scanned blue QR region is absent from the rendered 1-1 package immediately after acceptance");
                }
                var safeItems = reference.Level.items.Where(item => item.kind == TileKind.SafeGreen).ToArray();
                Check(safeItems.Length == 4 && reference.Level.layerOrder.IndexOf(TileLayer.SafeGreen) >
                      reference.Level.layerOrder.IndexOf(TileLayer.HazardRed),
                    "The provided basic.png produces four visible safety regions above the overlapping hazard mask");
                using (var preview = new GameCanvasRenderer(1))
                {
                    preview.Draw(reference.Level, 0);
                    var safeColor = TilePalette.ColorOf(TileKind.SafeGreen);
                    var visible = safeItems.Count(item =>
                    {
                        var x = item.position.x * 10 + 5;
                        var y = item.position.y * 10 + 5;
                        var pixel = preview.Texture.GetPixel(x, preview.Texture.height - 1 - y);
                        return pixel.r == safeColor.r && pixel.g == safeColor.g && pixel.b == safeColor.b;
                    });
                    Check(visible == safeItems.Length,
                        "All four basic.png regions in the provided package are visible in the converted preview");
                }
            }
            finally
            {
                if (reference.Level != null) UnityEngine.Object.DestroyImmediate(reference.Level);
            }
        }

        static void VerifyAnimatedContentAndPackageNaming(VenueLayout venue, string testRoot)
        {
            Check(ScanLevel.TryParsePackageLevelNumber("1-1 (100ms).zip", out var first) && first == 1 &&
                  ScanLevel.TryParsePackageLevelNumber("1-5 (250ms).zip", out var fifth) && fifth == 5 &&
                  ScanLevel.TryParsePackageLevelNumber("2-1 (250ms).zip", out var sixth) && sixth == 6 &&
                  ScanLevel.TryParsePackageLevelNumber("10-5 (350ms).zip", out var fiftieth) && fiftieth == 50 &&
                  !ScanLevel.TryParsePackageLevelNumber("1-6 (350ms).zip", out _) &&
                  !ScanLevel.TryParsePackageLevelNumber("11-1 (350ms).zip", out _),
                "Package filenames map valid grouped identifiers to the correct level numbers and reject out-of-range identifiers");

            var cross = new LevelMaskFrame { width = 5, height = 5, packedPixels = PackMask("00100", "00100", "11111", "00100", "00100") };
            Check(cross.TryGetContentRect(out var crossRect) && crossRect.Equals(new RectInt(2, 0, 1, 5)),
                "Irregular masks use the largest completely filled rectangle with center and top/left tie breaking");
            var mutable = new LevelMaskFrame { width = 2, height = 1, packedPixels = PackMask("10") };
            Check(mutable.TryGetContentRect(out var onePixel) && onePixel.Equals(new RectInt(0, 0, 1, 1)),
                "Animation content rectangles exclude empty pixels in an irregular frame");
            mutable.packedPixels = PackMask("01");
            Check(mutable.TryGetContentRect(out var changedPixel) && changedPixel.Equals(new RectInt(1, 0, 1, 1)),
                "Cached content geometry refreshes after a serialized mask changes");

            var number = new LevelItem { id = "animated-number", kind = TileKind.TouchBlue, position = new Vector2Int(12, 14),
                size = new Vector2Int(8, 6), label = "8" };
            number.animationFrames.Add(Frame(5, 5, "11100", "11100", "00000", "00000", "00000"));
            number.animationFrames.Add(Frame(5, 5, "00000", "00000", "00111", "00111", "00000"));
            number.animationFrames.Add(Frame(5, 5, "00000", "00000", "00000", "00000", "00000"));
            number.animationFrames.Add(Frame(5, 5, "00000", "00111", "00111", "00000", "00000"));
            Check(number.TryGetContentBounds(number.position, 0, out var numberStart) && numberStart.position == number.position &&
                  numberStart.size == new Vector2(3, 2),
                "An animated number uses the initial frame's actual position and size");
            Check(number.TryGetContentBounds(number.position, 1, out var numberMoved) && numberMoved.position == new Vector2(14, 16) &&
                  numberMoved.size == new Vector2(3, 2) &&
                  number.TryGetContentBounds(number.position, 3, out var numberEnd) && numberEnd.position == new Vector2(14, 15),
                "Animated number bounds follow moved frames without changing item identity");
            Check(!number.TryGetContentBounds(number.position, 2, out _),
                "An empty animation frame hides its number");

            var scan = new LevelItem { id = "animated-qr", kind = TileKind.ScanBlue, position = new Vector2Int(28, 14),
                size = new Vector2Int(8, 8) };
            scan.animationFrames.Add(Frame(8, 8, "11110000", "11110000", "11110000", "11110000", "00000000", "00000000", "00000000", "00000000"));
            scan.animationFrames.Add(Frame(8, 8, "00001111", "00001111", "00001111", "00001111", "00000000", "00000000", "00000000", "00000000"));
            scan.animationFrames.Add(Frame(8, 8, "00000000", "00000000", "00000000", "00000000", "11110000", "11110000", "11110000", "11110000"));
            scan.animationFrames.Add(Frame(8, 8, "00000000", "00000000", "00000000", "00000000", "00001111", "00001111", "00001111", "00001111"));
            scan.animationFrames.Add(Frame(8, 8, "00000000", "00000000", "00000000", "00000000", "00000000", "00000000", "00000000", "00000000"));
            Check(scan.TryGetContentBounds(scan.position, 0, out var qrStart) && qrStart.position == new Vector2(28, 14) &&
                  scan.TryGetContentBounds(scan.position, 1, out var qrRight) && qrRight.position == new Vector2(32, 14) &&
                  scan.TryGetContentBounds(scan.position, 2, out var qrLower) && qrLower.position == new Vector2(28, 18) &&
                  !scan.TryGetContentBounds(scan.position, 4, out _) && LevelValidation.ContentErrors(scan).Count == 0,
                "Moving animated QR targets remain valid at 4×4 active cells, update position per frame and skip empty frames");

            var sparseAnimatedNumber = new LevelItem
            {
                id = "sparse-animated-number", kind = TileKind.TouchBlue, position = new Vector2Int(26, 8),
                size = new Vector2Int(12, 4), label = "8"
            };
            sparseAnimatedNumber.animationFrames.Add(Frame(12, 4,
                "111100000000", "111100000000", "000000001111", "000000001111"));
            Check(!venue.IsAllowed(sparseAnimatedNumber.position, sparseAnimatedNumber.size) &&
                  venue.FootprintIsAllowed(sparseAnimatedNumber) && venue.PathIsAllowed(sparseAnimatedNumber) &&
                  !venue.TouchesEntrance(sparseAnimatedNumber) && !venue.TouchesScoreboard(sparseAnimatedNumber),
                "Irregular animation frames are checked by occupied cells, not the larger union bounds");
            var forbiddenAnimatedScan = new LevelItem
            {
                id = "forbidden-animated-scan", kind = TileKind.ScanBlue, position = new Vector2Int(28, 8),
                size = new Vector2Int(4, 2)
            };
            forbiddenAnimatedScan.animationFrames.Add(Frame(2, 2, "11", "11"));
            forbiddenAnimatedScan.animationFrames[0].offset = new Vector2Int(2, 0);
            Check(!venue.FootprintIsAllowed(forbiddenAnimatedScan) && !venue.PathIsAllowed(forbiddenAnimatedScan) &&
                  venue.TouchesEntrance(forbiddenAnimatedScan),
                "An occupied animated cell inside the fixed entrance remains invalid");
            var animatedSafe = new LevelItem
            {
                id = "animated-safe-floor", kind = TileKind.SafeGreen, position = new Vector2Int(8, 8),
                size = new Vector2Int(12, 12)
            };
            var safeFrame = new LevelMaskFrame { width = 12, height = 12,
                packedPixels = PackMask("000000000000", "000000000000", "001100000000", "001100000000",
                    "000000000000", "000000000000", "000000000000", "000000000000", "000000000000", "000000000000",
                    "000000000011", "000000000011") };
            animatedSafe.animationFrames.Add(safeFrame);
            animatedSafe.waypoints.Add(new Vector2Int(20, 20));
            Check(!VenueLayout.IsOnFloor(animatedSafe.position, animatedSafe.size, venue.pixelsPerCell) &&
                  VenueLayout.PathIsOnFloor(animatedSafe, venue.pixelsPerCell) && venue.PathIsAllowed(animatedSafe),
                "Animated SafeGreen validates its visible frames and swept path on Floor while ignoring empty union space");
            var animatedMapLevel = ScriptableObject.CreateInstance<ScanLevel>();
            try
            {
                animatedMapLevel.venue = venue;
                animatedMapLevel.items.Add(sparseAnimatedNumber);
                Check(!LevelValidation.Errors(animatedMapLevel).Any(error => error.Contains("入口") || error.Contains("黑區") || error.Contains("保留區")),
                    "Level validation accepts legal animated content whose union bounds cross unused protected space");
            }
            finally { UnityEngine.Object.DestroyImmediate(animatedMapLevel); }
            VerifyAnimatedQrOutput(venue, scan, testRoot);

            var publishDraft = ScriptableObject.CreateInstance<ScanLevel>();
            var assigned = ScriptableObject.CreateInstance<ScanLevel>();
            try
            {
                publishDraft.importedPackageName = "1-2 (250ms).zip";
                publishDraft.levelNumber = 2;
                assigned.levelNumber = 1;
                Check(ScanProWorkbench.TryResolvePublishNumber(publishDraft, new[] { assigned }, out var freeNumber, out _) && freeNumber == 2 &&
                      publishDraft.levelNumber == 2,
                    "Publishing an imported package preserves its filename-assigned number despite shuffled draft arrival order");
                assigned.levelNumber = 2; assigned.displayName = "既有 1-2";
                Check(!ScanProWorkbench.TryResolvePublishNumber(publishDraft, new[] { assigned }, out _, out var conflict) &&
                      conflict.Contains("1-2") && publishDraft.levelNumber == 2,
                    "A conflicting package number stops publication without changing the draft");
                assigned.levelNumber = 49; assigned.displayName = "既有 10-4";
                publishDraft.importedPackageName = "10-5 (350ms).zip"; publishDraft.levelNumber = 50;
                Check(ScanProWorkbench.TryResolvePublishNumber(publishDraft, new[] { assigned }, out var crossGroup, out _) && crossGroup == 50,
                    "Publishing preserves the last valid number across level groups");
                publishDraft.importedPackageName = "Unnumbered (350ms).zip"; publishDraft.levelNumber = 1;
                assigned.levelNumber = 1;
                Check(ScanProWorkbench.TryResolvePublishNumber(publishDraft, new[] { assigned }, out var fallback, out _) && fallback == 2,
                    "Unnumbered packages retain automatic first-free publication numbering");
            }
            finally { UnityEngine.Object.DestroyImmediate(publishDraft); UnityEngine.Object.DestroyImmediate(assigned); }

            var imported = AssetDatabase.LoadAssetAtPath<ScanLevel>("Assets/ScanPro/LevelDrafts/Imported_1-2 (250ms).asset");
            if (imported != null)
            {
                var times = new[] { 0d, .25d, 1.5d, 4.25d, 17.75d };
                using (var renderer = new GameCanvasRenderer())
                {
                    var animatedNumbers = imported.items.Where(item => item.kind == TileKind.TouchBlue).ToArray();
                    var checkedFrames = 0;
                    foreach (var target in animatedNumbers)
                    {
                        for (var frameIndex = 0; frameIndex < target.animationFrames.Count; frameIndex++)
                        {
                            var maskFrame = target.animationFrames[frameIndex];
                            var found = target.TryGetContentBounds(target.position, frameIndex, out var bounds);
                            if (maskFrame.Pixels.All(bit => !bit))
                            {
                                Check(!found, "Empty imported numeric frames hide both the tile and its label");
                                continue;
                            }
                            if (!found) throw new InvalidOperationException("A visible imported number lost its content bounds.");
                            var localX = Mathf.RoundToInt(bounds.x - target.position.x) - maskFrame.offset.x;
                            var localY = Mathf.RoundToInt(bounds.y - target.position.y) - maskFrame.offset.y;
                            for (var y = 0; y < bounds.height; y++) for (var x = 0; x < bounds.width; x++)
                                if (!maskFrame.Contains(localX + x, localY + y))
                                    throw new InvalidOperationException("Numeric label bounds include pixels outside the current blue mask.");
                            checkedFrames++;
                        }
                    }
                    Check(checkedFrames == animatedNumbers.Sum(target => target.animationFrames.Count(frame => frame.Pixels.Any(bit => bit))),
                        $"Every visible frame of all {animatedNumbers.Length} imported numeric targets keeps its content inside the current mask");
                    var codes = imported.items.Where(item => item.IsScan).Select((item, index) => new { item.id, Code = (1001 + index).ToString("D4") })
                        .ToDictionary(entry => entry.id, entry => entry.Code);
                    var snapshots = new[] { times[0], times[1], times[3], times[4] };
                    Directory.CreateDirectory(testRoot);
                    for (var i = 0; i < snapshots.Length; i++)
                    {
                        renderer.Draw(imported, snapshots[i], null, codes);
                        Check(!HasWhiteOutsideCurrentMasks(renderer.Texture, imported, snapshots[i]),
                            $"Imported preview at {snapshots[i]:F2}s has no oversized text or QR pixels outside visible blue masks");
                        var png = Path.Combine(testRoot, $"imported-{i:D2}-{snapshots[i]:F2}s.png");
                        File.WriteAllBytes(png, renderer.Texture.EncodeToPNG());
                    }
                }
                Check(imported.items.Count(item => item.kind == TileKind.TouchBlue && item.animationFrames.Count == 72) == 8,
                    "The eight 1-2 numeric targets retain their original IDs and all 72 animation frames");
                Check(imported.items.Count(item => item.kind == TileKind.ScanBlue && item.animationFrames.Count > 0) == 8,
                    "All eight imported moving QR objectives retain their animation and scan identities");
            }

            var venuePosition = new LevelItem { kind = TileKind.TouchBlue, position = new Vector2Int(12, 14), size = Vector2Int.one,
                waypoints = new List<Vector2Int> { new Vector2Int(16, 14) } };
            Check(venuePosition.TryGetContentBounds(1.5, 1500, 250, out var manualPath) && manualPath.position == new Vector2(16, 14),
                "Ordinary waypoint movement remains independent of imported animation offsets");
            number.animationFrames.Clear();
            number.position = new Vector2Int(100, 100);
            Check(number.TryGetContentBounds(number.position, 0, out var circleFallback) && circleFallback.position == number.position,
                "Static hand-authored content continues to use its original item bounds");
        }

        static void VerifyImportPackageMetadata(string fixturePath, VenueLayout venue, string testRoot)
        {
            var numberedPath = Path.Combine(testRoot, "1-2 (350ms).zip");
            File.Copy(fixturePath, numberedPath, true);
            var numbered = AsepriteZipImporter.Read(numberedPath, venue);
            try
            {
                Check(numbered.Succeeded && numbered.Level.levelNumber == 2 && numbered.Level.LevelIdentifier == "1-2" &&
                      numbered.Level.displayName == "1-2 (350ms)" && numbered.Level.animationSpeedMilliseconds == 350,
                    "A numbered ZIP imports with its filename as the display name and its grouped identifier as sort order");
            }
            finally { if (numbered.Level != null) UnityEngine.Object.DestroyImmediate(numbered.Level); }

            var unnumberedPath = Path.Combine(testRoot, "Unnumbered Fixture (350ms).zip");
            File.Copy(fixturePath, unnumberedPath, true);
            var unnumbered = AsepriteZipImporter.Read(unnumberedPath, venue);
            try
            {
                Check(unnumbered.Succeeded && unnumbered.Level.displayName == "Unnumbered Fixture (350ms)" &&
                      unnumbered.Warnings.Any(warning => warning.Contains("自動配號")),
                    "An unnumbered ZIP keeps its filename and reports the automatic fallback number");
            }
            finally { if (unnumbered.Level != null) UnityEngine.Object.DestroyImmediate(unnumbered.Level); }

            var repeated = AsepriteZipImporter.Read(numberedPath, venue);
            try
            {
                Check(repeated.Succeeded && repeated.Level.levelNumber == 2 && repeated.Level.displayName == "1-2 (350ms)",
                    "Repeated package imports preserve their filename and requested level number in a separate draft");
            }
            finally { if (repeated.Level != null) UnityEngine.Object.DestroyImmediate(repeated.Level); }
        }

        static void VerifyAnimatedQrOutput(VenueLayout venue, LevelItem item, string outputFolder)
        {
            var level = ScriptableObject.CreateInstance<ScanLevel>();
            level.venue = venue; level.animationSpeedMilliseconds = 250; level.items.Add(item);
            const string code = "0007";
            var codes = new Dictionary<string, string> { [item.id] = code };
            try
            {
                using (var renderer = new GameCanvasRenderer())
                using (var generator = new QRCodeGenerator())
                using (var qrData = generator.CreateQrCode(code, QRCodeGenerator.ECCLevel.Q))
                {
                    var matrixSize = qrData.ModuleMatrix.Count;
                    var snapshots = new[] { 0d, .25d, .5d, .75d };
                    for (var i = 0; i < snapshots.Length; i++)
                    {
                        var seconds = snapshots[i];
                        renderer.Draw(level, seconds, null, codes);
                        var frameIndex = item.AnimationFrameIndex(seconds, level.animationSpeedMilliseconds);
                        Check(item.TryGetQrContentBounds(item.PositionAt(seconds, level.movementSegmentMs), frameIndex, out var bounds),
                            $"Animated QR frame {frameIndex + 1} supplies a visible content region");
                        var renderScale = renderer.RenderScale;
                        var boundsWidth = Mathf.RoundToInt(bounds.width * 10);
                        var boundsHeight = Mathf.RoundToInt(bounds.height * 10);
                        var inset = !item.HasAnimation && item.shape == TileShape.Circle ? 0 : 2;
                        var logicalSide = Mathf.Min(boundsWidth, boundsHeight) - inset * 2;
                        var moduleSize = logicalSide * renderScale / matrixSize;
                        var pixelSide = moduleSize * matrixSize;
                        var padding = (logicalSide * renderScale - pixelSide) / 2;
                        var originX = (Mathf.RoundToInt(bounds.x * 10) + (boundsWidth - Mathf.Min(boundsWidth, boundsHeight)) / 2 + inset) * renderScale + padding;
                        var originY = (Mathf.RoundToInt(bounds.y * 10) + (boundsHeight - Mathf.Min(boundsWidth, boundsHeight)) / 2 + inset) * renderScale + padding;
                        var rgb = new byte[pixelSide * pixelSide * 3];
                        var texturePixels = renderer.Texture.GetPixels32();
                        for (var y = 0; y < pixelSide; y++) for (var x = 0; x < pixelSide; x++)
                        {
                            var color = texturePixels[(renderer.Texture.height - 1 - (originY + y)) * renderer.Texture.width + originX + x];
                            var offset = (y * pixelSide + x) * 3;
                            rgb[offset] = color.r; rgb[offset + 1] = color.g; rgb[offset + 2] = color.b;
                        }
                        var enlargedSide = pixelSide * 3;
                        var enlarged = new byte[enlargedSide * enlargedSide * 3];
                        for (var y = 0; y < enlargedSide; y++) for (var x = 0; x < enlargedSide; x++)
                        {
                            var source = ((y / 3) * pixelSide + x / 3) * 3;
                            var target = (y * enlargedSide + x) * 3;
                            enlarged[target] = rgb[source]; enlarged[target + 1] = rgb[source + 1]; enlarged[target + 2] = rgb[source + 2];
                        }
                        var decoded = new BarcodeReaderGeneric()
                            .Decode(new RGBLuminanceSource(enlarged, enlargedSide, enlargedSide, RGBLuminanceSource.BitmapFormat.RGB24));
                        Check(decoded != null && decoded.Text == code,
                            $"The moving QR in animation frame {frameIndex + 1} independently decodes to {code}");
                        if (i == 0 || i == snapshots.Length - 1)
                        {
                            Directory.CreateDirectory(outputFolder);
                            File.WriteAllBytes(Path.Combine(outputFolder, $"animated-qr-frame-{frameIndex + 1:D2}.png"), renderer.Texture.EncodeToPNG());
                        }
                    }
                    renderer.Draw(level, 1.0, null, codes);
                    var qrBluePixels = CountColorInItem(renderer.Texture, item, TilePalette.ColorOf(TileKind.ScanBlue));
                    Check(qrBluePixels == 0, "An empty animation frame hides the animated QR and its blue target together");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(level); }
        }

        static bool HasWhiteOutsideCurrentMasks(Texture2D texture, ScanLevel level, double seconds)
        {
            var width = texture.width; var height = texture.height; var scale = texture.width / 520;
            var covered = new bool[width * height];
            foreach (var item in level.items.Where(target => target.kind == TileKind.TouchBlue || target.IsScan))
            {
                if (!item.HasAnimation) continue;
                var frame = item.animationFrames[item.AnimationFrameIndex(seconds, level.animationSpeedMilliseconds)];
                if (frame == null) continue;
                var origin = item.PositionAt(seconds, level.movementSegmentMs);
                var mask = frame.Pixels;
                for (var y = 0; y < frame.height; y++) for (var x = 0; x < frame.width; x++)
                {
                    if (!mask[y * frame.width + x]) continue;
                    var left = Mathf.RoundToInt((origin.x + frame.offset.x + x) * 10 * scale);
                    var top = Mathf.RoundToInt((origin.y + frame.offset.y + y) * 10 * scale);
                    for (var py = top; py < top + 10 * scale; py++)
                    {
                        var row = (height - 1 - py) * width;
                        for (var px = left; px < left + 10 * scale; px++)
                            if (px >= 0 && px < width && py >= 0 && py < height) covered[row + px] = true;
                    }
                }
            }
            var pixels = texture.GetPixels32();
            for (var i = 0; i < pixels.Length; i++)
                if (pixels[i].r == 255 && pixels[i].g == 255 && pixels[i].b == 255 && !covered[i]) return true;
            return false;
        }

        static LevelMaskFrame Frame(int width, int height, params string[] rows) =>
            new LevelMaskFrame { width = width, height = height, packedPixels = PackMask(rows) };

        static string PackMask(params string[] rows)
        {
            var width = rows.Length == 0 ? 0 : rows[0].Length;
            var bits = new BitArray(width * rows.Length);
            for (var y = 0; y < rows.Length; y++)
                for (var x = 0; x < width; x++) bits[y * width + x] = rows[y][x] == '1' || rows[y][x] == '#';
            var bytes = new byte[(bits.Length + 7) / 8]; bits.CopyTo(bytes, 0);
            return Convert.ToBase64String(bytes);
        }

        static int CountColorInItem(Texture2D texture, LevelItem item, Color32 color)
        {
            var count = 0;
            var pixels = texture.GetPixels32();
            var scale = texture.width / 520;
            var left = Mathf.Clamp(item.position.x * 10 * scale, 0, texture.width);
            var right = Mathf.Clamp((item.position.x + item.size.x) * 10 * scale, 0, texture.width);
            var top = Mathf.Clamp(item.position.y * 10 * scale, 0, texture.height);
            var bottom = Mathf.Clamp((item.position.y + item.size.y) * 10 * scale, 0, texture.height);
            for (var y = top; y < bottom; y++) for (var x = left; x < right; x++)
            {
                var pixel = pixels[(texture.height - 1 - y) * texture.width + x];
                if (pixel.r == color.r && pixel.g == color.g && pixel.b == color.b) count++;
            }
            return count;
        }

        static void VerifyLevelDeletion(string draftFolder, string levelFolder)
        {
            var path = AssetDatabase.GenerateUniqueAssetPath(draftFolder + "/_LevelDeletionVerification.asset");
            var draft = ScriptableObject.CreateInstance<ScanLevel>();
            AssetDatabase.CreateAsset(draft, path);
            try
            {
                Check(ScanProWorkbench.CanDeleteAsset(draft) && ScanProWorkbench.DeleteEditableAsset(draft) &&
                      AssetDatabase.LoadAssetAtPath<ScanLevel>(path) == null,
                    "The workbench can permanently delete a selected draft level asset");
                var formalPath = AssetDatabase.GenerateUniqueAssetPath(levelFolder + "/_LevelDeletionEligibilityVerification.asset");
                var formal = ScriptableObject.CreateInstance<ScanLevel>();
                formal.levelNumber = 1;
                AssetDatabase.CreateAsset(formal, formalPath);
                var readOnly = ScriptableObject.CreateInstance<ScanLevel>();
                readOnly.levelNumber = 6;
                try
                {
                    Check(ScanProWorkbench.CanDeleteAsset(formal) && !ScanProWorkbench.CanDeleteAsset(readOnly),
                        "Editable formal levels can be deleted while read-only levels remain protected");
                }
                finally
                {
                    if (AssetDatabase.LoadAssetAtPath<ScanLevel>(formalPath) != null)
                        AssetDatabase.DeleteAsset(formalPath);
                    if (readOnly != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(readOnly)))
                        UnityEngine.Object.DestroyImmediate(readOnly);
                }
            }
            finally
            {
                if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
                if (draft != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(draft)))
                    UnityEngine.Object.DestroyImmediate(draft);
            }
            VerifyFormalLevelDeletion(levelFolder);
        }

        static void VerifyFormalLevelDeletion(string levelFolder)
        {
            var path = AssetDatabase.GenerateUniqueAssetPath(levelFolder + "/_LevelDeletionVerification.asset");
            var formal = ScriptableObject.CreateInstance<ScanLevel>();
            formal.levelNumber = 5;
            formal.displayName = "刪除測試關卡";
            AssetDatabase.CreateAsset(formal, path);
            var scenePath = PrototypeBuild.ScenePath;
            var scene = SceneManager.GetSceneByPath(scenePath);
            var wasLoaded = scene.IsValid() && scene.isLoaded;
            var previousActive = SceneManager.GetActiveScene();
            PrototypeGame game = null;
            ScanLevel[] originalLevels = Array.Empty<ScanLevel>();
            var capturedOriginalLevels = false;
            try
            {
                if (!wasLoaded) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                game = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<PrototypeGame>(true)).FirstOrDefault();
                if (game == null) throw new InvalidOperationException("PrototypeGame is missing from the test scene.");
                var serialized = new SerializedObject(game);
                serialized.Update();
                var levels = serialized.FindProperty("levels");
                originalLevels = Enumerable.Range(0, levels.arraySize)
                    .Select(index => levels.GetArrayElementAtIndex(index).objectReferenceValue as ScanLevel).ToArray();
                capturedOriginalLevels = true;

                SetSceneLevelReferences(scene, game, new[] { formal });
                var deletedLast = ScanProWorkbench.DeleteEditableAsset(formal);
                var afterLastDelete = new SerializedObject(game);
                afterLastDelete.Update();
                Check(deletedLast && AssetDatabase.LoadAssetAtPath<ScanLevel>(path) == null &&
                      afterLastDelete.FindProperty("levels").arraySize == 0,
                    "The workbench can delete the last formal level and leave an empty scene list");

                formal = ScriptableObject.CreateInstance<ScanLevel>();
                formal.levelNumber = 5;
                formal.displayName = "恢復測試關卡";
                AssetDatabase.CreateAsset(formal, path);
                Check(ScanProWorkbench.SyncPrototypeSceneLevels(new[] { formal }),
                    "Publishing a new formal level restores the Player scene list after it was empty");

                SetSceneLevelReferences(scene, game, originalLevels.Concat(new[] { formal }).ToArray());

                var deleted = ScanProWorkbench.DeleteEditableAsset(formal);
                var after = new SerializedObject(game);
                after.Update();
                var remaining = after.FindProperty("levels");
                Check(deleted && AssetDatabase.LoadAssetAtPath<ScanLevel>(path) == null &&
                      remaining.arraySize == originalLevels.Length,
                    "Deleting an editable formal level also removes its reference from the Player scene");
            }
            finally
            {
                if (capturedOriginalLevels && game != null && scene.IsValid() && scene.isLoaded)
                {
                    try { SetSceneLevelReferences(scene, game, originalLevels); }
                    catch (Exception exception) { Debug.LogError("Failed to restore the Sandbox scene level list: " + exception.Message); }
                }
                if (AssetDatabase.LoadAssetAtPath<ScanLevel>(path) != null)
                    AssetDatabase.DeleteAsset(path);
                if (!wasLoaded && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
            }
        }

        static void SetSceneLevelReferences(UnityEngine.SceneManagement.Scene scene, PrototypeGame game, ScanLevel[] levels)
        {
            var serialized = new SerializedObject(game);
            serialized.Update();
            var property = serialized.FindProperty("levels");
            property.arraySize = levels.Length;
            for (var i = 0; i < levels.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = levels[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save the temporary scene reference.");
        }

        static void VerifyStaticMultiRegionBlue(VenueLayout venue, string testRoot)
        {
            var zipPath = Path.Combine(testRoot, "ScanPro_Static_Blue_Regions (350ms).zip");
            WriteStaticMultiRegionFixture(zipPath);
            var result = AsepriteZipImporter.Read(zipPath, venue);
            try
            {
                Check(result.Succeeded, "A single-frame multi-region blue fixture imports: " + result.Error);
                var countItems = result.Level.items.Where(item => item.kind == TileKind.TouchBlue)
                    .OrderBy(item => item.position.y).ThenBy(item => item.position.x).ToArray();
                Check(countItems.Length == 10 && countItems.Select(item => item.label)
                      .SequenceEqual(Enumerable.Range(1, 10).Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture))),
                    "One static count image numbers its ten disconnected regions 1 through 10");
                using (var preview = new GameCanvasRenderer(1))
                {
                    preview.Draw(result.Level, 0);
                    var white = new Color32(255, 255, 255, 255);
                    var missing = countItems.Where(item => CountColorInItem(preview.Texture, item, white) == 0)
                        .Select(item => item.label).ToArray();
                    Check(missing.Length == 0,
                        "All imported numbers remain visible in 1×1 blue cells, including the two-digit label 10; missing: " +
                        string.Join(", ", missing));
                }
                var scanItems = result.Level.items.Where(item => item.kind == TileKind.ScanBlue).ToArray();
                var game = new GameSession(34);
                game.Start(result.Level);
                var codes = scanItems.Select(item => game.Codes[item.id]).ToArray();
                Check(scanItems.Length == 2 && codes.Distinct().Count() == 2 &&
                      codes.All(code => code.Length == 4 && code.All(char.IsDigit)),
                    "One static scan image keeps each disconnected region as a separate QR with a unique code");
            }
            finally
            {
                if (result.Level != null) UnityEngine.Object.DestroyImmediate(result.Level);
            }
        }

        static void VerifyInspectorShiftGreen(VenueLayout venue, Vector2Int outsideFloor)
        {
            var fixture = ScriptableObject.CreateInstance<ScanLevel>();
            fixture.venue = venue;
            fixture.items.Add(new LevelItem
            {
                kind = TileKind.SafeGreen, position = new Vector2Int(12, 12), size = Vector2Int.one,
                allowSafeGreenOutsideFloor = true
            });
            try
            {
                var serialized = new SerializedObject(fixture);
                serialized.Update();
                serialized.FindProperty("items").GetArrayElementAtIndex(0)
                    .FindPropertyRelative("position").vector2IntValue = outsideFloor;
                var accepted = LevelPlacementGuard.Apply(serialized, out var message);
                Check(accepted && fixture.items[0].position == outsideFloor && string.IsNullOrEmpty(message),
                    "Inspector placement accepts an imported shiftGreen region outside Floor");
            }
            finally { UnityEngine.Object.DestroyImmediate(fixture); }
        }

        static void VerifySwitchGroups(ScanLevel level)
        {
            var game = new GameSession(23);
            game.Start(level);
            foreach (var trigger in level.items.Where(item => item.kind == TileKind.SwitchCyan))
            {
                Check(trigger.targetGroups.Count == 2, "Each shutter has two ordered switch states");
                var first = level.items.First(item => item.group == trigger.targetGroups[0]);
                var second = level.items.First(item => item.group == trigger.targetGroups[1]);
                Check(!game.States[first.id].Disabled && game.States[second.id].Disabled,
                    "A shutter starts on its first image state");
                var code = game.Codes[trigger.id];
                Check(game.Scan("A", code, 0).Disposition == ScanDisposition.Accepted,
                    "A shutter QR advances the state sequence");
                Check(game.States[first.id].Disabled && !game.States[second.id].Disabled,
                    "The next shutter state changes the corresponding group interaction");
            }
        }

        static void VerifyAscendingCompletion(ScanLevel level)
        {
            var game = new GameSession(29);
            game.Start(level);
            var first = level.items.Single(item => item.kind == TileKind.TouchBlue && item.label == "1" &&
                item.position == new Vector2Int(15, 15));
            var later = level.items.Single(item => item.kind == TileKind.TouchBlue && item.label == "2+1");
            game.Tick(.01, new[] { TouchAt(later, 1) });
            Check(!game.States[later.id].Completed, "A later arithmetic answer cannot be completed before the smaller answer");
            game.Tick(.01, Array.Empty<GameTouch>());
            game.Tick(.01, new[] { TouchAt(first, 1) });
            Check(game.States[first.id].Completed, "The smallest numeric answer can be completed first");
            game.Tick(.01, Array.Empty<GameTouch>());
            game.Tick(.01, new[] { TouchAt(later, 1) });
            Check(game.States[later.id].Completed, "An arithmetic answer is ordered by its evaluated result");
            game.Tick(3.0, Array.Empty<GameTouch>());
            Check(game.States[first.id].Completed && game.States[later.id].Completed,
                "Completed blue targets remain completed after their animation cycles");
        }

        static void VerifyHeartRestart(ScanLevel level, string hazardId)
        {
            var game = new GameSession(31);
            game.Start(level);
            for (var i = 0; i < 3; i++) game.AddHeart();
            Check(game.Hearts == GameSession.MaximumHearts, "Adding hearts never exceeds the fifteen-heart limit");
            for (var i = 0; i < GameSession.MaximumHearts; i++)
            {
                var hazard = game.States[hazardId].Item;
                game.Tick(.01, new[] { TouchAt(hazard, 12) });
                if (game.Outcome.HasValue) break;
                game.Tick(.01, Array.Empty<GameTouch>());
            }
            Check(game.Outcome == LevelOutcome.HeartsDepleted, "The imported 15-heart level can reach a failed outcome");
            game.Start(level);
            Check(game.Hearts == GameSession.MaximumHearts, "Restarting a failed imported level restores all fifteen hearts");
        }

        static GameTouch TouchAt(LevelItem item, int id)
            => new GameTouch(id, (Vector2)item.position + new Vector2(.5f, .5f));

        static void WriteFixture(string path, bool includeSpeed, bool includeInvalidBlueContent = false)
        {
            if (File.Exists(path)) File.Delete(path);
            using (var stream = File.Create(path))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var root = includeSpeed ? "ScanPro_Importer_Fixture (350ms)/" : "ScanPro_Importer_Fixture_NoSpeed/";
                var images = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                images["basic.png"] = Png(new Mark(12, 12, Blue), new Mark(13, 13, Green),
                    new Mark(15, 12, new Color32(230, 40, 200, 0)));
                images["frames/1.png"] = Png(new Mark(12, 12, Red), new Mark(22, 20, Red), new Mark(23, 20, Red));
                images["frames/2.png"] = Region(23, 20, 2, 1, Yellow);
                images["frames/10.png"] = Region(24, 20, 2, 1, Purple);
                images["blue_count/1/1.png"] = Region(15, 15, 1, 1, Blue);
                images["blue_count/1/2.png"] = Region(16, 15, 1, 1, Green);
                images["blue_count/1/10.png"] = Region(17, 15, 1, 1, Yellow);
                images["blue_count/2+1/1.png"] = Region(19, 15, 1, 1, Red);
                if (includeInvalidBlueContent)
                    images["blue_count/not-a-number/1.png"] = Region(20, 15, 1, 1, Purple);
                images["blue_scan/qr/1.png"] = Region(32, 20, 4, 4, Cyan);
                images["trigger/bot1/from/1.png"] = Region(6, 7, 1, 1, Purple);
                images["trigger/bot1/to(clearwrong).png"] = Region(22, 20, 2, 1, Green);
                images["shutter/bot1_shiftGreen/from/1.png"] = Region(7, 20, 1, 1, Cyan);
                images["shutter/bot1_shiftGreen/to1.png"] = Region(7, 20, 1, 1, Green);
                images["shutter/bot1_shiftGreen/to2.png"] = Region(7, 21, 1, 1, Yellow);
                images["shutter/bot2_shiftRed/from/1.png"] = Region(8, 20, 1, 1, Cyan);
                images["shutter/bot2_shiftRed/to1.png"] = Region(28, 20, 1, 1, Red);
                images["shutter/bot2_shiftRed/to2.png"] = Region(29, 20, 1, 1, Yellow);
                images["yellow/1.png"] = Region(25, 25, 1, 1, Yellow);
                foreach (var pair in images.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    var entry = archive.CreateEntry(root + pair.Key, System.IO.Compression.CompressionLevel.Optimal);
                    using (var output = entry.Open()) output.Write(pair.Value, 0, pair.Value.Length);
                }
            }
        }

        static void WriteStaticMultiRegionFixture(string path)
        {
            if (File.Exists(path)) File.Delete(path);
            using (var stream = File.Create(path))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var images = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["blue_count/1/1.png"] = Png(
                        new Mark(5, 5, Blue), new Mark(9, 5, Green), new Mark(13, 5, Yellow),
                        new Mark(17, 5, Red), new Mark(21, 5, Purple), new Mark(25, 5, Cyan),
                        new Mark(29, 5, Blue), new Mark(33, 5, Green), new Mark(37, 5, Yellow),
                        new Mark(41, 5, Red)),
                    ["blue_scan/station/1.png"] = Png(new Mark(34, 28, Cyan), new Mark(38, 28, Blue))
                };
                foreach (var pair in images.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    var entry = archive.CreateEntry(pair.Key, System.IO.Compression.CompressionLevel.Optimal);
                    using (var output = entry.Open()) output.Write(pair.Value, 0, pair.Value.Length);
                }
            }
        }

        static byte[] Region(int x, int y, int width, int height, Color32 color)
        {
            var marks = new List<Mark>();
            for (var iy = 0; iy < height; iy++) for (var ix = 0; ix < width; ix++) marks.Add(new Mark(x + ix, y + iy, color));
            return Png(marks.ToArray());
        }

        static byte[] Png(params Mark[] marks)
        {
            const int width = 370, height = 270;
            var pixels = new Color32[width * height];
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                pixels[(height - 1 - y) * width + x] = BaseColors[(x / 37 + y / 27) % BaseColors.Length];
            foreach (var mark in marks)
            {
                var sourceX = Mathf.Clamp(Mathf.FloorToInt((mark.X * 10 + 5) * width / 520f), 0, width - 1);
                var sourceY = Mathf.Clamp(Mathf.FloorToInt((mark.Y * 10 + 5) * height / 420f), 0, height - 1);
                pixels[(height - 1 - sourceY) * width + sourceX] = mark.Color;
            }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try { texture.SetPixels32(pixels); texture.Apply(false, false); return texture.EncodeToPNG(); }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        static string[] GetFormalLevelHashes()
        {
            return Directory.GetFiles("Assets/ScanPro/Levels", "Level_*.asset")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => System.Security.Cryptography.SHA256.Create().ComputeHash(File.ReadAllBytes(path)))
                .Select(bytes => BitConverter.ToString(bytes).Replace("-", "")).ToArray();
        }

        static void EnsureAssetFolder(string path)
        {
            var parts = path.Split('/'); var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
