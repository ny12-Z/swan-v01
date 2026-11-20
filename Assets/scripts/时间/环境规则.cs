using UnityEngine;

/// <summary>
/// 环境演化的数值表：每一格随时间怎么掉水分 / 养分，附近有水时怎么补水。
///
/// 默认值对应策划表：
///   - 每 4 刻掉 0.1 水分、0.1 养分（4 刻/秒，所以是 0.1/秒）。
///   - 半径 3 格内有水，每 4 刻补水 +0.2（净增 +0.1/秒，所以湖边不会干）。
///
/// 时间系统暂时不做，但数值已经放在资产里，以后加「天气 / 季节 / G01 基因」只改这里。
/// </summary>
[CreateAssetMenu(fileName = "环境规则", menuName = "种田/环境规则", order = 40)]
public class 环境规则 : ScriptableObject
{
    [Header("自身衰减（每 N 刻结算一次）")]
    [SerializeField, Tooltip("多少刻结算一次。4 = 每 4 刻（1 秒）一次")]
    private int 衰减间隔刻 = 4;

    [SerializeField, Tooltip("每次结算掉多少水分（负数才是掉）")]
    private float 每间隔水分变化 = -0.1f;

    [SerializeField, Tooltip("每次结算掉多少养分（负数才是掉）")]
    private float 每间隔养分变化 = -0.1f;

    [Header("水分扩散（周围有水就补水）")]
    [SerializeField, Tooltip("判定半径（格）。斜着也算，即切比雪夫距离")]
    private int 水影响半径 = 3;

    [SerializeField, Tooltip("多少刻结算一次扩散")]
    private int 水影响间隔刻 = 4;

    [SerializeField, Tooltip("半径内有水时，每次结算补多少水分")]
    private float 水影响水分 = 0.2f;

    // ---------- 只读出口（运行时用，避免外面直接改字段） ----------

    public int 衰减间隔 => Mathf.Max(1, 衰减间隔刻);
    public float 衰减水分 => 每间隔水分变化;
    public float 衰减养分 => 每间隔养分变化;

    public int 扩散半径 => Mathf.Max(0, 水影响半径);
    public int 扩散间隔 => Mathf.Max(1, 水影响间隔刻);
    public float 扩散水分 => 水影响水分;
}
