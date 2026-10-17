using UnityEngine.Scripting;
using MajdataPlay.i18n;

namespace MajdataPlay.Settings.OptionEnumerators;

[Preserve]
public sealed class RenderingNumberSettingEnumerator : DefaultNumberEnumerator, IOptionEnumerator
{
    [Preserve]
    public RenderingNumberSettingEnumerator() { }

    protected override void InitInternal()
    {
        base.InitInternal();
        AppendPercent();
    }

    public override void Refresh()
    {
        base.Refresh();
        AppendPercent();
    }

    public override void RefreshLocalization()
    {
        base.RefreshLocalization();
        AppendPercent();
    }

    public override bool MoveNext() => Move(true);
    public override bool MovePrevious() => Move(false);

    bool Move(bool next)
    {
        if (UnsupportedSystemScaling) return false;
        var previous = Value;
        bool moved = next ? base.MoveNext() : base.MovePrevious();
        // base 的数字编辑会重建文本；触及边界时保持现有的百分比文本。
        if (!moved || Equals(previous, Value)) return false;
        AppendPercent();
        GameManager.ApplyRenderingSettings();
        return true;
    }

    // 不支持的平台保留设置与保存值，但不能用菜单输入修改或提交无效请求。
    bool UnsupportedSystemScaling
    {
        get
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return false;
#else
            return Name == nameof(RenderingOptions.SystemResolutionScaling);
#endif
        }
    }

    void AppendPercent()
    {
        if (UnsupportedSystemScaling)
            LocalizedValueTexts[0] = "MAJSETTING_PROPERTY_SystemScalingStatus_OPTION_Unsupported".i18n();
        else
            LocalizedValueTexts[0] += "%";
    }
}
