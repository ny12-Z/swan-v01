using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

/// <summary>一次交互能不能成立、为什么不能。</summary>
public enum 交互结果
{
    可交互 = 0,
    没有格子 = 1,
    超出范围 = 2,
    够不着 = 3,
    无事发生 = 4,
}

/// <summary>
/// 交互层：把「鼠标指着哪个格子」翻译成「能不能互动、互动后属性怎么变」。
/// 所有判断规则都来自 <see cref="地图规则"/> 资产，这里不写死数值。
/// </summary>
[DisallowMultipleComponent]
public class 地图交互 : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] private 地图数据 数据;

    [Tooltip("玩家移动组件，用来把屏幕坐标换成世界/格坐标")]
    [SerializeField] private 玩家移动 玩家;

    [Tooltip("角色 Transform，用来判断玩家在哪一格")]
    [SerializeField] private Transform 角色;

    [Tooltip("碰撞层（Collision），这层有瓦片的格子玩家到不了")]
    [SerializeField] private Tilemap 碰撞层;

    [Tooltip("渲染玩家画面的相机，留空会自动用 Camera.main")]
    [SerializeField] private Camera 相机;

    private static readonly Vector2Int[] 八方向 =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
        new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1),
    };

    private readonly HashSet<Vector2Int> 可达集合 = new HashSet<Vector2Int>();
    private Vector2Int 可达缓存起点 = new Vector2Int(int.MinValue, int.MinValue);

    public 地图数据 数据表 => 数据;
    public 地图规则 规则 => 数据 != null ? 数据.规则表 : null;

    // ───────────────────────────── 坐标 ─────────────────────────────

    /// <summary>鼠标当前指着的格坐标（先换算到玩家所在平面，再用 Tilemap 转格）。</summary>
    public bool 取鼠标所在格(out Vector2Int 坐标)
    {
        坐标 = default;
        if (数据 == null) return false;
        if (!取鼠标世界位置(out Vector2 世界)) return false;
        坐标 = 数据.取格坐标(世界);
        return true;
    }

    private bool 取鼠标世界位置(out Vector2 世界)
    {
        世界 = default;
        Camera 画面 = 相机 != null ? 相机 : Camera.main;
        if (画面 == null) return false;

        // 项目只启用了新 Input System，旧的 Input.mousePosition 会直接抛异常
        Mouse 鼠标 = Mouse.current;
        if (鼠标 == null) return false;
        Vector2 屏幕 = 鼠标.position.ReadValue();

        // 正交相机下，任意 z 都能换算到角色所在平面
        世界 = 画面.ScreenToWorldPoint(new Vector3(屏幕.x, 屏幕.y,
            Mathf.Abs(画面.transform.position.z - (角色 != null ? 角色.position.z : 0f))));
        return true;
    }

    /// <summary>玩家当前所在的格。</summary>
    public Vector2Int 玩家格
    {
        get
        {
            if (数据 == null) return default;
            Transform 目标 = 角色 != null ? 角色 : (玩家 != null ? 玩家.transform : null);
            if (目标 == null) return default;
            return 数据.取格坐标(目标.position);
        }
    }

    // ───────────────────────────── 判断 ─────────────────────────────

    public 交互结果 评估(Vector2Int 坐标)
    {
        if (数据 == null || !数据.有格子(坐标)) return 交互结果.没有格子;
        if (!在范围(坐标)) return 交互结果.超出范围;
        if (!可达(坐标)) return 交互结果.够不着;
        return 交互结果.可交互;
    }

    /// <summary>以玩家格为基准的方形范围；4 → 覆盖 玩家-1 .. 玩家+2 共 4×4。</summary>
    public bool 在范围(Vector2Int 坐标)
    {
        if (数据 == null || 规则 == null) return false;
        int 边长 = Mathf.Max(1, 规则.交互范围格数);
        int 起点偏移 = (边长 - 1) / 2;
        Vector2Int 左下 = 玩家格 - new Vector2Int(起点偏移, 起点偏移);
        return 坐标.x >= 左下.x && 坐标.x < 左下.x + 边长
            && 坐标.y >= 左下.y && 坐标.y < 左下.y + 边长;
    }

    /// <summary>能不能走过去：有格子、类型可通行、碰撞层没瓦片。</summary>
    private bool 可行走(Vector2Int 坐标)
    {
        if (数据 == null || !数据.取(坐标, out 单元格状态 状态)) return false;

        单元格类型定义 定义 = 数据.取定义(状态.类型);
        if (定义 != null && !定义.可通行) return false;

        return !数据.有瓦片(碰撞层, 坐标);
    }

    /// <summary>
    /// 可达性：从玩家格做 8 邻域 BFS，目标格能站人、或和能站人的格子相邻，就算够得着。
    /// 所以湖中心（周围全是水）够不着，湖岸（贴着陆地）够得着。
    /// </summary>
    public bool 可达(Vector2Int 目标)
    {
        Vector2Int 起点 = 玩家格;
        if (起点 != 可达缓存起点)
        {
            可达缓存起点 = 起点;
            重建可达(起点);
        }

        if (可达集合.Contains(目标)) return true;
        foreach (Vector2Int 偏移 in 八方向)
        {
            if (可达集合.Contains(目标 + 偏移)) return true;
        }
        return false;
    }

    private void 重建可达(Vector2Int 起点)
    {
        可达集合.Clear();
        if (数据 == null || 规则 == null) return;

        int 窗口 = Mathf.Max(1, 规则.可达检测窗口);
        Queue<Vector2Int> 队列 = new Queue<Vector2Int>();
        可达集合.Add(起点);
        队列.Enqueue(起点);

        while (队列.Count > 0)
        {
            Vector2Int 当前 = 队列.Dequeue();
            foreach (Vector2Int 偏移 in 八方向)
            {
                Vector2Int 下一个 = 当前 + 偏移;
                if (Mathf.Abs(下一个.x - 起点.x) > 窗口 || Mathf.Abs(下一个.y - 起点.y) > 窗口) continue;
                if (可达集合.Contains(下一个)) continue;
                if (!可行走(下一个)) continue;
                可达集合.Add(下一个);
                队列.Enqueue(下一个);
            }
        }
    }

    // ───────────────────────────── 执行 ─────────────────────────────

    /// <summary>对格子用一次道具。返回 false 表示这次交互没成立（不播动画、不弹提示）。</summary>
    public bool 对格子使用(Vector2Int 坐标, 互动道具 道具)
    {
        if (数据 == null || 规则 == null) return false;
        if (评估(坐标) != 交互结果.可交互) return false;
        if (!数据.取(坐标, out 单元格状态 状态)) return false;

        // 纯水格：动作照播（可以往水里浇水），但属性不再变化
        if (状态.类型 == 单元格类型.纯水) return true;

        规则.取增量(道具, out float 水分, out float 养分, out float 植被);
        数据.加属性(坐标, 水分, 养分, 植被);
        return true;
    }

    /// <summary>给调试/提示用：把判断结果翻成一句人话。</summary>
    public static string 结果文字(交互结果 结果)
    {
        switch (结果)
        {
            case 交互结果.可交互: return "可以互动";
            case 交互结果.没有格子: return "这里没有土地";
            case 交互结果.超出范围: return "太远了";
            case 交互结果.够不着: return "过不去";
            case 交互结果.无事发生: return "没什么效果";
            default: return 结果.ToString();
        }
    }
}
