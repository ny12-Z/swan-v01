// 这个文件放在 scripts 里方便统一管理，但它只给编辑器用：
// 整段用 UNITY_EDITOR 包起来，打包成播放器时会被编译掉，不会引用到 UnityEditor。
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 一键生成「4 方向 × 待机 / 走路」动画：切图 → 8 个 AnimationClip → 1 个 AnimatorController → 挂到场景里的「玩家」。
/// 菜单：工具 / 生成玩家移动动画。重复执行会覆盖旧资源，可以随时重跑。
/// </summary>
public static class PlayerAnimSetup
{
    private const string 贴图路径 = "Assets/角色/Basic Charakter Spritesheet.png";
    private const string 动画目录 = "Assets/角色/动画";
    private const string 控制器路径 = 动画目录 + "/玩家动画.controller";
    private const string 场景路径 = "Assets/Scenes/SampleScene.unity";
    private const float 帧率 = 12f;

    // 和贴图的行顺序一致：第 1 行下、第 2 行上、第 3 行左、第 4 行右
    private static readonly string[] 方向名 = { "Down", "Up", "Left", "Right" };

    [MenuItem("工具/生成玩家移动动画")]
    public static void Generate()
    {
        Dictionary<int, Sprite> 切片 = 加载切片();
        if (切片 == null)
        {
            Debug.LogError("[玩家动画] 没找到 16 张切片，请确认贴图 Sprite Mode = Multiple，且已按 48×48 切成 4 行 4 列。");
            return;
        }

        确保目录();

        AnimationClip[] 待机 = new AnimationClip[4];
        AnimationClip[] 走路 = new AnimationClip[4];

        for (int d = 0; d < 4; d++)
        {
            待机[d] = 生成动画("Idle_" + 方向名[d], new[] { 切片[d * 4] });
            走路[d] = 生成动画("Walk_" + 方向名[d], new[] { 切片[d * 4], 切片[d * 4 + 1], 切片[d * 4 + 2], 切片[d * 4 + 3] });
        }

        AnimatorController 控制器 = 生成控制器(待机, 走路);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        挂到玩家(控制器);

        Debug.Log("[玩家动画] 已生成 8 个 Clip + 1 个 AnimatorController，并挂到场景里的「玩家」身上。");
    }

    // ---- 切片 ----

    private static Dictionary<int, Sprite> 加载切片()
    {
        Object[] 全部资源 = AssetDatabase.LoadAllAssetsAtPath(贴图路径);
        Dictionary<int, Sprite> 结果 = new Dictionary<int, Sprite>();

        foreach (Object 资源 in 全部资源)
        {
            Sprite 切片 = 资源 as Sprite;
            if (切片 == null) continue;

            int 序号;
            int 下划线 = 切片.name.LastIndexOf('_');
            if (下划线 >= 0 && int.TryParse(切片.name.Substring(下划线 + 1), out 序号))
            {
                结果[序号] = 切片;
            }
        }

        return 结果.Count >= 16 ? 结果 : null;
    }

    // ---- 动画剪辑 ----

    private static AnimationClip 生成动画(string 名称, Sprite[] 帧)
    {
        string 路径 = 动画目录 + "/" + 名称 + ".anim";
        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(路径) != null)
        {
            AssetDatabase.DeleteAsset(路径);
        }

        AnimationClip clip = new AnimationClip { name = 名称, frameRate = 帧率 };

        AnimationClipSettings 设置 = AnimationUtility.GetAnimationClipSettings(clip);
        设置.loopTime = true;
        设置.stopTime = 帧.Length / 帧率;
        AnimationUtility.SetAnimationClipSettings(clip, 设置);

        ObjectReferenceKeyframe[] 关键帧 = new ObjectReferenceKeyframe[帧.Length];
        for (int i = 0; i < 帧.Length; i++)
        {
            关键帧[i] = new ObjectReferenceKeyframe { time = i / 帧率, value = 帧[i] };
        }

        // 精灵换图就是给 SpriteRenderer.m_Sprite 打关键帧
        EditorCurveBinding 绑定 = EditorCurveBinding.PPtrCurve(string.Empty, typeof(SpriteRenderer), "m_Sprite");
        AnimationUtility.SetObjectReferenceCurve(clip, 绑定, 关键帧);

