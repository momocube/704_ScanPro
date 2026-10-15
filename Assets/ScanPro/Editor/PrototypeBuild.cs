using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScanPro704.Editor
{
    [InitializeOnLoad]
    public static class PrototypeBuild
    {
        const string Root = "Assets/ScanPro";
        public const string ScenePath = Root + "/Scenes/PrototypeGame.unity";
        public const string ProfilePath = Root + "/Data/704_Output.asset";
        static PrototypeBuild() => EditorApplication.playModeStateChanged += OnPlayModeChanged;

        internal static bool IsOutputCandidate(ScanLevel level)
        {
            if (level == null) return false;
            var path = AssetDatabase.GetAssetPath(level);
            return path.StartsWith(Root + "/Levels/", StringComparison.OrdinalIgnoreCase) ||
                (path.StartsWith(Root + "/LevelDrafts/", StringComparison.OrdinalIgnoreCase) &&
                 !string.IsNullOrWhiteSpace(level.importedPackageName));
        }

        internal static ScanLevel[] FindOutputLevels()
        {
            var folders = new[] { Root + "/Levels", Root + "/LevelDrafts" }
                .Where(AssetDatabase.IsValidFolder).ToArray();
            if (folders.Length == 0) return Array.Empty<ScanLevel>();
            return AssetDatabase.FindAssets("t:ScanLevel", folders)
                .Select(guid => AssetDatabase.LoadAssetAtPath<ScanLevel>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(level => IsOutputCandidate(level) && !level.excludeFromOutput)
                .OrderBy(level => level.levelNumber)
                .ThenBy(level => AssetDatabase.GetAssetPath(level), StringComparer.Ordinal)
                .ToArray();
        }

        internal static void ValidateOutputLevels(ScanLevel[] levels)
        {
            if (levels == null || levels.Length == 0)
                throw new InvalidOperationException("沒有可輸出的關卡。請匯入 ZIP 並勾選「納入遊戲輸出」。");
            foreach (var level in levels)
            {
                if (level.levelNumber < 1 || level.levelNumber > 50)
                    throw new InvalidOperationException(level.DisplayTitle + "：輸出關號須介於 1～50。");
                var errors = LevelValidation.Errors(level);
                if (errors.Count > 0)
                    throw new InvalidOperationException($"{level.DisplayTitle}（{AssetDatabase.GetAssetPath(level)}）：\n" + string.Join("\n", errors));
            }
        }

        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode) return;
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded) return;
            try
            {
                var levels = FindOutputLevels();
                ValidateOutputLevels(levels);
                foreach (var game in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<PrototypeGame>(true)))
                    game.levels = levels;
            }
            catch (Exception exception)
            {
                EditorApplication.isPlaying = false;
                Debug.LogError("無法開始試玩：" + exception.Message);
            }
        }

        [MenuItem("704/開啟遊戲輸出場景")]
        public static void OpenCurrentOutputScene()
        {
            if (PrepareCurrentOutputScene() != null) EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("704/建立範例試玩場景（測試用）")]
        public static void Prepare()
        {
            ProjectSetup.Initialize();
            var venue = AssetDatabase.LoadAssetAtPath<VenueLayout>(Root + "/Data/704_Venue.asset");
            var profile = AssetDatabase.LoadAssetAtPath<OutputProfile>(ProfilePath);
            if (profile == null) { profile = ScriptableObject.CreateInstance<OutputProfile>(); profile.editorLayout = venue; profile.Configure704(); AssetDatabase.CreateAsset(profile, ProfilePath); }
            if (profile.presentationShader == null) profile.presentationShader = Shader.Find("704/Fullscreen Fit");
            if(!profile.Validate(out var profileError)) throw new Exception(profileError);
            if (profile.presentationShader == null) throw new Exception("704 fullscreen aspect-fit shader is missing.");
            CreatePro(4, "Pro 數字排序", ProMechanic.Numbers7, venue);
            CreatePro(5, "Pro 配對合作", ProMechanic.Pairs7, venue);
            foreach (var guid in AssetDatabase.FindAssets("t:ScanLevel", new[] { Root + "/Levels" }))
            {
                var level = AssetDatabase.LoadAssetAtPath<ScanLevel>(AssetDatabase.GUIDToAssetPath(guid));
                var errors = LevelValidation.Errors(level);
                if (errors.Count > 0) throw new Exception($"Level {level.levelNumber}: " + string.Join("\n", errors));
            }
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var previous = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var go = new GameObject("704 Prototype"); var game = go.AddComponent<PrototypeGame>();
                game.levels = AssetDatabase.FindAssets("t:ScanLevel", new[] { Root + "/Levels" }).Select(g => AssetDatabase.LoadAssetAtPath<ScanLevel>(AssetDatabase.GUIDToAssetPath(g))).OrderBy(l => l.levelNumber).ToArray();
                game.output = profile; game.outputShader = Shader.Find("704/Output Atlas"); game.caveShader = Shader.Find("704/Cave Surface");
                var camera = new GameObject("Main Camera").AddComponent<Camera>(); camera.tag="MainCamera"; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                game.outputCamera = camera;
                var presenter = camera.gameObject.AddComponent<VenueOutputPresenter>();
                presenter.presentationShader = profile.presentationShader; presenter.enabled=false;
                if (presenter.presentationShader == null) throw new Exception("704 fullscreen aspect-fit shader is missing.");
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            finally { if (!Application.isBatchMode && previous.All(s => !string.IsNullOrEmpty(s.path))) EditorSceneManager.RestoreSceneManagerSetup(previous); }
            PlayerSettings.defaultScreenWidth = 960; PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true; PlayerSettings.allowFullscreenSwitch = false;
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets(); Debug.Log("SCANPRO_PREPARED: 5 levels and calibrated pixel output profile.");
        }
        static void CreatePro(int number, string label, ProMechanic mechanic, VenueLayout venue)
        {
            var path = Root + $"/Levels/Level_{number:00}.asset";
            if (AssetDatabase.LoadAssetAtPath<ScanLevel>(path) != null) return;
            var level = ScriptableObject.CreateInstance<ScanLevel>(); level.venue = venue; level.levelNumber = number; level.displayName = label; level.proMechanic = mechanic;
            for (var i = 0; i < 15; i++) level.items.Add(new LevelItem { kind = TileKind.TouchBlue, position = new Vector2Int(11 + (i % 8) * 3, 33 + (i / 8) * 4), size = new Vector2Int(2,2), proTarget = true });
            level.items.Add(new LevelItem { kind = TileKind.ScanBlue, position = new Vector2Int(33,20), size = new Vector2Int(4,4) });
            level.items.Add(new LevelItem { kind = TileKind.SafeGreen, position = new Vector2Int(11,11), size = new Vector2Int(30,3) });
            AssetDatabase.CreateAsset(level, path);
        }
        [MenuItem("704/輸出 704 雙螢幕遊戲")]
        public static void BuildWindows() => BuildCurrentLevelsFromWorkbench();

        /// <summary>References imported assets directly, without publishing copies or initializing sample levels.</summary>
        internal static ScanLevel[] PrepareCurrentOutputScene()
        {
            if (!File.Exists(ScenePath)) throw new FileNotFoundException("目前遊戲輸出場景不存在。", ScenePath);
            var profile = AssetDatabase.LoadAssetAtPath<OutputProfile>(ProfilePath);
            if (profile == null) throw new InvalidOperationException("場域輸出設定不存在。");
            if (!profile.Validate(out var profileError)) throw new InvalidOperationException(profileError);
            if (profile.presentationShader == null)
                throw new InvalidOperationException("場域輸出設定尚未引用全螢幕縮放 Shader；請在 704_Output 設定 704/Fullscreen Fit 後再 Build。");

            var levels = FindOutputLevels();
            ValidateOutputLevels(levels);
            if (!ScanProWorkbench.SyncPrototypeSceneLevels(levels)) return null;

            // Verify the saved scene now references the selected original assets in playback order.
            var sceneText = File.ReadAllText(ScenePath);
            var levelsBlock = Regex.Match(sceneText, @"(?ms)^\s*levels:\s*(?<refs>.*?)^\s*output:");
            if (!levelsBlock.Success) throw new InvalidOperationException("試玩場景找不到關卡引用。");
            var referencedGuids = Regex.Matches(levelsBlock.Groups["refs"].Value, @"guid:\s*([0-9a-fA-F]{32})")
                .Cast<Match>().Select(match => match.Groups[1].Value.ToLowerInvariant()).ToArray();
            var expectedGuids = levels.Select(level => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(level)).ToLowerInvariant()).ToArray();
            if (!referencedGuids.SequenceEqual(expectedGuids))
                throw new InvalidOperationException("試玩場景引用與選定輸出關卡不一致，已停止建置。");
            var profileGuid = AssetDatabase.AssetPathToGUID(ProfilePath);
            if (string.IsNullOrEmpty(profileGuid) || !sceneText.Contains("guid: " + profileGuid))
                throw new InvalidOperationException("試玩場景未引用目前的 704 輸出設定，已停止建置。");

            foreach (var level in levels)
                if (EditorUtility.IsDirty(level)) AssetDatabase.SaveAssetIfDirty(level);
            if (GameColorSettings.Shared != null) AssetDatabase.SaveAssetIfDirty(GameColorSettings.Shared);
            return levels;
        }

        public static void BuildCurrentLevelsFromWorkbench()
        {
            var levels = PrepareCurrentOutputScene();
            if (levels == null) return;

            var date = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Taipei Standard Time").ToString("yyyy-MM-dd");
            var baseName = date + "_704_CurrentLevels_MultiWindow_DualScreen";
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var outputDirectory = Path.Combine(projectRoot, "Builds", baseName);
            var suffix = 2;
            while (Directory.Exists(outputDirectory))
                outputDirectory = Path.Combine(projectRoot, "Builds", baseName + "_" + (suffix++).ToString("00"));
            var args = Environment.GetCommandLineArgs();
            var outputIndex = Array.IndexOf(args, "--build-output");
            if (outputIndex >= 0 && outputIndex + 1 < args.Length)
            {
                outputDirectory = Path.GetFullPath(args[outputIndex + 1]);
                if (Directory.Exists(outputDirectory) && Directory.EnumerateFileSystemEntries(outputDirectory).Any())
                    throw new InvalidOperationException("指定的輸出資料夾已有檔案，請使用新資料夾：" + outputDirectory);
            }
            Directory.CreateDirectory(outputDirectory);

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(outputDirectory, "ScanPro704.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("目前輸出關卡 Build 失敗：" + report.summary.result);
            File.WriteAllText(Path.Combine(outputDirectory, "啟動場域輸出.cmd"),
                "@echo off\r\ncd /d \"%~dp0\"\r\nstart \"\" \"ScanPro704.exe\"\r\n");
            File.WriteAllLines(Path.Combine(outputDirectory, "關卡清單.txt"),
                levels.Select(level => level.DisplayTitle + " | " + AssetDatabase.GetAssetPath(level)));
            Debug.Log("SCANPRO_CURRENT_LEVELS_BUILD_OK: " + outputDirectory);
            if (!Application.isBatchMode)
                EditorUtility.DisplayDialog("Build 完成", $"{levels.Length} 個關卡已直接輸出至：\n" + outputDirectory, "確定");
        }
    }
}
