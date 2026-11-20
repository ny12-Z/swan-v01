using System;
using UnityEngine;

/// <summary>
/// 单元格的属性种类。取值一律 1~100（上下限在「地图规则」资产里可改）。
/// 以后要加新属性（酸碱性、温度…）：在这里加一行，再改 <see cref="单元格属性.数量"/> 即可，
/// 规则表、调试显示、存档都会自动跟上。
/// </summary>
public enum 单元格属性
{
    水分 = 0,
    养分 = 1,
    植被 = 2,
}

/// <summary>属性种类的常量表，别在业务代码里写魔法数字。</summary>
public static class 单元格属性表
{
    /// <summary>属性总数。加属性时同步修改。</summary>
    public const int 数量 = 3;

    /// <summary>按枚举顺序列出所有属性，方便遍历（调试面板、存档用）。</summary>
    public static readonly 单元格属性[] 全部 =
    {
        单元格属性.水分,
        单元格属性.养分,
        单元格属性.植被,
    };

    public static string 显示名(单元格属性 属性)
    {
        switch (属性)
        {
            case 单元格属性.水分: return "水分";
            case 单元格属性.养分: return "养分";
            case 单元格属性.植被: return "植被";
            default: return 属性.ToString();
        }
    }
}

/// <summary>
/// 一个格子的运行时状态：属性数值 + 当前类型。
/// 用 class 而不是 struct：它会被字典大量引用，class 可以避免「复制一份、改了个副本」的坑。
/// </summary>
[Serializable]
public class 单元格状态
{
    [Tooltip("格坐标（Tilemap 的 cell 坐标）")]
    public Vector2Int 坐标;

    [Tooltip("当前类型，由属性按「地图规则」推导出来，不是手动填的")]
    public 单元格类型 类型;

    [Tooltip("这个格子的瓦片画在哪一层（地表层数组的下标），换类型时画回同一层")]
    public int 地表层 = 0;

    [Tooltip("各属性数值，下标 = 单元格属性")]
    public float[] 数值 = new float[单元格属性表.数量];

    public float 取(单元格属性 属性) => 数值[(int)属性];

    public void 设(单元格属性 属性, float 值, float 下限, float 上限)
    {
        数值[(int)属性] = Mathf.Clamp(值, 下限, 上限);
    }

    /// <summary>按枚举顺序复制一份数值（存档/调试用）。</summary>
    public float[] 复制数值()
    {
        float[] 副本 = new float[数值.Length];
        Array.Copy(数值, 副本, 数值.Length);
        return 副本;
    }

    public float 水分 => 取(单元格属性.水分);
    public float 养分 => 取(单元格属性.养分);
    public float 植被 => 取(单元格属性.植被);
}