        AssetDatabase.CreateAsset(clip, 路径);
        return AssetDatabase.LoadAssetAtPath<AnimationClip>(路径);
    }

    // ---- 状态机 ----

    private static AnimatorController 生成控制器(AnimationClip[] 待机, AnimationClip[] 走路)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(控制器路径) != null)
        {
            AssetDatabase.DeleteAsset(控制器路径);
        }

        AnimatorController 控制器 = AnimatorController.CreateAnimatorControllerAtPath(控制器路径);
        控制器.AddParameter("Speed", AnimatorControllerParameterType.Float);
        控制器.AddParameter("Dir", AnimatorControllerParameterType.Int);

        AnimatorStateMachine 状态机 = 控制器.layers[0].stateMachine;
        AnimatorState[] 待机状态 = new AnimatorState[4];
        AnimatorState[] 走路状态 = new AnimatorState[4];

        for (int d = 0; d < 4; d++)
        {
            待机状态[d] = 状态机.AddState("Idle_" + 方向名[d], new Vector3(280f, 60f + d * 70f, 0f));
            待机状态[d].motion = 待机[d];

            走路状态[d] = 状态机.AddState("Walk_" + 方向名[d], new Vector3(540f, 60f + d * 70f, 0f));
            走路状态[d].motion = 走路[d];
        }

        // 进游戏默认站着朝下
        状态机.defaultState = 待机状态[0];

        // 从「任意状态」切过去：方向对上 + 速度在对应区间，就不用连 20 多条线了
        for (int d = 0; d < 4; d++)
        {
            加任意状态切换(状态机, 走路状态[d], d, true);
            加任意状态切换(状态机, 待机状态[d], d, false);
        }

        return 控制器;
    }

    private static void 加任意状态切换(AnimatorStateMachine 状态机, AnimatorState 目标, int 方向, bool 移动中)
    {
        AnimatorStateTransition 切换 = 状态机.AddAnyStateTransition(目标);
        切换.hasExitTime = false;
        切换.hasFixedDuration = true;
        切换.duration = 0f;
        切换.canTransitionToSelf = false;
        切换.AddCondition(AnimatorConditionMode.Equals, 方向, "Dir");
        切换.AddCondition(移动中 ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less, 0.1f, "Speed");
    }

    // ---- 挂到场景里的玩家 ----

    private static void 挂到玩家(AnimatorController 控制器)
    {
        Scene 场景 = EditorSceneManager.OpenScene(场景路径, OpenSceneMode.Single);
        GameObject 玩家 = GameObject.Find("玩家");

        if (玩家 == null)
        {
            Debug.LogError("[玩家动画] 场景里找不到名为「玩家」的物体，动画资源已生成但没有挂上。");
            return;
        }

        Animator 动画器 = 玩家.GetComponent<Animator>();
        if (动画器 == null)
        {
            动画器 = 玩家.AddComponent<Animator>();
        }

        动画器.runtimeAnimatorController = 控制器;
        动画器.applyRootMotion = false;

        // 顺手把「玩家移动」脚本里的动画器引用填上，省得手动拖；
        // 如果场景还是旧版本、玩家身上没挂这个脚本，就补一个
        玩家移动 移动 = 玩家.GetComponent<玩家移动>();
        if (移动 == null)
        {
            移动 = 玩家.AddComponent<玩家移动>();
            Debug.Log("[玩家动画] 场景里的「玩家」原来没挂「玩家移动」，已自动补上。");
        }

        SerializedObject 序列化 = new SerializedObject(移动);
        SerializedProperty 引用 = 序列化.FindProperty("动画器");
        if (引用 != null)
        {
            引用.objectReferenceValue = 动画器;
            序列化.ApplyModifiedPropertiesWithoutUndo();
        }

        EditorSceneManager.MarkSceneDirty(场景);
        EditorSceneManager.SaveScene(场景);
    }

    private static void 确保目录()
    {
        if (!AssetDatabase.IsValidFolder(动画目录))
        {
            AssetDatabase.CreateFolder("Assets/角色", "动画");
        }
    }
}
#endif
