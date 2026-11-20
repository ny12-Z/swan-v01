using UnityEngine;

/// <summary>
/// 一株已经种在地里的作物（纯运行时数据，不存档、不序列化）。
///
/// 只存「种在哪、是什么、长了多久、现在显示第几帧、谁在画」，
/// 判定成熟 / 取帧这些规则都在 作物定义 里，状态本身不做业务决策。
/// </summary>
public class 作物状态
{
    /// <summary>种在哪一格。</summary>
    public Vector2Int 坐标;

    /// <summary>品种规格书。</summary>
    public 作物定义 定义;

    /// <summary>已经长了多少秒。</summary>
    public float 已生长秒;

    /// <summary>当前显示第几帧（0 起）。</summary>
    public int 阶段;

    /// <summary>画这株作物的渲染器（场景里那个小 Sprite）。</summary>
    public SpriteRenderer 渲染;

    /// <summary>是否成熟，可以收获。</summary>
    public bool 已成熟 => 定义 != null && 定义.已成熟(已生长秒);

    /// <summary>生长进度 0~1，给以后做进度条 / 提示用。</summary>
    public float 进度
    {
        get
        {
            if (定义 == null || 定义.成熟秒数 <= 0f) return 1f;
            return Mathf.Clamp01(已生长秒 / 定义.成熟秒数);
        }
    }
}
