// 只读调试面板 —— 只在编辑器里编译。
// 整段包在 #if UNITY_EDITOR 里：打包时这个类根本不存在，不会带进玩家版本。
// 如果将来想在 Player 里也能看（例如开发版），把下面的 UNITY_EDITOR 换成
// UNITY_EDITOR || DEVELOPMENT_BUILD 即可。
#if UNITY_EDITOR

using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 只读调试面板：一眼看清「时间 / 环境 / 地图 / 作物」的实时状态，用来核实 tick 系统有没有真的在跑。
///
/// 设计原则：
///   1) 只读——不修改任何数据；删掉这个脚本对游戏没有任何影响。
///   2) 零接线——不用挂到场景里，进入 Play 会自动创建一个（见 自动创建）。
///      如果哪天想手动控制，把它挂到 时间系统 上即可，自动创建会跳过。
///   3) 中文用系统字体渲染（微软雅黑），不依赖项目里的任何字库资源。
///
/// 操作：F1 显示 / 隐藏。
/// </summary>
[DisallowMultipleComponent]
public class 调试面板 : MonoBehaviour
{
    [Header("操作")]
    [SerializeField, Tooltip("按这个键显示 / 隐藏面板")]
    private Key 开关按键 = Key.F1;

    [SerializeField] private bool 启动时显示 = true;

    [Header("外观")]
    [SerializeField, Range(8, 28)] private int 字号 = 14;
    [SerializeField] private Vector2 屏幕位置 = new Vector2(12f, 12f);

    [Header("引用（留空会在运行时自动找）")]
    [SerializeField] private 时间tick 时钟;
    [SerializeField] private 环境演化 环境;
    [SerializeField] private 地图数据 数据;
    [SerializeField] private 地图交互 交互;
    [SerializeField] private 作物系统 作物;

    private const float 采样间隔 = 0.5f;

    private bool 显示;
    private GUIStyle 样式;
    private readonly StringBuilder 文本 = new StringBuilder(512);

    private int 上次刻数;
    private float 上次采样时刻;
    private float 实测刻率;

    private void Awake()
    {
        显示 = 启动时显示;

        时钟 = 时钟 != null ? 时钟 : 时间tick.当前 ?? FindAnyObjectByType<时间tick>();
        数据 = 数据 != null ? 数据 : 地图数据.当前 ?? FindAnyObjectByType<地图数据>();
        环境 = 环境 != null ? 环境 : FindAnyObjectByType<环境演化>();
        交互 = 交互 != null ? 交互 : FindAnyObjectByType<地图交互>();
        作物 = 作物 != null ? 作物 : 作物系统.当前 ?? FindAnyObjectByType<作物系统>();

        上次采样时刻 = Time.unscaledTime;
    }

    private void Update()
    {
        Keyboard 键盘 = Keyboard.current;
        if (键盘 != null && 键盘[开关按键].wasPressedThisFrame)
        {
            显示 = !显示;
        }

        采样刻率();
    }

    /// <summary>用「两次采样之间过了多少刻 ÷ 多少真实秒」算出实际的 tick 速率。</summary>
    private void 采样刻率()
    {
        if (时钟 == null) return;

        float 现在 = Time.unscaledTime;
        float 间隔 = 现在 - 上次采样时刻;
        if (间隔 < 采样间隔) return;

        int 刻数 = 时钟.刻数;
        float 本次 = Mathf.Max(0, 刻数 - 上次刻数) / 间隔;
        实测刻率 = 实测刻率 <= 0f ? 本次 : Mathf.Lerp(实测刻率, 本次, 0.5f);

        上次刻数 = 刻数;
        上次采样时刻 = 现在;
    }

    private void OnGUI()
    {
        if (!显示) return;

        if (样式 == null)
        {
            建样式();
            上次刻数 = 时钟 != null ? 时钟.刻数 : 0;
        }

        拼文本();

        Vector2 尺寸 = 样式.CalcSize(new GUIContent(文本.ToString()));
        Rect 面板 = new Rect(屏幕位置.x, 屏幕位置.y, 尺寸.x + 20f, 尺寸.y + 16f);

        Color 旧色 = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.62f);
        GUI.DrawTexture(面板, Texture2D.whiteTexture);
        GUI.color = 旧色;

