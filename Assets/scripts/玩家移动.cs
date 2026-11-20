using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 玩家移动：按住 W A S D（或方向键）让 2D 刚体在俯视角地图上移动。
/// 所有位移都写在 FixedUpdate 里，与物理步进同步，所以必须配合 Rigidbody2D 使用。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public class 玩家移动 : MonoBehaviour
{
    [Header("移动参数")]
    [SerializeField, Tooltip("移动速度，单位：世界单位 / 秒")]
    private float 速度 = 5f;

    [Header("引用")]
    [SerializeField, Tooltip("角色的 2D 刚体，留空时会在 Awake 里自动从本物体上取")]
    private Rigidbody2D 刚体;

    [SerializeField, Tooltip("角色身上的 Animator，留空时会在 Awake 里自动从本物体上取")]
    private Animator 动画器;

    [Header("状态")]
    [SerializeField, Tooltip("关掉后角色不响应输入、原地不动（互动时由 玩家互动 临时关闭）")]
    private bool 允许移动 = true;

    [SerializeField, Tooltip("勾上后角色一直面向鼠标，不再按走动方向转身（可选功能，默认关闭；用道具时的朝向由 玩家互动 单独处理）")]
    private bool 朝向跟随鼠标;

    // 当前物理帧的输入方向，长度已归一化，斜着走不会更快
    private Vector2 输入方向;

    // 朝向：0 = 下，1 = 上，2 = 左，3 = 右（和贴图行顺序一致）
    private int 朝向;

    // Animator 参数用哈希，比每帧传字符串快
    private static readonly int 方向参数 = Animator.StringToHash("Dir");
    private static readonly int 速度参数 = Animator.StringToHash("Speed");

    /// <summary>读取或修改移动速度，方便其他脚本（减速陷阱、加速道具等）调用。</summary>
    public float Speed
    {
        get => 速度;
        set => 速度 = Mathf.Max(0f, value);
    }

    /// <summary>当前朝向：0 = 下，1 = 上，2 = 左，3 = 右。互动系统按它挑选同方向的动画。</summary>
    public int 当前朝向 => 朝向;

    /// <summary>是否允许移动。互动（用道具）期间由 玩家互动 临时关掉。</summary>
    public bool 可移动
    {
        get => 允许移动;
        set => 允许移动 = value;
    }

    /// <summary>
    /// 朝向是否由鼠标决定（可选功能，默认关闭）。关闭时朝向只跟 WASD / 方向键走；
    /// 想做成「在跑动中一直看着准星」的玩法时把它打开。
    /// 用道具动作时朝哪边播，由 <see cref="取鼠标朝向"/> 单独决定，与控制这个开关无关。
    /// </summary>
    public bool 鼠标控制朝向
    {
        get => 朝向跟随鼠标;
        set => 朝向跟随鼠标 = value;
    }

    /// <summary>立刻按当前鼠标位置刷新一次朝向（角色会真的转身面向鼠标）。</summary>
    public void 朝向指向鼠标()
    {
        if (取鼠标世界位置(out Vector2 世界位置))
        {
            记录朝向(世界位置.x - transform.position.x, 世界位置.y - transform.position.y);
        }
    }

    /// <summary>
    /// 只「看」鼠标：返回鼠标相对角色的方向（0 = 下，1 = 上，2 = 左，3 = 右），
    /// 但**不改变**角色本身的朝向。没有鼠标 / 主相机时返回当前朝向。
    /// 用道具时靠它决定这一次动作朝哪边播，走路朝向依旧只跟 WASD。
    /// </summary>
    public int 取鼠标朝向()
    {
        if (!取鼠标世界位置(out Vector2 世界位置)) return 朝向;

        return 换算朝向(世界位置.x - transform.position.x, 世界位置.y - transform.position.y, 朝向);
    }

    /// <summary>按当前输入方向刷新一次朝向（松手没输入则保持原朝向）。</summary>
    public void 刷新朝向()
    {
        更新朝向();
    }

    // 在 Inspector 里 Reset 组件、或刚把脚本拖到物体上时会自动填好刚体引用
    private void Reset()
    {
        刚体 = GetComponent<Rigidbody2D>();
    }

    private void Awake()
    {
        if (刚体 == null)
        {
            刚体 = GetComponent<Rigidbody2D>();
        }

        if (动画器 == null)
        {
            动画器 = GetComponent<Animator>();
        }

        // 俯视角 2D：不要重力，也不要被碰撞带着转圈
        刚体.gravityScale = 0f;
        刚体.constraints |= RigidbodyConstraints2D.FreezeRotation;
        // 抗锯齿式插值，画面更顺滑（不想要可以删掉这一行）
        刚体.interpolation = RigidbodyInterpolation2D.Interpolate;
    }

    // 原来放在 Update 里的移动全部改到 FixedUpdate：
    // 固定时间步长里改速度，物理和碰撞才稳。
    private void FixedUpdate()
    {
        读取输入();
        更新朝向();

        // 互动（锄地/砍树/浇水）时不许乱跑，速度清零，动画走待机
        if (!允许移动)
        {
            输入方向 = Vector2.zero;
            刚体.linearVelocity = Vector2.zero;
            更新动画();
            return;
        }

        刚体.linearVelocity = 输入方向 * 速度;
        更新动画();
    }

    // 用新版 Input System 直接读键盘状态，不依赖旧版 Input 管理器
    private void 读取输入()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null)
        {
            // 没有键盘（例如手机）时保持不动
            输入方向 = Vector2.zero;
            return;
        }

        float x = 0f;
        float y = 0f;

        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) x -= 1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x += 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed) y -= 1f;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed) y += 1f;

        输入方向 = new Vector2(x, y).normalized;
    }

    // 朝向：互动时跟鼠标，平时跟走动方向（松手后保持上一次的方向）
    private void 更新朝向()
    {
        if (朝向跟随鼠标 && 取鼠标世界位置(out Vector2 鼠标位置))
        {
            记录朝向(鼠标位置.x - transform.position.x, 鼠标位置.y - transform.position.y);
            return;
        }

        if (输入方向.sqrMagnitude > 0f)
        {
            记录朝向(输入方向.x, 输入方向.y);
        }
    }

    // 把鼠标屏幕坐标换算成「和玩家同一个平面」的世界坐标；没有鼠标或没有主相机时返回 false
    private bool 取鼠标世界位置(out Vector2 世界位置)
    {
        世界位置 = Vector2.zero;

        Mouse 鼠标 = Mouse.current;
        Camera 相机 = Camera.main;
        if (鼠标 == null || 相机 == null) return false;

        Vector3 屏幕位置 = 鼠标.position.ReadValue();
        // 正交相机看图不需要透视：把 z 设成「相机到玩家平面」的距离，换算结果才落在玩家的平面上
        屏幕位置.z = Mathf.Abs(相机.transform.position.z - transform.position.z);

        世界位置 = 相机.ScreenToWorldPoint(屏幕位置);
        return true;
    }

    // 只在真正有输入时才改朝向，松手后保持上一次的方向
    private void 记录朝向(float x, float y)
    {
        朝向 = 换算朝向(x, y, 朝向);
    }

    // 把「指向某个方向的向量」换算成 0 下 / 1 上 / 2 左 / 3 右；向量为零时返回原朝向
    private static int 换算朝向(float x, float y, int 原朝向)
    {
        if (x == 0f && y == 0f) return 原朝向;

        // 横向分量更大就走左右，否则走上下
        if (Mathf.Abs(x) > Mathf.Abs(y))
        {
            return x > 0f ? 3 : 2;
        }

        return y > 0f ? 1 : 0;
    }

    // 把朝向和速度喂给 Animator：Speed > 0.1 播走路，否则播待机
    private void 更新动画()
    {
        if (动画器 == null) return;

        // 速度越快帧播得也越快，免得出现"滑步"
        float 播放倍率 = Mathf.Max(0.1f, 速度 / 5f);
        if (!Mathf.Approximately(动画器.speed, 播放倍率))
        {
            动画器.speed = 播放倍率;
        }

        动画器.SetInteger(方向参数, 朝向);
        动画器.SetFloat(速度参数, 输入方向.sqrMagnitude > 0f ? 速度 : 0f);
    }
}
