using System;
using System.Collections.Generic;
namespace RoleplayOverlay
{
  public sealed class LiveAudioEngine : IAudioEngine
  {
    private readonly Audio _audio;
    public TimeSpan? LastDuration => null;
    public LiveAudioEngine(Audio audio)
    {
      _audio = audio ?? throw new ArgumentNullException(nameof(audio));
    }
    public void PlayMp3(string path)
      => _audio.PlayMp3(path);
    public void Speak(string text, string? voice)
      => _audio.Speak(text, voice);
    public void StopAll()
      => _audio.StopAll();
    public void SetLevelSink(SpeakerKind who)
      => _audio.SetLevelSink(who);
    public void StartMicLevelMonitor(IEnumerable<string>? micPreferences)
      => _audio.StartMicLevelMonitor(micPreferences);
    public bool TryRestartMic(IEnumerable<string>? micPreferences)
      => _audio.TryRestartMic(micPreferences);
    public void Dispose() { }
  }
}