        GUI.Label(new Rect(面板.x + 10f, 面板.y + 6f, 面板.width - 20f, 面板.height - 12f), 文本.ToString(), 样式);
    }

    private void 建样式()
    {
        Font 字体 = null;
        try
        {
            字体 = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Microsoft YaHei UI", "SimHei", "Arial" }, Mathf.Max(1, 字号));
        }
        catch
        {
            字体 = null;
        }

        样式 = new GUIStyle(GUI.skin.label)
        {
            font = 字体 != null ? 字体 : GUI.skin.font,
            fontSize = 字号,
            alignment = TextAnchor.UpperLeft,
            richText = false,
            wordWrap = false,
        };
        样式.normal.textColor = new Color(0.93f, 0.96f, 0.90f);
    }

    private void 拼文本()
    {
        文本.Clear();

        行("── 只读调试面板 · F1 隐藏 ──");

        // 时钟
        if (时钟 != null)
        {
            float 每刻 = 时钟.每刻秒数;
            行($"时钟  刻数 {时钟.刻数}   实测 {实测刻率:0.00} 刻/秒   配置 {(每刻 > 0f ? 1f / 每刻 : 0f):0.0} 刻/秒   运行中 {(时钟.正在运行 ? "是" : "否")}");
        }
        else
        {
            行("时钟  未找到 时间tick");
        }

        // 环境
        if (环境 != null)
        {
            行($"环境  衰减 {环境.衰减次数} 次   扩散 {环境.扩散次数} 次");

            环境规则 规则 = 环境.规则表;
            if (规则 != null)
            {
                行($"      衰减：每 {规则.衰减间隔} 刻  水分 {规则.衰减水分:+0.##;-0.##}  养分 {规则.衰减养分:+0.##;-0.##}");
                行($"      扩散：每 {规则.扩散间隔} 刻  水分 {规则.扩散水分:+0.##;-0.##}  半径 {规则.扩散半径} 格");
            }
            else
            {
                行("      没有配 环境规则 资产");
            }
        }
        else
        {
            行("环境  未找到 环境演化");
        }

        // 地图与光标
        if (数据 != null)
        {
            string 玩家格 = 交互 != null ? $"({交互.玩家格.x}, {交互.玩家格.y})" : "—";
            行($"地图  {数据.格数} 格   玩家格 {玩家格}");
        }
        else
        {
            行("地图  未找到 地图数据");
        }

        Vector2Int 鼠标格 = Vector2Int.zero;
        bool 有鼠标格 = 交互 != null && 交互.取鼠标所在格(out 鼠标格);

        单元格状态 格状态;
        if (有鼠标格 && 数据 != null && 数据.取(鼠标格, out 格状态))
        {
            行($"光标  格 ({鼠标格.x}, {鼠标格.y})   类型 {格状态.类型}   地表层 {格状态.地表层}");
            行($"      水分 {格状态.水分:0.0}   养分 {格状态.养分:0.0}   植被 {格状态.植被:0.0}");

            if (交互 != null)
            {
                行($"      范围 {(交互.在范围(鼠标格) ? "内" : "外")}   可达 {(交互.可达(鼠标格) ? "是" : "否")}   评估 {地图交互.结果文字(交互.评估(鼠标格))}");
            }
        }
        else
        {
            行("光标  没有指向地图上的格子");
        }

        // 作物
        if (作物 != null)
        {
            作物定义 当前种子 = 作物.当前定义;
            行($"作物  {作物.数量} 株   当前种子 {(当前种子 != null ? 当前种子.显示名 : "—")}");

            作物状态 这株作物;
            if (有鼠标格 && 作物.取作物(鼠标格, out 这株作物))
            {
                作物定义 定义 = 这株作物.定义;
                string 名称 = 定义 != null ? 定义.显示名 : "未知";
                int 总阶段 = 定义 != null ? 定义.帧数 : 0;
                行($"      该格 {名称}   已长 {这株作物.已生长秒:0.0} 秒   阶段 {这株作物.阶段 + 1}/{总阶段}   进度 {这株作物.进度 * 100f:0}%   {(这株作物.已成熟 ? "已成熟" : "生长中")}");
            }
        }
        else
        {
            行("作物  未找到 作物系统");
        }
    }

    private void 行(string 内容)
    {
        文本.Append(内容).Append('\n');
    }

    // ---------- 运行时自动创建（不改场景） ----------

    /// <summary>
    /// 进入 Play 后自动生成一个面板，不需要往场景里挂任何东西。
    /// 场景里已经手动挂了 调试面板 的话就跳过，避免出现两个。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void 自动创建()
    {
        if (FindAnyObjectByType<调试面板>() != null) return;

        GameObject 物体 = new GameObject("调试面板（运行时自动创建）");
        DontDestroyOnLoad(物体);
        物体.AddComponent<调试面板>();
    }
}

#endif
