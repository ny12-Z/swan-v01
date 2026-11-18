using System.IO;
using Swan.Game;
using Swan.Game.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Swan.EditorTools
{
    /// <summary>
    /// 生成主场景 + 打包。场景是「生成物」而不是手拖的黑盒：
    /// 在这里改一行，重新跑一次，场景就一致；新人 checkout 下来也能一键还原。
    /// </summary>
    public static class SceneBuilder
    {
        public const string SceneFolder = "Assets/Scenes";
        public const string ScenePath = SceneFolder + "/Main.unity";
        public const string MaterialFolder = "Assets/Materials";
        public const string MaterialPath = MaterialFolder + "/MapSprite.mat";
        public const string SpriteShaderName = "Universal Render Pipeline/2D/Sprite-Unlit-Default";
        public const string FallbackSpriteShaderName = "Sprites/Default";
        public const string BuildFolder = "Builds";
        public const string ExecutableName = "SwanV01.exe";

        [MenuItem("SWAN/1 生成主场景 Main.unity", priority = 1)]
        public static void BuildMainScene()
        {
            EnsureFolder(SceneFolder, "Scenes");
            EnsureFolder(MaterialFolder, "Materials");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            // 相机参数由 config 决定，这里只保证组件存在
            cameraObject.AddComponent<MapCameraController>();

            var root = new GameObject("Swan");
            root.AddComponent<SwanBootstrap>();

            // 地图渲染器放进场景，并把精灵材质序列化上去：
            // SpriteRenderer 没有材质会画成洋红，而打包后 Shader.Find 可能被剔除，
            // 所以「材质作为资源被场景引用」是唯一稳的做法。
            var mapObject = new GameObject("Map");
            mapObject.transform.SetParent(root.transform, false);
            var mapRenderer = mapObject.AddComponent<MapRenderer>();
            Material spriteMaterial = EnsureSpriteMaterial();
            if (spriteMaterial != null)
            {
                var serialized = new SerializedObject(mapRenderer);
                SerializedProperty property = serialized.FindProperty("_spriteMaterial");
                if (property != null)
                {
                    property.objectReferenceValue = spriteMaterial;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
            if (!saved)
            {
                throw new IOException($"场景保存失败：{ScenePath}");
            }

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"[SWAN] 主场景已生成：{ScenePath}");
        }

        /// <summary>精灵材质资源：存在就复用，不存在就用 URP 2D 的 Unlit 着色器建一个。</summary>
        public static Material EnsureSpriteMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (existing != null) return existing;

            Shader shader = Shader.Find(SpriteShaderName) ?? Shader.Find(FallbackSpriteShaderName);
            if (shader == null)
            {
                Debug.LogError($"[SWAN] 找不到精灵着色器（{SpriteShaderName} / {FallbackSpriteShaderName}），地图会画成洋红");
                return null;
            }

            var material = new Material(shader) { name = "MapSprite" };
            AssetDatabase.CreateAsset(material, MaterialPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SWAN] 已创建精灵材质：{MaterialPath}（{shader.name}）");
            return material;
        }

        [MenuItem("SWAN/2 打包 Windows (Builds/SwanV01.exe)", priority = 2)]
        public static void BuildWindows()
        {
            if (!File.Exists(Path.GetFullPath(ScenePath)))
            {
                BuildMainScene();
            }

            // 保险：把精灵着色器加入 Always Included，避免打包时被剔除
            EnsureAlwaysIncludedShader(SpriteShaderName);
            EnsureAlwaysIncludedShader(FallbackSpriteShaderName);

            Directory.CreateDirectory(BuildFolder);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(BuildFolder, ExecutableName).Replace('\\', '/'),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                throw new IOException($"打包失败：{report.summary.result}（{report.summary.totalErrors} 个错误）");
            }

            Debug.Log($"[SWAN] 打包完成：{options.locationPathName}（{report.summary.totalSize / (1024 * 1024)} MB）");
        }

        static void EnsureAlwaysIncludedShader(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null) return;

            var graphicsSettings = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
            if (graphicsSettings == null)
            {
                Debug.LogWarning("[SWAN] 读不到 ProjectSettings/GraphicsSettings.asset，跳过 Always Included 设置");
                return;
            }

            var serialized = new SerializedObject(graphicsSettings);
            SerializedProperty list = serialized.FindProperty("m_AlwaysIncludedShaders");
            if (list == null) return;

            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) return;
            }

            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log($"[SWAN] 已把着色器加入 Always Included：{shaderName}");
        }

        static void EnsureFolder(string path, string leaf)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder("Assets", leaf);
            }
        }
    }
}
