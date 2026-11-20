using UnityEngine;

/// <summary>能种的作物品种。顺序就是数字键 1 / 2 的顺序。</summary>
public enum 作物品种
{
    小麦 = 0,
    苹果 = 1,
}

/// <summary>
/// 一种作物的「规格书」：长什么样、几帧、多久成熟、收获什么。
/// 一个品种一个资产，放在 Assets/数据/作物/ 下；要加新作物就再建一个，不用改代码。
/// </summary>
[CreateAssetMenu(fileName = "作物定义", menuName = "种田/作物定义", order = 30)]
public class 作物定义 : ScriptableObject
{
    [Header("身份")]
    public 作物品种 品种 = 作物品种.小麦;

    [Tooltip("显示名，以后给背包 / 提示条用")]
    public string 显示名 = "小麦";

    [Header("贴图（全部来自 Basic_Plants.png）")]
    [Tooltip("HUD 上显示的种子图标（素材左一列）")]
    public Sprite 图标;

    [Tooltip("成长帧，从左到右；最后一帧就是成熟的样子")]
    public Sprite[] 成长帧 = new Sprite[0];

    [Tooltip("收获物贴图（暂时不用，给以后做背包 / 掉落留的）")]
    public Sprite 收获物;

    [Header("节奏")]
    [Tooltip("从种下到成熟要多少秒（默认 4 分钟）")]
    public float 成熟秒数 = 240f;

    // ---------- 只读计算 ----------

    public int 帧数 => 成长帧 == null ? 0 : 成长帧.Length;

    /// <summary>每一帧占多少秒（4 帧 × 60 秒 = 4 分钟）。</summary>
    public float 每阶段秒数 => 帧数 <= 0 ? Mathf.Max(0.01f, 成熟秒数) : 成熟秒数 / 帧数;

    public bool 已成熟(float 已生长秒) => 已生长秒 >= 成熟秒数;

    /// <summary>按已生长秒算当前该显示第几帧（0 起）。</summary>
    public int 取阶段(float 已生长秒)
    {
        if (帧数 <= 0) return 0;
        return Mathf.Clamp(Mathf.FloorToInt(已生长秒 / 每阶段秒数), 0, 帧数 - 1);
    }

    public Sprite 取帧(int 阶段)
    {
        if (帧数 <= 0) return null;
        return 成长帧[Mathf.Clamp(阶段, 0, 帧数 - 1)];
    }
}
