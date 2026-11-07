using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BattleBlood.EditorTools
{
    /// <summary>
    /// Sets the import rules for everything LAGP made, the project settings, the post-processing asset and the one scene, and builds the player.
    /// The battle itself is built at runtime from a seed. Menu: Tools > Battle Blood > Build Everything, or tools/build_game.py in batch mode.
    /// </summary>
    public static class Builder
    {
        const string Art = "Assets/Game/Resources/Art";
        const string ScenePath = "Assets/Game/Scenes/Main.unity";
        static readonly HashSet<string> Rigged = new() { "infantry", "archer", "cavalry", "horse" };
        static readonly HashSet<string> Looping = new() { "BowAim", "BowAimWalk", "SSIdle", "SSBlockIdle", "SSRun", "Idle", "Walk", "Run", "Jog", "Sprint", "Trot", "SwordIdle", "Driving", "Dizzy", "JumpLoop", "PistolIdle", "PistolAimNeutral" };

        [MenuItem("Tools/Battle Blood/Build Everything")]
        public static void BuildAll()
        {
            ConfigureImports();
            ProjectSetup();
            MakePostProfile();
            KeepInstancingVariants();
            MakeScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[BUILD] OK everything built");
        }

        public static void BuildPlayerBatch()
        {
            var args = System.Environment.GetCommandLineArgs();
            string output = "Build/BattleBlood.exe";
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-bboutput") output = args[i + 1];
            if (!args.Contains("-bbskipcontent")) BuildAll();
            BuildPlayer(output);
        }

        public static string BuildInEditor(string output, bool skipContent, string logFile)
        {
            var lines = new System.Text.StringBuilder();
            void Keep(string text, string stack, LogType type) { lines.AppendLine(text); if (type == LogType.Exception) lines.AppendLine(stack); }
            Application.logMessageReceived += Keep;
            try
            {
                if (!skipContent) BuildAll();
                BuildPlayer(output);
            }
            finally
            {
                Application.logMessageReceived -= Keep;
                File.WriteAllText(logFile, lines.ToString());
            }
            return "built";
        }

        public static void BuildPlayer(string output)
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = output, target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
            Debug.Log($"[BUILD] player {report.summary.result} size {report.summary.totalSize / (1024 * 1024)} MB errors {report.summary.totalErrors}");
            if (report.summary.result != BuildResult.Succeeded) throw new System.Exception("player build failed");
        }

        // ---- imports ----

        static void ConfigureImports()
        {
            // Bring in files changed on disk first: a model's clip list must be read from the file as it is now, not as it was.
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.StartAssetEditing();
            try
            {
                if (Directory.Exists($"{Art}/Models"))
                {
                    foreach (var p in Directory.GetFiles($"{Art}/Models", "*.fbx")) Model(p.Replace('\\', '/'));
                    foreach (var p in Directory.GetFiles($"{Art}/Models", "*.png")) Tex(p.Replace('\\', '/'), 2048, false);
                }
                if (Directory.Exists($"{Art}/Textures"))
                    foreach (var p in Directory.GetFiles($"{Art}/Textures", "*.png")) Tex(p.Replace('\\', '/'), 2048, Path.GetFileName(p).StartsWith("sky_"));
                if (Directory.Exists($"{Art}/UI"))
                    foreach (var p in Directory.GetFiles($"{Art}/UI", "*.png")) Ui(p.Replace('\\', '/'));
                if (Directory.Exists($"{Art}/Audio"))
                    foreach (var p in Directory.GetFiles($"{Art}/Audio", "*.ogg")) Sound(p.Replace('\\', '/'));
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh();
        }

        static void Model(string path)
        {
            var m = AssetImporter.GetAtPath(path) as ModelImporter;
            if (m == null) { Debug.LogError("[BUILD] no model " + path); return; }
            string name = Path.GetFileNameWithoutExtension(path);
            bool rigged = Rigged.Contains(name);
            m.materialImportMode = ModelImporterMaterialImportMode.None;
            // Realistic look: the pipeline's faceted normals are recalculated smooth, for this game only.
            m.importNormals = ModelImporterNormals.Calculate;
            m.normalCalculationMode = ModelImporterNormalCalculationMode.AreaAndAngleWeighted;
            m.normalSmoothingAngle = name.StartsWith("rock") || name == "ruin" ? 55f : 80f;
            m.importTangents = ModelImporterTangents.None;
            m.clipAnimations = new ModelImporterClipAnimation[0];    // forget the clips of the file as it was; the defaults below are this file's
            m.isReadable = true;                // severed parts are cut from the soldiers' meshes at runtime; props are stood upright and get arrow colliders
            if (rigged)
            {
                m.animationType = ModelImporterAnimationType.Generic;
                m.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                m.optimizeGameObjects = false;
                m.importAnimation = true;
                m.resampleCurves = false;
                var clips = m.defaultClipAnimations;
                foreach (var c in clips)
                {
                    string shortName = ClipName(c.name);
                    c.name = shortName;
                    c.loopTime = Looping.Contains(shortName);
                    c.loopPose = false;
                    c.lockRootRotation = true; c.lockRootHeightY = false; c.lockRootPositionXZ = false;
                    c.keepOriginalOrientation = true; c.keepOriginalPositionXZ = true; c.keepOriginalPositionY = true;
                }
                m.clipAnimations = clips;
            }
            else
            {
                m.animationType = ModelImporterAnimationType.None;
                m.importAnimation = false;
            }
            m.SaveAndReimport();
        }

        static string ClipName(string raw)
        {
            string s = raw;
            int bar = s.LastIndexOf('|');
            if (bar >= 0) s = s[(bar + 1)..];
            int dot = s.IndexOf('.');
            if (dot > 0) s = s[..dot];
            return s;
        }

        static void Tex(string path, int max, bool readable)
        {
            var t = AssetImporter.GetAtPath(path) as TextureImporter;
            if (t == null) return;
            bool normal = path.EndsWith("_n.png");
            bool card = path.Contains("_card") || path.EndsWith("banner.png") || path.EndsWith("feather.png");
            t.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            t.wrapMode = card || path.Contains("blood_") && !path.EndsWith("blood_tile.png") ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            t.mipmapEnabled = true;
            t.anisoLevel = normal ? 4 : 8;
            t.maxTextureSize = path.EndsWith("/Models/oak.png") ? 4096 : max;
            t.sRGBTexture = !normal && !path.EndsWith("blood_tile.png");
            t.alphaIsTransparency = card;
            t.mipMapsPreserveCoverage = card;
            if (card) t.alphaTestReferenceValue = 0.4f;
            t.isReadable = readable;
            // The ground tiles and their variants go into one texture array per ground type: every slice must have the same format.
            string file = Path.GetFileNameWithoutExtension(path);
            foreach (var g in new[] { "grass", "grass_dry", "dirt", "mud", "rock", "moss" })
                if (!normal && (file == g || System.Text.RegularExpressions.Regex.IsMatch(file, "^" + g + @"_v\d+$"))) t.alphaSource = TextureImporterAlphaSource.None;
            if (normal) t.alphaSource = TextureImporterAlphaSource.FromInput;      // every normal map the same compressed format
            t.textureCompression = TextureImporterCompression.Compressed;
            t.SaveAndReimport();
        }

        static void Ui(string path)
        {
            var t = AssetImporter.GetAtPath(path) as TextureImporter;
            if (t == null) return;
            t.textureType = TextureImporterType.Default;
            t.alphaIsTransparency = true;
            t.mipmapEnabled = true;
            t.wrapMode = TextureWrapMode.Clamp;
            t.maxTextureSize = 2048;
            t.SaveAndReimport();
        }

        static void Sound(string path)
        {
            var a = AssetImporter.GetAtPath(path) as AudioImporter;
            if (a == null) return;
            var s = a.defaultSampleSettings;
            bool longOne = path.Contains("music") || path.Contains("wind") || path.Contains("swamp") || path.Contains("melee") || path.Contains("gallop") || path.Contains("fire_");
            s.loadType = longOne ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.7f;
            a.defaultSampleSettings = s;
            a.SaveAndReimport();
        }

        // ---- settings ----

        static void ProjectSetup()
        {
            PlayerSettings.productName = "Battle Blood";
            PlayerSettings.companyName = "LAGP";
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            if (asset == null) { Debug.LogError("[BUILD] missing Assets/Settings/PC_RPAsset.asset"); return; }
            GraphicsSettings.defaultRenderPipeline = asset;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            asset.shadowDistance = 95f;
            asset.shadowCascadeCount = 3;
            asset.supportsCameraOpaqueTexture = false;
            asset.supportsCameraDepthTexture = true;
            asset.msaaSampleCount = 2;
            EditorUtility.SetDirty(asset);
        }

        static void MakePostProfile()
        {
            const string path = "Assets/Game/Resources/post.asset";
            AssetDatabase.DeleteAsset(path);
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, path);
            var tone = p.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.ACES);
            var bloom = p.Add<Bloom>(true); bloom.intensity.Override(0.55f); bloom.threshold.Override(1f); bloom.scatter.Override(0.7f);
            var grade = p.Add<ColorAdjustments>(true); grade.contrast.Override(14f); grade.saturation.Override(-6f); grade.postExposure.Override(0.05f);
            var split = p.Add<SplitToning>(true); split.shadows.Override(new Color(.46f, .52f, .56f)); split.highlights.Override(new Color(.62f, .55f, .47f)); split.balance.Override(0f);
            var vig = p.Add<Vignette>(true); vig.intensity.Override(0.28f); vig.smoothness.Override(0.45f); vig.color.Override(Color.black);
            var grain = p.Add<FilmGrain>(true); grain.type.Override(FilmGrainLookup.Medium2); grain.intensity.Override(0.22f); grain.response.Override(0.75f);
            var chroma = p.Add<ChromaticAberration>(true); chroma.intensity.Override(0.07f);
            var blur = p.Add<MotionBlur>(true); blur.mode.Override(MotionBlurMode.CameraAndObjects); blur.quality.Override(MotionBlurQuality.Medium); blur.intensity.Override(0f); blur.clamp.Override(0.1f);
            var dof = p.Add<DepthOfField>(true); dof.mode.Override(DepthOfFieldMode.Gaussian); dof.gaussianStart.Override(30f); dof.gaussianEnd.Override(80f); dof.gaussianMaxRadius.Override(0f);
            foreach (var c in p.components) AssetDatabase.AddObjectToAsset(c, p);
            EditorUtility.SetDirty(p);
        }

        /// <summary>
        /// Materials made at runtime do not keep their shader's instancing variant in a build: with nothing in the project using it, it is stripped,
        /// and instanced draws of that shader silently draw nothing. One saved material per instanced shader, and "keep all" for instancing.
        /// </summary>
        static void KeepInstancingVariants()
        {
            var gs = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var strip = gs.FindProperty("m_InstancingStripping");
            if (strip != null) { strip.intValue = 2; gs.ApplyModifiedPropertiesWithoutUndo(); }
            const string dir = "Assets/Game/Resources/KeepVariants";
            Directory.CreateDirectory(dir);
            foreach (var n in new[] { "BBRing", "BBDecal", "BBFoliage", "BBLit", "BBLeaves" })
            {
                string path = $"{dir}/{n}.mat";
                var shader = AssetDatabase.LoadAssetAtPath<Shader>($"Assets/Game/Resources/Shaders/{n}.shader");
                if (shader == null) { Debug.LogError("[BUILD] missing shader " + n); continue; }
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
                m.shader = shader;
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
            }
            Debug.Log("[BUILD] instancing variants kept");
        }

        static void MakeScene()
        {
            Directory.CreateDirectory("Assets/Game/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Boot").AddComponent<Boot>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
