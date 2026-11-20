// 这个文件放在 scripts 里方便统一管理，但它只给编辑器用：
// 整段用 UNITY_EDITOR 包起来，打包成播放器时会被编译掉，不会引用到 UnityEditor。
#if UNITY_EDITOR
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 在 SampleScene 里搭一套相机跟随：
/// 新建空对象「相机管理」，「Main Camera」和 Cinemachine 的「跟随相机」都挂到它下面，
/// 主相机加 CinemachineBrain，跟随相机跟随玩家。
/// 菜单：工具 / 搭建相机跟随。重复执行不会重复创建。
/// </summary>
public static class 相机跟随
{
    const string 场景路径 = "Assets/Scenes/SampleScene.unity";
    const string 管理名字 = "相机管理";
    const string 跟随相机名字 = "跟随相机";
    const string 玩家名字 = "玩家";

    [MenuItem("工具/搭建相机跟随")]
    public static void Setup()
    {
        var 场景 = EditorSceneManager.OpenScene(场景路径);

        // 1. 管理用的空对象
        var 管理 = GameObject.Find(管理名字);
        if (管理 == null)
        {
            管理 = new GameObject(管理名字);
            Undo.RegisterCreatedObjectUndo(管理, "创建相机管理");
        }

        管理.transform.position = Vector3.zero;

        // 2. 主相机挂到管理对象下，并加上 CinemachineBrain
        var 主相机 = Camera.main;
        if (主相机 == null)
        {
            var 相机物体 = GameObject.Find("Main Camera");
            主相机 = 相机物体 != null ? 相机物体.GetComponent<Camera>() : null;
        }

        if (主相机 == null)
        {
            Debug.LogError("[相机跟随] 场景里找不到 Main Camera，先建一个主相机再运行。");
            return;
        }

        主相机.transform.SetParent(管理.transform, true);

        if (主相机.GetComponent<CinemachineBrain>() == null)
            主相机.gameObject.AddComponent<CinemachineBrain>();

        // 3. Cinemachine 跟随相机
        var 跟随物体 = GameObject.Find(跟随相机名字);
        if (跟随物体 == null)
        {
            跟随物体 = new GameObject(跟随相机名字);
            跟随物体.transform.SetParent(管理.transform, false);
        }
        else if (跟随物体.transform.parent != 管理.transform)
        {
            跟随物体.transform.SetParent(管理.transform, true);
        }

        var 玩家 = GameObject.Find(玩家名字);
        if (玩家 != null)
        {
            跟随物体.transform.position = 玩家.transform.position + new Vector3(0f, 0f, -10f);
        }
        else
        {
            Debug.LogWarning("[相机跟随] 场景里没找到「玩家」，跟随目标留空，请手动拖一下。");
            跟随物体.transform.position = new Vector3(0f, 0f, -10f);
        }

        var 虚拟相机 = 跟随物体.GetComponent<CinemachineCamera>();
        if (虚拟相机 == null)
            虚拟相机 = 跟随物体.AddComponent<CinemachineCamera>();

        // 镜头参数直接抄主相机：正交、Size 5，切到 Cinemachine 画面不会跳
        虚拟相机.Lens = LensSettings.FromCamera(主相机);

        // 位置跟随：2D 只需要 x/y 有跟随，z 保持 -10
        var 跟随 = 跟随物体.GetComponent<CinemachineFollow>();
        if (跟随 == null)
            跟随 = 跟随物体.AddComponent<CinemachineFollow>();

        跟随.FollowOffset = new Vector3(0f, 0f, -10f);
        跟随.TrackerSettings.PositionDamping = new Vector3(0.15f, 0.15f, 0f);

        if (玩家 != null)
            虚拟相机.Target.TrackingTarget = 玩家.transform;

        EditorSceneManager.MarkSceneDirty(场景);
        EditorSceneManager.SaveScene(场景);

        Debug.Log("[相机跟随] 完成：相机管理 → Main Camera（CinemachineBrain）+ 跟随相机（CinemachineCamera + CinemachineFollow，跟随「玩家」）");
    }
}

/// <summary>
/// 给 Unity 批处理用的入口（-executeMethod 用 ASCII 名字更稳）：
/// Unity.exe -batchmode -quit -executeMethod CameraFollowBatch.Run
/// </summary>
public static class CameraFollowBatch
{
    public static void Run()
    {
        相机跟随.Setup();
    }
}
#endif
