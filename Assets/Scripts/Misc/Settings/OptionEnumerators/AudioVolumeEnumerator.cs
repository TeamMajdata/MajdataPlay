using MajdataPlay.IO;

namespace MajdataPlay.Settings.OptionEnumerators;
public sealed class AudioVolumeEnumerator : DefaultNumberEnumerator, IOptionEnumerator
{
    decimal _lastValue = 0;
    AudioManager _audioManager;

    public override void Refresh()
    {
        base.Refresh();
        if(_lastValue != CurrentValue)
        {
            UpdateVolume();
            PlayPreviewSfx();
            _lastValue = CurrentValue;
        }        
    }

    protected override void InitInternal()
    {
        base.InitInternal();
        _lastValue = CurrentValue;
        _audioManager = MajInstances.AudioManager;
    }

    void UpdateVolume()
    {
        _audioManager.ReadVolumeFromSettings();
    }

    void PlayPreviewSfx()
    {
        var previewSfxName = Name switch
        {
            nameof(VolumeOptions.Answer) => "answer.wav",
            nameof(VolumeOptions.Tap) => "tap_perfect.wav",
            nameof(VolumeOptions.Ex) => "tap_ex.wav",
            nameof(VolumeOptions.Break) => "break.wav",
            nameof(VolumeOptions.Slide) => "slide.wav",
            nameof(VolumeOptions.Touch) => "touch.wav",
            nameof(VolumeOptions.Hanabi) => "touch_hanabi.wav",
            nameof(VolumeOptions.Voice) => "MajdataPlay.wav",
            _ => null
        };

        if (previewSfxName is not null)
        {
            _audioManager.PlaySFX(previewSfxName);
        }
    }
}
