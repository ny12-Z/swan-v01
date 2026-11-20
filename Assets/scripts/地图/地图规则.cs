using UnityEngine;

/// <summary>
/// 全局数值表：属性范围、类型判定阈值、交互增量、交互范围。
/// 做成资产而不是散在代码里，策划调数值不用改脚本、不用重编译。
/// </summary>
[CreateAssetMenu(fileName = "地图规则", menuName = "种田/地图规则", order = 20)]
public class 地图规则 : ScriptableObject
{
    [Header("属性范围")]
    [Tooltip("所有属性都会被夹在这个区间里")]
    public float 属性下限 = 1f;
    public float 属性上限 = 100f;

    [Header("类型判定（从上到下依次匹配，第一条命中的就是结果）")]
    [Range(1f, 100f)] public float 纯水最低水分 = 85f;
    [Range(0f, 100f)] public float 纯水最高植被 = 20f;
    [Range(0f, 100f)] public float 草原最低植被 = 40f;
    [Range(0f, 100f)] public float 普通土地最低水分 = 40f;
    [Range(0f, 100f)] public float 普通土地最低养分 = 28f;

    [Header("普通土地（翻过的旱地）")]
    [Tooltip("旱地比较干，锄头翻不进水，所以另给一套门槛：养分够就能算普通土地（旱地初始 28/30，锄一次变 24/36 正好过线）")]
    [Range(0f, 100f)] public float 翻土最低水分 = 24f;
    [Range(0f, 100f)] public float 翻土最低养分 = 36f;

    [Header("交互范围")]
    [Tooltip("以玩家所在格为基准的方形边长（格）。4 = 从玩家格左/下各 1 格起覆盖 4×4；改成 5 就是标准 5×5 居中")]
    [Min(1)] public int 交互范围格数 = 4;

    [Tooltip("可达性 BFS 的最大搜索半径（格），只为限制计算量")]
    [Min(1)] public int 可达检测窗口 = 24;

    [Header("浇水（水壶）增量")]
    public float 浇水水分 = 20f;
    public float 浇水养分 = 0f;
    public float 浇水植被 = 0f;

    [Header("锄地（锄头）增量")]
    public float 锄地水分 = -4f;
    public float 锄地养分 = 6f;
    [Tooltip("负得足够大就等于清零")]
    public float 锄地植被 = -100f;

    [Header("砍伐（斧头）增量")]
    public float 斧头水分 = -3f;
    public float 斧头养分 = -3f;
    public float 斧头植被 = -100f;

    /// <summary>
    /// 由属性推导类型。注意：类型永远是算出来的，不单独维护，避免「属性一套、类型一套」两个真相。
    /// </summary>
    public 单元格类型 判定类型(float 水分, float 养分, float 植被)
    {
        if (水分 >= 纯水最低水分 && 植被 < 纯水最高植被) return 单元格类型.纯水;
        if (植被 >= 草原最低植被) return 单元格类型.草原;
        if (水分 >= 普通土地最低水分 && 养分 >= 普通土地最低养分) return 单元格类型.普通土地;
        // 旱地被锄头翻过：干但肥，也算种得了东西的普通土地
        if (水分 >= 翻土最低水分 && 养分 >= 翻土最低养分) return 单元格类型.普通土地;
        return 单元格类型.旱地;
    }

    /// <summary>把一种道具的交互增量取出来，方便统一处理。</summary>
    public void 取增量(互动道具 道具, out float 水分, out float 养分, out float 植被)
    {
        switch (道具)
        {
            case 互动道具.水壶:
                水分 = 浇水水分; 养分 = 浇水养分; 植被 = 浇水植被; return;
            case 互动道具.锄头:
                水分 = 锄地水分; 养分 = 锄地养分; 植被 = 锄地植被; return;
            case 互动道具.斧头:
                水分 = 斧头水分; 养分 = 斧头养分; 植被 = 斧头植被; return;
            default:
                水分 = 0f; 养分 = 0f; 植被 = 0f; return;
        }
    }
}
