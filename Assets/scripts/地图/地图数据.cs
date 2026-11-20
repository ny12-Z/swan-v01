using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 地图的数据层：从场景里的 Tilemap 读出「哪里有格子、初始是什么类型」，
/// 之后所有变化都只存在这里的字典里（不写回 Tile 资产，Tile 是全图共享的）。
/// 场景里只有一个，挂在「地图系统」上。
/// </summary>
[DisallowMultipleComponent]
public class 地图数据 : MonoBehaviour
{
    /// <summary>便捷访问，场景里只会有一个。</summary>
    public static 地图数据 当前 { get; private set; }

    [Header("引用")]
    [Tooltip("地表瓦片层，按从下到上的顺序填。后填的层如果这一格有瓦片，就以它为准（例如上层画的 Tilled_Dirt）")]
    [SerializeField] private Tilemap[] 地表层;

    [Tooltip("数值表：阈值、增量、范围都在这里")]
    [SerializeField] private 地图规则 规则;

    [Tooltip("四种类型的规格书（Assets/数据/地图/单元格类型/ 下）")]
    [SerializeField] private 单元格类型定义[] 类型表;

    /// <summary>格子变化通知：坐标、旧类型、新类型。显示层订阅它。</summary>
    public event Action<Vector2Int, 单元格类型, 单元格类型> 类型变化;

    private readonly Dictionary<Vector2Int, 单元格状态> 格子 = new Dictionary<Vector2Int, 单元格状态>();
    private readonly Dictionary<TileBase, 单元格类型定义> 瓦片对照 = new Dictionary<TileBase, 单元格类型定义>();

    public 地图规则 规则表 => 规则;
    public Tilemap 参考层 => 地表层 != null && 地表层.Length > 0 ? 地表层[0] : null;
    public int 格数 => 格子.Count;

    private void Awake()
    {
        当前 = this;
        建图();
    }

    private void OnDestroy()
    {
        if (当前 == this) 当前 = null;
    }

    // ───────────────────────────── 建图 ─────────────────────────────

    /// <summary>从 Tilemap 扫描一遍，建立「坐标 → 初始状态」的字典。</summary>
    private void 建图()
    {
        格子.Clear();
        瓦片对照.Clear();

        if (地表层 == null || 地表层.Length == 0)
        {
            Debug.LogWarning("[地图] 地图数据 没有配置地表层，地图是空的。", this);
            return;
        }

        if (类型表 != null)
        {
            foreach (单元格类型定义 定义 in 类型表)
            {
                if (定义 == null || 定义.瓦片 == null || !定义.参与初始识别) continue;
                foreach (TileBase 瓦片 in 定义.瓦片)
                {
                    if (瓦片 == null) continue;
                    瓦片对照[瓦片] = 定义;
                }
            }
        }

        // 从下往上遍历：上层的瓦片会覆盖下层的判断结果
        // （例如 ground-above 上单独画了 Tilled_Dirt 的格，就该算旱地而不是下层的草原）
        for (int 层 = 0; 层 < 地表层.Length; 层++)
        {
            Tilemap 图 = 地表层[层];
            if (图 == null) continue;

            foreach (Vector3Int 位置 in 图.cellBounds.allPositionsWithin)
            {
                TileBase 瓦片 = 图.GetTile(位置);
                if (瓦片 == null) continue;
                if (!瓦片对照.TryGetValue(瓦片, out 单元格类型定义 定义)) continue;

                Vector2Int 坐标 = new Vector2Int(位置.x, 位置.y);
                单元格状态 状态 = new 单元格状态
                {
                    坐标 = 坐标,
                    类型 = 定义.类型,
                    地表层 = 层,
                    数值 = new float[单元格属性表.数量],
                };
                状态.设(单元格属性.水分, 定义.初始水分, 规则 != null ? 规则.属性下限 : 1f, 规则 != null ? 规则.属性上限 : 100f);
                状态.设(单元格属性.养分, 定义.初始养分, 规则 != null ? 规则.属性下限 : 1f, 规则 != null ? 规则.属性上限 : 100f);
                状态.设(单元格属性.植被, 定义.初始植被, 规则 != null ? 规则.属性下限 : 1f, 规则 != null ? 规则.属性上限 : 100f);
                格子[坐标] = 状态;
            }
        }

        Debug.Log($"[地图] 建图完成：{格子.Count} 个格子。", this);
    }

    // ───────────────────────────── 查询 ─────────────────────────────

    public bool 取(Vector2Int 坐标, out 单元格状态 状态) => 格子.TryGetValue(坐标, out 状态);

    public bool 有格子(Vector2Int 坐标) => 格子.ContainsKey(坐标);

    /// <summary>遍历所有格子（显示层初始化用）。</summary>
    public IEnumerable<单元格状态> 遍历全部() => 格子.Values;

    /// <summary>取某种类型的规格书，供显示层查瓦片和颜色。</summary>
    public 单元格类型定义 取定义(单元格类型 类型)
    {
        if (类型表 == null) return null;
        foreach (单元格类型定义 定义 in 类型表)
        {
            if (定义 != null && 定义.类型 == 类型) return 定义;
        }
        return null;
    }

    /// <summary>世界坐标 → 格坐标。</summary>
    public Vector2Int 取格坐标(Vector2 世界坐标)
    {
        Tilemap 层 = 参考层;
        if (层 == null) return new Vector2Int(Mathf.FloorToInt(世界坐标.x), Mathf.FloorToInt(世界坐标.y));
        Vector3Int 位置 = 层.WorldToCell(new Vector3(世界坐标.x, 世界坐标.y, 0f));
        return new Vector2Int(位置.x, 位置.y);
    }

    /// <summary>格坐标 → 该格中心的世界坐标（画光标用）。</summary>
    public Vector3 取格中心(Vector2Int 坐标)
    {
        Tilemap 层 = 参考层;
        if (层 == null) return new Vector3(坐标.x + 0.5f, 坐标.y + 0.5f, 0f);
        return 层.GetCellCenterWorld(new Vector3Int(坐标.x, 坐标.y, 0));
    }

    /// <summary>取某层上有没有瓦片（可达性判断用）。</summary>
    public bool 有瓦片(Tilemap 层, Vector2Int 坐标)
    {
        return 层 != null && 层.GetTile(new Vector3Int(坐标.x, 坐标.y, 0)) != null;
    }

    // ───────────────────────────── 修改 ─────────────────────────────

    /// <summary>
    /// 给格子加属性。内部完成：夹紧 → 重算类型 → 类型变了就发事件。
    /// 这是唯一修改属性的入口，别在外部直接改 单元格状态。
    /// </summary>
    public void 加属性(Vector2Int 坐标, float 水分增量, float 养分增量, float 植被增量)
    {
        if (!格子.TryGetValue(坐标, out 单元格状态 状态)) return;
        if (规则 == null) return;

        状态.设(单元格属性.水分, 状态.水分 + 水分增量, 规则.属性下限, 规则.属性上限);
        状态.设(单元格属性.养分, 状态.养分 + 养分增量, 规则.属性下限, 规则.属性上限);
        状态.设(单元格属性.植被, 状态.植被 + 植被增量, 规则.属性下限, 规则.属性上限);

        重算类型(状态);
    }

    private void 重算类型(单元格状态 状态)
    {
        if (规则 == null) return;

        单元格类型 新类型 = 规则.判定类型(状态.水分, 状态.养分, 状态.植被);
        if (新类型 == 状态.类型) return;

        单元格类型 旧类型 = 状态.类型;
        状态.类型 = 新类型;
        类型变化?.Invoke(状态.坐标, 旧类型, 新类型);
    }
}
