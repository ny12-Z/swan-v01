using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 可使用的道具。顺序就是滚轮切换的顺序，数值直接对应 Animator 里的「道具」参数。
/// </summary>
public enum 互动道具
{
    锄头 = 0,
    斧头 = 1,
    水壶 = 2,
    播种 = 3,   // 不碰地图属性：左键对着格子种 / 收；动作动画借用锄头那套
}

/// <summary>
/// 玩家互动：按 F 进入 / 退出互动状态，滚轮切道具，鼠标左键开始使用道具。
///
/// 交互节奏：
///   - 按 F 只进入「拿着工具」的状态，**不影响行走**，画面也不切换；走动时朝向照旧由 WASD 决定。
///   - 只有在互动状态下按左键，才切到动作画面、锁住移动、播放一次使用动画；
///     **朝哪边播放由鼠标决定**（只在这一刻看鼠标，不会影响走路时转身）。
///   - 动画播完（1.4 秒，和 互动动画搭建.cs 里的 动作总时长 对应）自动切回走路小人、恢复移动。
///
/// 层级约定（Scene 里）：
///   玩家
///   ├── SpriteRenderer …（走路小人）
///   └── 互动              ← 本脚本，负责按键与状态
///       └── 互动角色       ← SpriteRenderer + Animator（「互动动画.controller」）
///
/// 动作动画由 工具 / 搭建互动动画 生成，脚本只负责推 道具 / 方向 / 播放 三个参数。
/// 控制器的换向（AnyState → 各方向「准备」）带着「播放 == false」的条件，
/// 所以**必须先换方向、再拉播放**，顺序反了就会一直播上一次的方向（见 开始动作）。
/// </summary>
[DisallowMultipleComponent]
public class 玩家互动 : MonoBehaviour
{
    [Header("引用")]
    [SerializeField, Tooltip("播放动作动画的 Animator（子物体「互动角色」上的那个）")]
    private Animator 动作动画器;

    [SerializeField, Tooltip("播放动作动画的 SpriteRenderer（播放时才显示）")]
    private SpriteRenderer 动作渲染;

    [SerializeField, Tooltip("玩家本体的 SpriteRenderer（播放动作时隐藏，免得两个小人叠在一起）")]
    private SpriteRenderer 角色渲染;

    [SerializeField, Tooltip("玩家移动组件，播放动作时临时锁住移动；留空会从父物体上找")]
    private 玩家移动 移动;

    [SerializeField, Tooltip("地图交互组件。配了它就变成「对鼠标指着的格子互动」；留空则退回老行为（原地播动作）")]
    private 地图交互 地图;

    [SerializeField, Tooltip("作物系统。选中「播种」时，左键改成对鼠标指的格子播种 / 收获；留空会运行时自动找")]
    private 作物系统 作物;

    [Header("操作")]
    [SerializeField, Tooltip("进入 / 退出互动的按键")]
    private Key 开关按键 = Key.F;

    [SerializeField, Tooltip("滚轮触发阈值，太小会一格滚出好几下")]
    private float 滚轮阈值 = 0.1f;

    [SerializeField, Tooltip("一次使用动作的时长（秒）。要和 互动动画搭建.cs 里的 动作总时长 保持一致")]
    private float 动作时长 = 1.4f;

    [Header("状态（运行时只读）")]
    [SerializeField] private bool 互动中;
    [SerializeField] private 互动道具 当前道具 = 互动道具.锄头;
    [SerializeField] private bool 播放中;

    // Animator 参数用哈希，别每帧传字符串
    private static readonly int 道具参数 = Animator.StringToHash("道具");
    private static readonly int 方向参数 = Animator.StringToHash("方向");
    private static readonly int 播放参数 = Animator.StringToHash("播放");

    private float 动作结束时刻;

    // 这一次动作往哪个方向播（0 下 / 1 上 / 2 左 / 3 右）。按下左键时按鼠标算出来，跟走路朝向无关。
    private int 动作方向;

    // 上一次推给 Animator 的参数值，避免每帧重复 Set，也方便判断「方向真的变了没有」
    private int 上次道具 = -1;
    private int 上次方向 = -1;
    private bool 上次播放;

    /// <summary>是否正在互动（拿着工具、可以正常走路）。</summary>
    public bool 正在互动 => 互动中;

    /// <summary>是否正在播动作（此时移动被锁住）。</summary>
    public bool 正在播放 => 播放中;

    /// <summary>当前拿在手上的道具。</summary>
    public 互动道具 当前手持道具 => 当前道具;

