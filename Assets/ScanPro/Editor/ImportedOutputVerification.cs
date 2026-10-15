using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScanPro704.Editor
{
    // Run against an isolated copy: exercises scene references and temporary imported assets.
    public static class ImportedOutputVerification
    {
        static readonly List<string> checks = new List<string>();
        static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            checks.Add("PASS " + description);
        }

        static Dictionary<string, string> AssetHashes(string folder) => Directory.GetFiles(folder, "*.asset")
            .ToDictionary(path => path, path => Convert.ToBase64String(SHA256.Create().ComputeHash(File.ReadAllBytes(path))));

        public static void Run()
        {
            checks.Clear();
            const string drafts = "Assets/ScanPro/LevelDrafts";
            var formalBefore = AssetHashes("Assets/ScanPro/Levels");
            var importedBefore = AssetHashes(drafts);
            AsepriteZipImportVerification.Run();
            Check(AssetHashes("Assets/ScanPro/Levels").OrderBy(pair => pair.Key).SequenceEqual(formalBefore.OrderBy(pair => pair.Key)),
                "Existing importer checks leave the production level assets unchanged");

            var selected = PrototypeBuild.FindOutputLevels();
            Check(selected.Any(ScanProWorkbench.IsDraftAsset), "Existing imported assets are immediately eligible for game output");
            Check(selected.All(level => !level.excludeFromOutput), "Opted-out backup assets are absent from game output");
            Check(selected.Select(level => level.levelNumber).SequenceEqual(selected.Select(level => level.levelNumber).OrderBy(number => number)),
                "Output levels are ordered by their existing level numbers");
            PrototypeBuild.ValidateOutputLevels(selected);
            Check(true, "All selected imported levels pass the normal gameplay and placement validation");
            Check(ScanProWorkbench.SyncPrototypeSceneLevels(Array.Empty<ScanLevel>()), "The scene level list can be safely cleared");
            Check(PrototypeBuild.PrepareCurrentOutputScene().SequenceEqual(selected), "Preparing output repairs an empty scene using the original selected assets");
            Check(ReadSceneLevels().SequenceEqual(selected), "The saved Player scene references the imported assets directly and in order");

            var first = selected.First(ScanProWorkbench.IsDraftAsset);
            first.excludeFromOutput = true;
            Check(!PrototypeBuild.FindOutputLevels().Contains(first), "Unchecking an imported level removes it from the next output selection");
            first.excludeFromOutput = false;
            Check(PrototypeBuild.FindOutputLevels().Contains(first), "Rechecking the same asset restores it without conversion or copying");

            var manual = UnityEngine.Object.Instantiate(first);
            manual.importedPackageName = "";
            var manualPath = AssetDatabase.GenerateUniqueAssetPath(drafts + "/_OutputManualDraft.asset");
            AssetDatabase.CreateAsset(manual, manualPath);
            try
            {
                Check(!PrototypeBuild.FindOutputLevels().Contains(manual), "An unfinished manually created draft is not silently included");
            }
            finally { AssetDatabase.DeleteAsset(manualPath); }

            var fixtureZip = "TestResults/AsepriteImport_20261008/ScanPro_Importer_Fixture (350ms).zip";
            var import = AsepriteZipImporter.Read(fixtureZip, first.venue);
            Check(import.Succeeded, "A fresh ZIP imports successfully without a second conversion step");
            var purple = import.Level.items.Where(item => item.kind == TileKind.HoldPurple).ToArray();
            Check(purple.Length > 0 && purple.All(item => item.purpleClearMask != null && item.purpleClearMask.Pixels.Any(cell => cell)),
                "Every purple object in a new import already contains its source ClearMask");
            Check(import.Summary.Contains("ClearMask"), "The first import reports that ClearMask has already been applied");
            var importedPath = AssetDatabase.GenerateUniqueAssetPath(drafts + "/_OutputImportedFixture.asset");
            AssetDatabase.CreateAsset(import.Level, importedPath);
            try
            {
                Check(PrototypeBuild.FindOutputLevels().Contains(import.Level), "Saving a new imported asset makes it output-ready automatically");
                Check(ScanProWorkbench.LevelPickerLabel(import.Level).Contains("可輸出"), "The workbench identifies imports as output-ready");
                Check(ScanProWorkbench.SyncPrototypeSceneLevels(PrototypeBuild.FindOutputLevels()), "A freshly imported asset can be referenced by the Player scene");
                Check(ReadSceneLevels().Contains(import.Level), "The scene holds the original newly imported asset reference");
                Check(ScanProWorkbench.DeleteEditableAsset(import.Level), "An imported output level can be deleted safely");
                Check(ReadSceneLevels().SequenceEqual(selected), "Deleting an imported level clears only its scene reference");
            }
            finally
            {
                if (AssetDatabase.LoadAssetAtPath<ScanLevel>(importedPath) != null) AssetDatabase.DeleteAsset(importedPath);
                ScanProWorkbench.SyncPrototypeSceneLevels(selected);
            }

            var missingMaskZip = "TestResults/ImportedOutputMissingMask (350ms).zip";
            using (var source = ZipFile.OpenRead(fixtureZip))
            using (var destination = new ZipArchive(File.Create(missingMaskZip), ZipArchiveMode.Create))
            {
                foreach (var entry in source.Entries.Where(entry => !entry.FullName.EndsWith("to(clearwrong).png", StringComparison.OrdinalIgnoreCase)))
                {
                    var copy = destination.CreateEntry(entry.FullName);
                    using (var input = entry.Open()) using (var output = copy.Open()) input.CopyTo(output);
                }
            }
            var missingMask = AsepriteZipImporter.Read(missingMaskZip, first.venue);
            Check(!missingMask.Succeeded && missingMask.Error.Contains("ClearMask"),
                "A purple ZIP missing its clear mask is rejected instead of silently using the old whole-object behavior");
            Check(AssetHashes(drafts).OrderBy(pair => pair.Key).SequenceEqual(importedBefore.OrderBy(pair => pair.Key)),
                "Original imported asset bytes remain unchanged throughout output preparation and import checks");
            Check(AssetHashes("Assets/ScanPro/Levels").OrderBy(pair => pair.Key).SequenceEqual(formalBefore.OrderBy(pair => pair.Key)),
                "No formal copies or sample levels were generated");
            File.WriteAllText("TestResults/imported-output-checks.txt", string.Join("\n", checks) + $"\nTOTAL: {checks.Count} passed\n");
            Debug.Log($"SCANPRO_IMPORTED_OUTPUT_CHECKS_OK: {checks.Count} checks passed.");
        }

        static ScanLevel[] ReadSceneLevels()
        {
            var scene = SceneManager.GetSceneByPath(PrototypeBuild.ScenePath);
            var wasLoaded = scene.IsValid() && scene.isLoaded;
            if (!wasLoaded) scene = EditorSceneManager.OpenScene(PrototypeBuild.ScenePath, OpenSceneMode.Additive);
            try
            {
                return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<PrototypeGame>(true)).Single().levels;
            }
            finally { if (!wasLoaded) EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
