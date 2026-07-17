using System;
using System.Collections.Generic;
using System.IO;
using System.Speech.Synthesis;
using System.Threading.Tasks;
using NAudio.Wave;
namespace RoleplayOverlay
{
  public sealed class OfflineAudioEngine : IAudioEngine
  {
    private readonly string         _tempDir;
    private readonly AzureTtsEngine? _azure;
    private readonly SpeechSynthesizer _speech = new();
    public TimeSpan? LastDuration   { get; private set; }
    public string?   LastAudioPath  { get; private set; }
    public List<AudioSegment> Segments { get; } = new();
    public OfflineAudioEngine(string? tempDir = null)
      : this(tempDir, false, null, null) { }
    public OfflineAudioEngine(string? tempDir, bool useAzure, string? azureKey, string? azureRegion)
    {
      _tempDir = tempDir ?? Path.Combine(Path.GetTempPath(), "RoleplayOverlay_render");
      Directory.CreateDirectory(_tempDir);
      _speech.Rate   = 0;
      _speech.Volume = 100;
      if (useAzure && !string.IsNullOrWhiteSpace(azureKey) && !string.IsNullOrWhiteSpace(azureRegion))
        _azure = new AzureTtsEngine(azureKey, azureRegion);
    }
    public void PlayMp3(string path)
    {
      if (string.IsNullOrWhiteSpace(path))
      {
        Logger.Warn("[OfflineAudio] PlayMp3: chemin vide → silence (segment sera clamp a 0.5s)");
        LastDuration = TimeSpan.Zero; LastAudioPath = null; return;
      }
      if (!File.Exists(path))
      {
        Logger.Warn($"[OfflineAudio] PlayMp3: FICHIER INTROUVABLE '{path}' → silence (segment sera clamp a 0.5s)");
        LastDuration = TimeSpan.Zero; LastAudioPath = null; return;
      }
      try
      {
        using var reader = new Mp3FileReader(path);
        LastDuration  = reader.TotalTime;
        LastAudioPath = path;
        Segments.Add(new AudioSegment(path, LastDuration.Value, AudioSourceKind.Mp3));
        Logger.Info($"[OfflineAudio] PlayMp3 OK: '{path}' duration={LastDuration.Value.TotalSeconds:F2}s");
      }
      catch (Exception ex)
      {
        Logger.Error($"[OfflineAudio] PlayMp3 failed for '{path}'", ex);
        LastDuration = TimeSpan.Zero; LastAudioPath = null;
      }
    }
    public void Speak(string text, string? voice)
    {
      if (string.IsNullOrWhiteSpace(text))
      {
        LastDuration = TimeSpan.Zero; LastAudioPath = null; return;
      }
      var wavPath = Path.Combine(_tempDir, $"tts_{Guid.NewGuid():N}.wav");
      if (_azure != null)
      {
        var voiceName = AzureTtsEngine.ResolveVoiceName(voice);
        TimeSpan? dur = null;
        try
        {
          dur = Task.Run(() => _azure.SynthToWavFileAsync(text, voiceName, wavPath))
                    .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
          Logger.Warn($"[AzureTts] Exception sur '{text.Substring(0, Math.Min(40, text.Length))}…' : {ex.GetType().Name} — {ex.Message}");
        }
        if (dur.HasValue && File.Exists(wavPath) && new FileInfo(wavPath).Length > 44)
        {
          LastDuration  = dur.Value;
          LastAudioPath = wavPath;
          Segments.Add(new AudioSegment(wavPath, dur.Value, AudioSourceKind.TtsWav));
          return;
        }
        var reason = !dur.HasValue             ? "synthèse nulle (result != Completed)"
                   : !File.Exists(wavPath)     ? "wavPath introuvable"
                   : "WAV vide (≤ 44 bytes)";
        Logger.Warn($"[AzureTts] Fallback SAPI — raison : {reason} — voix={voiceName} — texte='{text.Substring(0, Math.Min(40, text.Length))}…'");
        try { if (File.Exists(wavPath)) File.Delete(wavPath); } catch { }
      }
      SpeakSapi(text, voice, wavPath);
    }
    private void SpeakSapi(string text, string? voice, string wavPath)
    {
      try
      {
        SelectVoiceSafely(voice);
        using var ms = new MemoryStream();
        _speech.SetOutputToWaveStream(ms);
        _speech.Speak(text);
        _speech.SetOutputToNull();
        ms.Position = 0;
        File.WriteAllBytes(wavPath, ms.ToArray());
        ms.Position = 0;
        using var waveReader = new WaveFileReader(ms);
        LastDuration  = waveReader.TotalTime;
        LastAudioPath = wavPath;
        Segments.Add(new AudioSegment(wavPath, LastDuration.Value, AudioSourceKind.TtsWav));
      }
      catch { LastDuration = TimeSpan.Zero; LastAudioPath = null; }
    }
    public void StopAll() { }
    public void SetLevelSink(SpeakerKind who) { }
    public void StartMicLevelMonitor(IEnumerable<string>? micPreferences) { }
    public bool TryRestartMic(IEnumerable<string>? micPreferences) => false;
    public void CleanTempFiles()
    {
      foreach (var seg in Segments)
        if (seg.Kind == AudioSourceKind.TtsWav && File.Exists(seg.Path))
          try { File.Delete(seg.Path); } catch { }
    }
    public void Dispose() => _speech.Dispose();
    private void SelectVoiceSafely(string? voice)
    {
      if (string.IsNullOrWhiteSpace(voice)) return;
      try
      {
        var ci = new System.Globalization.CultureInfo(voice);
        var v  = _speech.GetInstalledVoices()
                        .FirstOrDefault(x => x.Enabled && x.VoiceInfo.Culture.Equals(ci));
        if (v != null) { _speech.SelectVoice(v.VoiceInfo.Name); return; }
      }
      catch { }
      try { _speech.SelectVoice(voice); } catch { }
    }
  }
  public enum AudioSourceKind { Mp3, TtsWav }
  public sealed record AudioSegment(
    string          Path,
    TimeSpan        Duration,
    AudioSourceKind Kind
  );
}