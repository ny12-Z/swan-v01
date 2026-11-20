using System;
using UnityEngine;

/// <summary>
/// 世界时钟：把时间切成固定的「刻」（tick）。环境演化、作物生长都挂在这里推进。
///
/// 节奏由 每秒刻数 决定（默认 4 刻/秒 = 每刻 0.25 秒，和策划表的 4tick/s 对应）。
/// 每过一刻广播一次 每刻 事件，订阅者拿刻数自己决定是「每刻都做」还是「每 N 刻做一次」。
///
/// 层级约定（Scene 里，由 工具 / 搭建作物系统 生成）：
///   时间系统                ← 空物体，用来收纳整套时间相关组件
///   └──（组件）时间tick
///   └──（组件）环境演化      ← 按刻改每一格的水分 / 养分
/// </summary>
[DisallowMultipleComponent]
public class 时间tick : MonoBehaviour
{
    [Header("节奏")]
    [SerializeField, Tooltip("每秒推进多少刻。4 刻/秒 = 每刻 0.25 秒（策划表默认值）")]
    private float 每秒刻数 = 4f;

    [SerializeField, Tooltip("关掉后时钟暂停，所有挂在刻上的系统一起停")]
    private bool 运行中 = true;

    [Header("状态（运行时只读）")]
    [SerializeField] private int 已过刻数;

    /// <summary>场景里唯一的时钟，别的系统可以直接拿（没有则为 null）。</summary>
    public static 时间tick 当前 { get; private set; }

    /// <summary>每过一刻触发一次，参数是累计刻数（从 1 开始）。</summary>
    public event Action<int> 每刻;

    /// <summary>一刻等于多少秒。</summary>
    public float 每刻秒数 => 1f / Mathf.Max(0.01f, 每秒刻数);

    /// <summary>从开局到现在一共过了多少刻。</summary>
    public int 刻数 => 已过刻数;

    /// <summary>时钟是否在走。</summary>
    public bool 正在运行 => 运行中;

    private float 累加;

    private void Awake()
    {
        当前 = this;
    }

    private void OnDestroy()
    {
        if (当前 == this) 当前 = null;
    }

    private void Update()
    {
        if (!运行中) return;

        float 一步 = 每刻秒数;
        if (一步 <= 0f) return;

        累加 += Time.deltaTime;

        // 卡顿（或编辑器暂停后恢复）时最多补 64 刻，避免一帧里追几百刻把机器拖死
        int 保护 = 0;
        while (累加 >= 一步 && 保护 < 64)
        {
            累加 -= 一步;
            保护++;
            已过刻数++;
            每刻?.Invoke(已过刻数);
        }
    }

    // ---------- 对外接口 ----------

    /// <summary>暂停 / 继续时钟（所有挂在刻上的系统一起停）。</summary>
    public void 设置运行(bool 运行)
    {
        运行中 = 运行;
    }

    /// <summary>把时钟清零，方便重复测试。</summary>
    [ContextMenu("时钟清零")]
    public void 清零()
    {
        已过刻数 = 0;
        累加 = 0f;
    }
}
