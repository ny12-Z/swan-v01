using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD 里的一个道具槽位。一个槽位 = 底框（常显）+ 高亮（选中时显示）+ 图标（道具图）。
/// 三个字段都填同一类 UI 组件，编辑器工具 搭建HUD 会自动接好，手改也不会出错。
/// </summary>
[Serializable]
public class HUD槽位
{
    [Tooltip("这个槽位代表哪个道具，要和 互动道具 里的顺序对得上")]
    public 互动道具 道具 = 互动道具.锄头;

    [Tooltip("槽位底框（常显），一般是 HUD槽位 这张九宫格图")]
    public Image 底框;

    [Tooltip("选中时显示的高亮层，一般用颜色更深的槽位图，默认隐藏")]
    public Image 高亮;

    [Tooltip("道具图标，选中时变亮、未选中时半透明")]
    public Image 图标;
}

/// <summary>
/// 工具栏 HUD：始终显示在画面左下角，标出当前拿在手上的道具。
///
/// 它只负责「显示」，不处理输入——道具是滚轮切的，逻辑在 玩家互动 里；
/// 这里每帧读 玩家互动.当前手持道具 / 正在互动，把对应槽位点亮。
///
/// 层级约定（Scene 里，由 工具 / 搭建HUD 生成）：
///   HUD                        ← 空物体，用来收纳整套 UI
///   └── HUD画布                 ← Canvas + CanvasScaler + 本脚本
///       └── 工具栏               ← 面板底图 + CanvasGroup
///           ├── 槽位_锄头  ├── 高亮 / 图标
///           ├── 槽位_斧头  ├── 高亮 / 图标
///           ├── 槽位_水壶  ├── 高亮 / 图标
///           ├── 槽位_播种  ├── 高亮 / 图标（图按当前种子换：小麦 / 苹果）
///           └── 按键提示  └── 文字「F」
/// </summary>
[DisallowMultipleComponent]
public class 玩家HUD : MonoBehaviour
{
    [Header("引用")]
    [SerializeField, Tooltip("玩家互动组件（读当前道具 / 是否在互动），留空会在运行时自动找")]
    private 玩家互动 互动;

    [SerializeField, Tooltip("工具栏整体用的 CanvasGroup，没在互动时整块变淡")]
    private CanvasGroup 整体;

    [SerializeField, Tooltip("作物系统：播种槽位的图标跟着当前选中的种子换（小麦 / 苹果）")]
    private 作物系统 作物;

    [Header("槽位（按 互动道具 顺序：锄头 / 斧头 / 水壶）")]
    [SerializeField] private HUD槽位[] 槽位列表 = new HUD槽位[0];

    [Header("外观")]
    [SerializeField, Tooltip("选中时图标颜色")]
    private Color 选中图标色 = Color.white;

    [SerializeField, Tooltip("未选中时图标颜色（半透明，让选中的那个跳出来）")]
    private Color 未选中图标色 = new Color(1f, 1f, 1f, 0.45f);

    [SerializeField, Tooltip("选中时槽位放大的倍数")]
    private float 选中缩放 = 1.1f;

    [SerializeField, Tooltip("没拿工具时整块 HUD 的透明度")]
    private float 未互动透明度 = 0.7f;

    [SerializeField, Tooltip("拿着工具时整块 HUD 的透明度")]
    private float 互动中透明度 = 1f;

    [SerializeField, Tooltip("高亮 / 透明度的过渡速度，越大越跟手")]
    private float 过渡速度 = 14f;

    /// <summary>当前手上道具（只读，方便别的 UI 显示）。</summary>
    public 互动道具 当前道具 => 互动 != null ? 互动.当前手持道具 : 互动道具.锄头;

    private void Awake()
    {
        if (互动 == null) 互动 = 查找互动组件();
        if (整体 == null) 整体 = GetComponentInChildren<CanvasGroup>(true);
        if (作物 == null) 作物 = 作物系统.当前 != null ? 作物系统.当前 : FindAnyObjectByType<作物系统>();

        // 第一帧直接到位，避免开局看到一次从透明到不透明的闪动
        刷新(立即: true);
    }

    private void Update()
    {
        刷新(立即: false);
    }

    /// <summary>重新算一遍高亮与透明度。立即 = true 时不做过渡，直接到目标值。</summary>
    public void 刷新(bool 立即)
    {
        if (槽位列表 == null) return;

        float 步长 = 立即 ? 1f : Mathf.Clamp01(Time.unscaledDeltaTime * 过渡速度);

        互动道具 选中道具 = 当前道具;
        bool 互动中 = 互动 != null && 互动.正在互动;

        for (int i = 0; i < 槽位列表.Length; i++)
        {
            HUD槽位 槽位 = 槽位列表[i];
            if (槽位 == null) continue;

            // 播种槽位的图标跟着当前选中的种子换（小麦 / 苹果），其它槽位不动
            if (槽位.道具 == 互动道具.播种 && 作物 != null && 槽位.图标 != null)
            {
                Sprite 种子图 = 作物.当前定义 != null ? 作物.当前定义.图标 : null;
                if (种子图 != null && 槽位.图标.sprite != 种子图) 槽位.图标.sprite = 种子图;
            }

            bool 选中 = 槽位.道具 == 选中道具;

            if (槽位.高亮 != null && 槽位.高亮.enabled != 选中)
            {
                槽位.高亮.enabled = 选中;
            }

            if (槽位.图标 != null)
            {
                Color 目标色 = 选中 ? 选中图标色 : 未选中图标色;
                槽位.图标.color = 立即 ? 目标色 : Color.Lerp(槽位.图标.color, 目标色, 步长);
            }

            if (槽位.底框 != null)
            {
                float 目标缩放 = 选中 ? 选中缩放 : 1f;
                float 当前缩放 = 槽位.底框.rectTransform.localScale.x;
                float 新缩放 = 立即 ? 目标缩放 : Mathf.Lerp(当前缩放, 目标缩放, 步长);
                槽位.底框.rectTransform.localScale = new Vector3(新缩放, 新缩放, 1f);
            }
        }

        if (整体 != null)
        {
            float 目标透明度 = 互动中 ? 互动中透明度 : 未互动透明度;
            整体.alpha = 立即 ? 目标透明度 : Mathf.Lerp(整体.alpha, 目标透明度, 步长);
        }
    }

    // 场景里只有一个玩家，找不到引用时按类型兜底找一次
    private static 玩家互动 查找互动组件()
    {
        return UnityEngine.Object.FindAnyObjectByType<玩家互动>(FindObjectsInactive.Include);
    }
}
