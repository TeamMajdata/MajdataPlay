using UnityEngine.Scripting;

namespace MajdataPlay.Settings
{
    [Preserve]
    // 仅供旧 settings.json 读取迁移，不是现行画质或 Unity Quality 索引。
    // 保留六个原名称和数值，兼容历史字符串 / 数字保存格式。
    internal enum RenderQualityOption
    {
        VeryLow = 0,
        Low = 1,
        Medium = 2,
        High = 3,
        VeryHigh = 4,
        Ultra = 5
    }
}
