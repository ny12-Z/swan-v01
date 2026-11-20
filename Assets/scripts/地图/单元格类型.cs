using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 目前开放的四类单元格。以后按策划的 5×5 数值表扩展时，在这里加枚举值，
/// 再在 Assets/数据/地图/单元格类型/ 下新建对应资产、把阈值写进「地图规则」即可，不用改代码。
/// </summary>
public enum 单元格类型
{
    草原 = 0,
    旱地 = 1,
    普通土地 = 2,
    纯水 = 3,
}

/// <summary>
/// 一种单元格类型的「规格书」：初始属性、用哪些瓦片、染色、是否可通行。
/// 一份资产 = 一种类型，策划可以直接在 Inspector 里调，不需要改代码。
/// </summary>
[CreateAssetMenu(fileName = "单元格类型", menuName = "种田/单元格类型定义", order = 10)]
public class 单元格类型定义 : ScriptableObject
{
    [Header("身份")]
    public 单元格类型 类型 = 单元格类型.草原;

    [Tooltip("显示用名字，调试面板会直接显示它")]
    public string 显示名 = "草原";

    [Header("初始属性（1~100）")]
    public float 初始水分 = 50f;
    public float 初始养分 = 50f;
    public float 初始植被 = 100f;

    [Header("显示")]
    [Tooltip("这一类格子用哪些瓦片。多个时按格坐标固定挑一个，纯属视觉变化")]
    public TileBase[] 瓦片;

    [Tooltip("是否参与「从地图上认出初始类型」。像普通土地这种和旱地共用 Tilled_Dirt、只能靠玩家改出来的类型，要关掉，否则会把旱地的瓦片抢走")]
    public bool 参与初始识别 = true;

    [Header("通行")]
    [Tooltip("勾掉表示玩家走不上去（水）。可达性判断会用到")]
    public bool 可通行 = true;

    [Header("时间系统预留（现在不生效，只占位）")]
    [Tooltip("每 tick 的水分自然变化，负数=蒸发。等做时间系统时直接读这里")]
    public float 每刻水分变化;
    public float 每刻养分变化;
    public float 每刻植被变化;
}
