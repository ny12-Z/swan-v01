using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 显示层：监听 <see cref="地图数据.类型变化"/>，把格子换成对应类型的瓦片并染色。
/// 只负责「看起来是什么」，不含任何规则。
/// </summary>
[DisallowMultipleComponent]
public class 单元格显示 : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] private 地图数据 数据;

    [Tooltip("与「地图数据」里的地表层保持同一顺序；换瓦片时按 状态.地表层 的下标画回原来的层")]
    [SerializeField] private Tilemap[] 地表层;

    private void OnEnable()
    {
        if (数据 != null) 数据.类型变化 += 处理类型变化;
    }

    private void OnDisable()
    {
        if (数据 != null) 数据.类型变化 -= 处理类型变化;
    }

    /// <summary>
    /// 调试用：把全部格子按当前类型重画一遍。
    /// 注意这里**不**在 Start 里自动调用——地图上原有的瓦片是玩家在 Tile Palette 里一格一格画出来的，
    /// 全量重画会按散列换成同类型的其它瓦片（比如草地的另一帧），把整张地图刷花。
    /// 只有「类型真的变了」的格子才需要换瓦片（普通土地就是靠这个染成偏棕色）。
    /// </summary>
    [ContextMenu("重新刷全部格子")]
    private void 刷新全部()
    {
        if (数据 == null) return;
        foreach (单元格状态 状态 in 数据.遍历全部())
        {
            应用(状态);
        }
    }

    private void 处理类型变化(Vector2Int 坐标, 单元格类型 旧类型, 单元格类型 新类型)
    {
        if (数据 != null && 数据.取(坐标, out 单元格状态 状态))
        {
            应用(状态);
        }
    }

    private void 应用(单元格状态 状态)
    {
        单元格类型定义 定义 = 数据.取定义(状态.类型);
        if (定义 == null) return;

        Tilemap 层 = 取层(状态.地表层);
        if (层 == null) return;

        Vector3Int 位置 = new Vector3Int(状态.坐标.x, 状态.坐标.y, 0);
        层.SetTile(位置, 选瓦片(定义, 状态.坐标));
    }

    private Tilemap 取层(int 下标)
    {
        if (地表层 == null || 下标 < 0 || 下标 >= 地表层.Length) return null;
        return 地表层[下标];
    }

    /// <summary>同一种类型有多张瓦片时，按格坐标固定挑一张，保证同一格每次都一样。</summary>
    private static TileBase 选瓦片(单元格类型定义 定义, Vector2Int 坐标)
    {
        if (定义.瓦片 == null || 定义.瓦片.Length == 0) return null;
        if (定义.瓦片.Length == 1) return 定义.瓦片[0];

        uint 混合 = (uint)(坐标.x * 73856093) ^ (uint)(坐标.y * 19349663);
        int 序号 = (int)(混合 % (uint)定义.瓦片.Length);
        return 定义.瓦片[序号];
    }
}
