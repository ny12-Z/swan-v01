using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 播种 / 生长 / 收获。
///
/// 职责：
///   - 记着哪一格种了什么、长了多久（Dictionary&lt;坐标, 作物状态&gt;）。
///   - 每个世界刻推进一次生长，帧变了就换 Sprite（成长帧从左到右 = Basic_Plants.png 的 4 帧）。
///   - 提供「这一格能不能播种 / 能不能收获」的判定给鼠标光标和 玩家互动 用。
///
/// 它不画格子、不改地图属性：种地要求格子是「普通土地」（浇水或锄地翻出来的），
/// 判定只读 地图数据；作物本身是独立的 SpriteRenderer，不写进 Tilemap。
///
/// 层级约定（Scene 里，由 工具 / 搭建作物系统 生成）：
///   作物系统                    ← 空物体，挂本脚本
///   └── 作物容器                 ← 运行时生成的作物 Sprite 都挂在这里，方便一键清空
/// </summary>
[DisallowMultipleComponent]
public class 作物系统 : MonoBehaviour
{
    [Header("引用")]
    [SerializeField, Tooltip("地图数据（读格子类型），留空会在运行时自动找")]
    private 地图数据 地图;

    [SerializeField, Tooltip("世界时钟（作物按刻生长），留空会在运行时自动找")]
    private 时间tick 时钟;

    [SerializeField, Tooltip("所有可种品种的规格书，顺序 = 数字键 1 / 2 的顺序")]
    private 作物定义[] 定义表 = new 作物定义[0];

    [SerializeField, Tooltip("作物 Sprite 的父物体，留空就用自己")]
    private Transform 容器;

    [SerializeField, Tooltip("作物 Sprite 的预制件（只有 SpriteRenderer 的空壳），留空则运行时临时建")]
    private GameObject 视觉预制件;

    [Header("外观")]
    [SerializeField, Tooltip("作物 Sprite 的排序值，要比瓦片大才会画在地面之上")]
    private int 排序值 = 30;

    [SerializeField, Tooltip("作物相对格子中心的偏移，默认居中")]
    private Vector3 位置偏移 = Vector3.zero;

    [Header("状态（运行时只读）")]
    [SerializeField] private 作物品种 当前品种 = 作物品种.小麦;

    [SerializeField] private int 作物数量;

    private readonly Dictionary<Vector2Int, 作物状态> 作物表 = new Dictionary<Vector2Int, 作物状态>();

    /// <summary>场景里唯一的作物系统。</summary>
    public static 作物系统 当前 { get; private set; }

    /// <summary>种下去之后触发（给以后的音效 / 提示用）。</summary>
    public event Action<Vector2Int, 作物定义> 播种完成;

    /// <summary>收获之后触发（给以后的背包用）。</summary>
    public event Action<Vector2Int, 作物定义> 收获完成;

    /// <summary>现在选中的品种规格书。</summary>
    public 作物定义 当前定义 => 取定义(当前品种);

    /// <summary>现在选中的品种。</summary>
    public 作物品种 当前选中品种 => 当前品种;

    /// <summary>地里有几株作物。</summary>
    public int 数量 => 作物表.Count;

    /// <summary>所有作物的只读遍历（给调试 / 以后的存档用）。</summary>
    public IEnumerable<作物状态> 全部作物 => 作物表.Values;

    private void Awake()
    {
        当前 = this;

        if (地图 == null) 地图 = 地图数据.当前 != null ? 地图数据.当前 : FindAnyObjectByType<地图数据>();
        if (时钟 == null) 时钟 = 时间tick.当前 != null ? 时间tick.当前 : FindAnyObjectByType<时间tick>();
        if (容器 == null) 容器 = transform;

        if (时钟 != null) 时钟.每刻 += 推进生长;
        else Debug.LogWarning("[作物] 场景里没有 时间tick，作物不会生长");
    }

    private void OnDestroy()
    {
        if (时钟 != null) 时钟.每刻 -= 推进生长;
        if (当前 == this) 当前 = null;
    }

    // ---------- 选择品种 ----------

    /// <summary>按索引选品种（数字键 1 → 0，2 → 1）。</summary>
    public void 选择品种(int 索引)
    {
        if (定义表 == null || 定义表.Length == 0) return;
        int 下一个 = ((索引 % 定义表.Length) + 定义表.Length) % 定义表.Length;
        当前品种 = 定义表[下一个] != null ? 定义表[下一个].品种 : 当前品种;
    }

    /// <summary>按枚举选品种。</summary>
    public void 选择品种(作物品种 品种)
    {
        当前品种 = 品种;
    }

    public 作物定义 取定义(作物品种 品种)
    {
        if (定义表 == null) return null;
        for (int i = 0; i < 定义表.Length; i++)
        {
            if (定义表[i] != null && 定义表[i].品种 == 品种) return 定义表[i];
        }

        return null;
    }

    // ---------- 查询 ----------

    public bool 有作物(Vector2Int 坐标) => 作物表.ContainsKey(坐标);

    public bool 取作物(Vector2Int 坐标, out 作物状态 状态) => 作物表.TryGetValue(坐标, out 状态);

