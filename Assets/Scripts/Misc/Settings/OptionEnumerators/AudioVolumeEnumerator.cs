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
            nameof(SFXVolume.Answer) => "answer.wav",
            nameof(SFXVolume.Tap) => "tap_perfect.wav",
            nameof(SFXVolume.Ex) => "tap_ex.wav",
            nameof(SFXVolume.Break) => "break.wav",
            nameof(SFXVolume.Slide) => "slide.wav",
            nameof(SFXVolume.Touch) => "touch.wav",
            nameof(SFXVolume.Hanabi) => "touch_hanabi.wav",
            nameof(SFXVolume.Voice) => "MajdataPlay.wav",
            _ => null
        };

        if (previewSfxName is not null)
        {
            _audioManager.PlaySFX(previewSfxName);
        }
    }
}
