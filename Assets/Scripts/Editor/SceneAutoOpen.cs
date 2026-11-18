using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Swan.EditorTools
{
    /// <summary>
    /// 打开工程时自动载入主场景，避免出现「一打开就是空白工程」的错觉。
    ///
    /// 背景：Unity 打开工程只恢复「上次关闭时开着的场景」，记录写在
    /// Library/LastSceneManagerSetup.txt 里。只要那个记录是空的（batchmode 生成场景、
    /// 在空白场景里关掉编辑器、Library 被重建等都会这样），下次打开就只有一个未命名的
    /// 空白场景 —— 看起来像工程坏了。这里在编辑器加载后补一刀，把主场景打开。
    ///
    /// 三条保护：批量模式不动场景；一个编辑器会话只尝试一次；用户已经打开了任何一个
    /// 保存过的场景就不插手（那说明他有自己的选择）。
    /// </summary>
    [InitializeOnLoad]
    public static class SceneAutoOpen
    {
        /// <summary>主场景路径（同时被打包列表和菜单共用）。</summary>
        public const string MainScenePath = "Assets/Scenes/Main.unity";

        // 一个编辑器会话只自动打开一次：用户在空场景里干活时不要反复把场景抢回来。
        const string SessionKey = "Swan.SceneAutoOpen.Attempted";

        static SceneAutoOpen()
        {
            EditorApplication.delayCall += TryAutoOpen;
        }

        /// <summary>
        /// 判断要不要替用户打开主场景。纯逻辑（不碰编辑器状态），方便单测。
        /// </summary>
        /// <param name="activeScenePath">当前活动场景的资源路径；空白未命名场景是空字符串。</param>
        /// <param name="mainSceneExists">主场景文件是否存在。</param>
        public static bool ShouldAutoOpen(string activeScenePath, bool mainSceneExists)
        {
            if (!mainSceneExists)
            {
                // 场景还没生成过：什么都别做，也别报错（用户可能刚拉下来还没跑生成菜单）。
                return false;
            }

            // 已经有保存过的场景（哪怕不是 Main）就说明用户有自己的选择，不插手。
            return string.IsNullOrEmpty(activeScenePath);
        }

        static void TryAutoOpen()
        {
            if (Application.isBatchMode)
            {
                return;
            }

            if (SessionState.GetBool(SessionKey, false))
            {
                return;
            }

            SessionState.SetBool(SessionKey, true);

            var activeScenePath = SceneManager.GetActiveScene().path;
            if (!ShouldAutoOpen(activeScenePath, File.Exists(MainScenePath)))
            {
                return;
            }

            OpenMainScene();
            Debug.Log($"[SWAN] 已自动打开主场景 {MainScenePath}（上次关闭时没有保存场景记录）。");
        }

        /// <summary>手动打开主场景（菜单用）。</summary>
        [MenuItem("SWAN/0 打开主场景 Main.unity", false, 0)]
        static void OpenMainSceneMenu()
        {
            if (!File.Exists(MainScenePath))
            {
                EditorUtility.DisplayDialog("SWAN", $"主场景不存在：{MainScenePath}\n请先跑 SWAN/1 生成主场景。", "好");
                return;
            }

            OpenMainScene();
        }

        static void OpenMainScene()
        {
            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        }
    }
}