    /// <summary>这一格能不能种（能种返回 true，原因写在 out 里给提示用）。</summary>
    public bool 能否播种(Vector2Int 坐标, out string 原因)
    {
        原因 = null;

        if (地图 == null) { 原因 = "没有地图数据"; return false; }
        if (!地图.有格子(坐标)) { 原因 = "这里没有地"; return false; }
        if (有作物(坐标)) { 原因 = "这一格已经有作物了"; return false; }
        if (!地图.取(坐标, out 单元格状态 格)) { 原因 = "读不到这一格"; return false; }

        if (格.类型 != 单元格类型.普通土地)
        {
            原因 = "只能种在普通土地上（浇水或锄头翻过的地）";
            return false;
        }

        return true;
    }

    /// <summary>这一格有没有已经成熟、可以收的作物。</summary>
    public bool 能否收获(Vector2Int 坐标)
    {
        return 作物表.TryGetValue(坐标, out 作物状态 状态) && 状态.已成熟;
    }

    /// <summary>播种模式下鼠标指着的格子能不能操作（给 单元格光标 上色用）。</summary>
    public 交互结果 评估(Vector2Int 坐标)
    {
        if (地图 == null || !地图.有格子(坐标)) return 交互结果.没有格子;
        if (能否收获(坐标)) return 交互结果.可交互;
        if (能否播种(坐标, out _)) return 交互结果.可交互;
        return 交互结果.无事发生;
    }

    // ---------- 操作 ----------

    /// <summary>把当前选中的品种种到这一格。</summary>
    public bool 播种(Vector2Int 坐标)
    {
        return 播种(坐标, 当前定义);
    }

    /// <summary>把指定品种种到这一格。</summary>
    public bool 播种(Vector2Int 坐标, 作物定义 定义)
    {
        if (定义 == null)
        {
            Debug.LogWarning("[作物] 没有可用的作物定义（定义表是空的？）");
            return false;
        }

        if (!能否播种(坐标, out string 原因))
        {
            Debug.Log($"[作物] 不能播种：{原因}");
            return false;
        }

        作物状态 状态 = new 作物状态
        {
            坐标 = 坐标,
            定义 = 定义,
            已生长秒 = 0f,
            阶段 = 0,
            渲染 = 创建视觉(坐标, 定义),
        };

        作物表[坐标] = 状态;
        作物数量 = 作物表.Count;

        播种完成?.Invoke(坐标, 定义);
        return true;
    }

    /// <summary>收获这一格的作物（没成熟 / 没有作物返回 false）。</summary>
    public bool 收获(Vector2Int 坐标)
    {
        if (!作物表.TryGetValue(坐标, out 作物状态 状态)) return false;
        if (!状态.已成熟) return false;

        if (状态.渲染 != null) Destroy(状态.渲染.gameObject);

        作物表.Remove(坐标);
        作物数量 = 作物表.Count;

        // 背包还没做，先把「收到了什么」打出来，以后换成 背包.加入(定义.收获物)
        Debug.Log($"[作物] 收获 {状态.定义.显示名} ×1（背包系统待做）");

        收获完成?.Invoke(坐标, 状态.定义);
        return true;
    }

    /// <summary>清空所有作物（调试用）。</summary>
    [ContextMenu("清空全部作物")]
    public void 清空全部()
    {
        foreach (作物状态 状态 in 作物表.Values)
        {
            if (状态.渲染 != null) Destroy(状态.渲染.gameObject);
        }

        作物表.Clear();
        作物数量 = 0;
    }

    // ---------- 生长 ----------

    private void 推进生长(int 刻)
    {
        if (作物表.Count == 0) return;

        float 步长 = 时钟 != null ? 时钟.每刻秒数 : Time.deltaTime;

        foreach (作物状态 状态 in 作物表.Values)
        {
            if (状态.定义 == null) continue;

            状态.已生长秒 += 步长;

            int 新阶段 = 状态.定义.取阶段(状态.已生长秒);
            if (新阶段 == 状态.阶段) continue;

            状态.阶段 = 新阶段;
            应用帧(状态);
        }

        作物数量 = 作物表.Count;
    }

    private void 应用帧(作物状态 状态)
    {
        if (状态.渲染 == null) return;
        状态.渲染.sprite = 状态.定义.取帧(状态.阶段);
    }

    private SpriteRenderer 创建视觉(Vector2Int 坐标, 作物定义 定义)
    {
        Vector3 位置 = 地图 != null ? 地图.取格中心(坐标) : new Vector3(坐标.x + 0.5f, 坐标.y + 0.5f, 0f);
        位置 += 位置偏移;

        GameObject 物体;
        if (视觉预制件 != null)
        {
            物体 = Instantiate(视觉预制件, 位置, Quaternion.identity, 容器);
        }
        else
        {
            物体 = new GameObject("作物", typeof(SpriteRenderer));
            物体.transform.SetParent(容器, false);
            物体.transform.position = 位置;
        }

        物体.name = $"作物_{定义.显示名}_{坐标.x}_{坐标.y}";

        SpriteRenderer 渲染 = 物体.GetComponent<SpriteRenderer>();
        if (渲染 == null) 渲染 = 物体.AddComponent<SpriteRenderer>();

        渲染.sprite = 定义.取帧(0);
        渲染.sortingOrder = 排序值;

        return 渲染;
    }
}
