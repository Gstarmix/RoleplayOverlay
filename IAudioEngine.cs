using System;
using System.Collections.Generic;
namespace RoleplayOverlay
{
  public interface IAudioEngine : IDisposable
  {
    void PlayMp3(string path);
    void Speak(string text, string? voice);
    void StopAll();
    TimeSpan? LastDuration { get; }
    void SetLevelSink(SpeakerKind who);
    void StartMicLevelMonitor(IEnumerable<string>? micPreferences);
    bool TryRestartMic(IEnumerable<string>? micPreferences);
  }
}