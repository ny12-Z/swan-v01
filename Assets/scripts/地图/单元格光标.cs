using UnityEngine;

/// <summary>
/// 光标：按 F 进入互动后，在鼠标指着的格子上画一个方框。
/// 可以互动 → 黄色；超出范围 / 够不着 / 没有格子 → 红色。
/// 只管显示，不做判断。
/// </summary>
[DisallowMultipleComponent]
public class 单元格光标 : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] private 地图交互 交互;

    [Tooltip("用来判断现在是不是在互动状态；留空则不限制，一直显示")]
    [SerializeField] private 玩家互动 玩家;

    [Tooltip("作物系统。选中「播种」时改问它：这一格能不能播种 / 收获")]
    [SerializeField] private 作物系统 作物;

    [SerializeField] private SpriteRenderer 图形;

    [Header("颜色")]
    [SerializeField] private Color 可交互颜色 = new Color(1f, 0.92f, 0.25f, 1f);
    [SerializeField] private Color 不可交互颜色 = new Color(0.92f, 0.22f, 0.22f, 1f);

    [Tooltip("稍微抬高一点，避免和瓦片 z 打架")]
    [SerializeField] private float 高度偏移 = -0.1f;

    private void Awake()
    {
        if (图形 == null) 图形 = GetComponentInChildren<SpriteRenderer>();
        隐藏();
    }

    private void Update()
    {
        if (图形 == null || 交互 == null)
        {
            隐藏();
            return;
        }

        // 只在「拿工具站着」的时候显示：播放动作时收起，避免挡镜头
        if (玩家 != null && (!玩家.正在互动 || 玩家.正在播放))
        {
            隐藏();
            return;
        }

        if (!交互.取鼠标所在格(out Vector2Int 坐标) || !交互.数据表.有格子(坐标))
        {
            隐藏();
            return;
        }

        图形.enabled = true;
        Vector3 中心 = 交互.数据表.取格中心(坐标);
        中心.z += 高度偏移;
        transform.position = 中心;

        // 播种道具的规则在 作物系统 里（能不能种 / 能不能收），其余道具问地图系统
        交互结果 结果 = 玩家 != null && 玩家.当前手持道具 == 互动道具.播种 && 作物 != null
            ? 作物.评估(坐标)
            : 交互.评估(坐标);

        图形.color = 结果 == 交互结果.可交互 ? 可交互颜色 : 不可交互颜色;
    }

    private void 隐藏()
    {
        if (图形 != null) 图形.enabled = false;
    }
}