    // 在 Inspector 里 Reset 时自动把子物体上的组件填好
    private void Reset()
    {
        动作动画器 = GetComponentInChildren<Animator>(true);
        动作渲染 = 动作动画器 != null ? 动作动画器.GetComponent<SpriteRenderer>() : null;
        移动 = GetComponentInParent<玩家移动>();
        角色渲染 = GetComponentInParent<SpriteRenderer>();
    }

    private void Awake()
    {
        if (动作动画器 == null) 动作动画器 = GetComponentInChildren<Animator>(true);
        if (动作渲染 == null && 动作动画器 != null) 动作渲染 = 动作动画器.GetComponent<SpriteRenderer>();
        if (移动 == null) 移动 = GetComponentInParent<玩家移动>();
        if (角色渲染 == null) 角色渲染 = transform.parent != null ? transform.parent.GetComponent<SpriteRenderer>() : null;
        if (作物 == null) 作物 = 作物系统.当前 != null ? 作物系统.当前 : FindAnyObjectByType<作物系统>();

        // 开局：走路小人正常显示，动作渲染藏着
        if (动作渲染 != null) 动作渲染.enabled = false;
        互动中 = false;
        播放中 = false;
    }

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null && kb[开关按键].wasPressedThisFrame)
        {
            切换互动();
        }

        if (!互动中) return;

        // 动作播放中：等它自己播完，期间不接受滚轮 / 点击
        if (播放中)
        {
            if (Time.time >= 动作结束时刻) 结束动作();
            return;
        }

        // 没在播动作时，让控制器的「方向」随时跟着鼠标。
        // 这样按下左键的那一帧，状态机早就在鼠标对应的「准备」状态上，
        // 不会因为「换方向」和「拉播放」挤在同一帧而被 AnyState 的「播放 == false」条件挡住。
        跟踪鼠标方向();

        // 选着「播种」时，数字键 1 / 2 切品种（小麦 / 苹果）
        if (当前道具 == 互动道具.播种) 处理播种按键(kb);

        Mouse 鼠标 = Mouse.current;
        if (鼠标 == null) return;

        // 滚轮：正数往上滚换下一个，负数往下滚换上一个
        float 滚动 = 鼠标.scroll.ReadValue().y;
        if (Mathf.Abs(滚动) >= 滚轮阈值)
        {
            切换道具(滚动 > 0f ? 1 : -1);
        }

        // 左键：对鼠标指着的格子用一次道具（超出范围 / 够不着 / 没有格子就不播）
        if (鼠标.leftButton.wasPressedThisFrame)
        {
            尝试使用道具();
        }
    }

    // ---------- 对外接口 ----------

    /// <summary>进入 / 退出互动。</summary>
    public void 切换互动()
    {
        if (互动中) 退出互动(); else 进入互动();
    }

    /// <summary>进入互动状态：不锁移动、不换画面，只是开始响应滚轮和左键。</summary>
    public void 进入互动()
    {
        互动中 = true;
        播放中 = false;

        if (动作渲染 != null) 动作渲染.enabled = false;
        if (角色渲染 != null) 角色渲染.enabled = true;

        if (移动 != null)
        {
            移动.可移动 = true;
            // 互动时依旧按 WASD 转向；鼠标只在按左键那一刻起作用
            移动.鼠标控制朝向 = false;
            动作方向 = 移动.当前朝向;
        }

        同步参数();
    }

    /// <summary>退出互动状态；正在播放的话先干净地收尾。</summary>
    public void 退出互动()
    {
        if (播放中) 结束动作();

        互动中 = false;
        播放中 = false;

        if (动作动画器 != null) 动作动画器.SetBool(播放参数, false);
        上次播放 = false;
        if (动作渲染 != null) 动作渲染.enabled = false;
        if (角色渲染 != null) 角色渲染.enabled = true;

        if (移动 != null)
        {
            移动.可移动 = true;
            移动.鼠标控制朝向 = false;
        }
    }

    /// <summary>滚轮换道具，步长 ±1。播放中调用会被忽略。</summary>
    public void 切换道具(int 步长)
    {
        if (播放中) return;

        int 总数 = System.Enum.GetValues(typeof(互动道具)).Length;
        int 下一个 = ((int)当前道具 + 步长 + 总数) % 总数;
        当前道具 = (互动道具)下一个;
        同步参数();
    }

    /// <summary>
    /// 左键：先问地图系统「鼠标指的这一格能不能互动」，能才播动作。
    /// 没配 地图 字段时退回老行为（原地播一次）。
    /// </summary>
    private void 尝试使用道具()
    {
        if (播放中) return;

        // 播种：不碰地图属性，对着鼠标指的格子播种 / 收获，然后播一次挥锄动画当作动作
        if (当前道具 == 互动道具.播种)
        {
            if (作物 != null && 地图 != null && 地图.取鼠标所在格(out Vector2Int 播种格))
            {
                if (作物.能否收获(播种格))
                {
                    作物.收获(播种格);
                    开始动作();
                }
                else if (作物.能否播种(播种格, out string 原因))
                {
                    作物.播种(播种格);
                    开始动作();
                }
                else
                {
                    Debug.Log($"[互动] 这一格不能种：{原因}");
                }

                return;
            }

            开始动作();
            return;
        }

        if (地图 != null)
        {
            if (!地图.取鼠标所在格(out Vector2Int 坐标)) return;
            if (!地图.对格子使用(坐标, 当前道具)) return;
        }

        开始动作();
    }

    /// <summary>播放一次使用动作：切到动作画面、锁住移动、播完自动结束。</summary>
    public void 开始动作()
    {
        if (!互动中 || 播放中) return;

        // ① 这一次动作朝哪边：只看鼠标，不改变角色本身的走路朝向
        动作方向 = 移动 != null ? 移动.取鼠标朝向() : 0;

        // ② 先把「道具 + 方向」推给控制器，并立刻评估一次：
        //    换向靠的是 AnyState → 各方向「准备」的过渡，而那条过渡带着「播放 == false」的条件，
        //    所以必须在 播放 还是 false 的时候把方向换好，否则状态机会留在上一次的方向上，
        //    接着「准备 → 使用」就会播上一次的动画（这就是之前那个 bug）。
        if (动作动画器 != null)
        {
            推送参数(播放: false);
            动作动画器.Update(0f);
        }

        // ③ 再打开播放，状态机就会从刚选好的「准备_当前方向」走到「使用_当前方向」
        播放中 = true;
        动作结束时刻 = Time.time + 动作时长;
        上次播放 = false;   // 让下一次推送一定把 播放 = true 发出去

        if (移动 != null) 移动.可移动 = false;      // 动作期间站住不动
        if (角色渲染 != null) 角色渲染.enabled = false;
        if (动作渲染 != null) 动作渲染.enabled = true;

        推送参数(播放: true);
    }

    /// <summary>收尾：切回走路小人、恢复移动、动画器回到准备状态。</summary>
    private void 结束动作()
    {
        播放中 = false;

        推送参数(播放: false);
        if (动作渲染 != null) 动作渲染.enabled = false;
        if (角色渲染 != null) 角色渲染.enabled = true;
        if (移动 != null) 移动.可移动 = true;
    }

    // ---------- 内部 ----------

    /// <summary>互动中、且没在播动作时，让控制器的方向参数一直跟着鼠标（看不见，只为了状态机不落后）。</summary>
    private void 跟踪鼠标方向()
    {
        if (移动 == null || 动作动画器 == null) return;

        动作方向 = 移动.取鼠标朝向();
        推送参数(播放: false);
    }

    /// <summary>选中播种时，数字键 1 / 2 切品种（小麦 / 苹果）。</summary>
    private void 处理播种按键(Keyboard kb)
    {
        if (作物 == null || kb == null) return;

        if (kb.digit1Key.wasPressedThisFrame) 作物.选择品种(0);
        else if (kb.digit2Key.wasPressedThisFrame) 作物.选择品种(1);
    }

    /// <summary>
    /// 动作动画只有 锄头 / 斧头 / 水壶 三套（互动动画.controller 的「道具」参数只认 0~2），
    /// 播种借用锄头那套（翻地的动作），这样不用改动画器。
    /// </summary>
    private static int 动画道具(互动道具 道具)
    {
        return 道具 == 互动道具.播种 ? (int)互动道具.锄头 : (int)道具;
    }

    /// <summary>把当前的 道具 / 方向 / 播放 推给 Animator（值没变就不重复推）。</summary>
    private void 同步参数()
    {
        推送参数(播放中);
    }

    /// <summary>
    /// 推送参数。<paramref name="播放"/> 单独作为参数传进来，
    /// 因为 开始动作 需要在「播放 = false」的情况下先把方向换好，再手动发一次 true。
    /// </summary>
    private void 推送参数(bool 播放)
    {
        if (动作动画器 == null) return;

        int 道具 = 动画道具(当前道具);
        if (道具 != 上次道具)
        {
            动作动画器.SetInteger(道具参数, 道具);
            上次道具 = 道具;
        }

        if (动作方向 != 上次方向)
        {
            动作动画器.SetInteger(方向参数, 动作方向);
            上次方向 = 动作方向;
        }

        if (播放 != 上次播放)
        {
            动作动画器.SetBool(播放参数, 播放);
            上次播放 = 播放;
        }
    }
}
