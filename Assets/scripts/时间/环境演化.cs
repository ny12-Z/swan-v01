using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 环境演化：挂在时钟上，按刻推进每一格的地图属性。
///
///   1) 每 衰减间隔 刻：所有格子掉一点水分 / 养分（策划表：4 刻掉 0.1）。
///   2) 每 扩散间隔 刻：半径内有水（纯水）的格子补水（策划表：4 刻 +0.2）。
///
/// 数值不写死，全部来自 环境规则.asset；改数值不用改代码。
/// 属性变了以后由 地图数据 重算类型、再通知 单元格显示 换瓦片，这里不需要操心表现。
/// </summary>
[DisallowMultipleComponent]
public class 环境演化 : MonoBehaviour
{
    [Header("引用")]
    [SerializeField, Tooltip("地图数据（拿格子表），留空会在运行时自动找")]
    private 地图数据 数据;

    [SerializeField, Tooltip("世界时钟，留空会在运行时自动找")]
    private 时间tick 时钟;

    [SerializeField, Tooltip("数值表")]
    private 环境规则 规则;

    [Header("开关")]
    [SerializeField] private bool 启用衰减 = true;
    [SerializeField] private bool 启用扩散 = true;

    // 每刻都要遍历，缓存一份列表避免迭代中改字典
    private readonly List<单元格状态> 缓存 = new List<单元格状态>();

    /// <summary>一共结算过多少次衰减 / 扩散（调试看状态用）。</summary>
    public int 衰减次数 { get; private set; }
    public int 扩散次数 { get; private set; }

    /// <summary>只读：数值表（调试面板要显示当前的间隔与增减值）。</summary>
    public 环境规则 规则表 => 规则;

    private void Awake()
    {
        if (数据 == null) 数据 = 地图数据.当前 != null ? 地图数据.当前 : FindAnyObjectByType<地图数据>();
        if (时钟 == null) 时钟 = 时间tick.当前 != null ? 时间tick.当前 : FindAnyObjectByType<时间tick>();

        if (时钟 != null) 时钟.每刻 += 处理刻;
        else Debug.LogWarning("[环境] 场景里没有 时间tick，环境演化不会生效");
    }

    private void OnDestroy()
    {
        if (时钟 != null) 时钟.每刻 -= 处理刻;
    }

    private void 处理刻(int 刻)
    {
        if (数据 == null) return;

        if (规则 == null)
        {
            Debug.LogWarning("[环境] 没配 环境规则 资产，环境演化不生效");
            return;
        }

        bool 要衰减 = 启用衰减 && 刻 % 规则.衰减间隔 == 0;
        bool 要扩散 = 启用扩散 && 刻 % 规则.扩散间隔 == 0;
        if (!要衰减 && !要扩散) return;

        缓存.Clear();
        缓存.AddRange(数据.遍历全部());

        if (要衰减) 结算衰减();
        if (要扩散) 结算扩散();
    }

    // ---------- 结算 ----------

    private void 结算衰减()
    {
        float 水 = 规则.衰减水分;
        float 养 = 规则.衰减养分;
        if (Mathf.Approximately(水, 0f) && Mathf.Approximately(养, 0f)) return;

        for (int i = 0; i < 缓存.Count; i++)
        {
            数据.加属性(缓存[i].坐标, 水, 养, 0f);
        }

        衰减次数++;
    }

    private void 结算扩散()
    {
        float 补水 = 规则.扩散水分;
        if (Mathf.Approximately(补水, 0f)) return;

        int 半径 = 规则.扩散半径;

        for (int i = 0; i < 缓存.Count; i++)
        {
            单元格状态 格 = 缓存[i];
            if (!附近有水(格.坐标, 半径)) continue;
            数据.加属性(格.坐标, 补水, 0f, 0f);
        }

        扩散次数++;
    }

    /// <summary>半径（切比雪夫，斜着也算）内有没有纯水格。</summary>
    private bool 附近有水(Vector2Int 坐标, int 半径)
    {
        for (int dx = -半径; dx <= 半径; dx++)
        {
            for (int dy = -半径; dy <= 半径; dy++)
            {
                if (dx == 0 && dy == 0) continue;

                Vector2Int 邻居 = new Vector2Int(坐标.x + dx, 坐标.y + dy);
                if (数据.取(邻居, out 单元格状态 状态) && 状态.类型 == 单元格类型.纯水) return true;
            }
        }

        return false;
    }
}
