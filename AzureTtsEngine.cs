using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using NAudio.Wave;
namespace RoleplayOverlay
{
  public sealed class AzureTtsEngine
  {
    public const string DefaultVoiceFR = "fr-FR-HenriNeural";
    public const string DefaultVoiceEN = "en-US-GuyNeural";
    public static string ResolveVoiceName(string? voice)
    {
      if (string.IsNullOrWhiteSpace(voice)) return DefaultVoiceFR;
      if (voice.Length > 5 && voice[5] == '-') return voice;
      return voice.ToLowerInvariant() switch
      {
        "fr-fr" or "fr" => DefaultVoiceFR,
        "en-us" or "en" => DefaultVoiceEN,
        "en-gb"         => "en-GB-RyanNeural",
        _               => DefaultVoiceFR
      };
    }
    private readonly string _key;
    private readonly string _region;
    public AzureTtsEngine(string key, string region)
    {
      _key    = key;
      _region = region;
    }
    public bool IsConfigured =>
      !string.IsNullOrWhiteSpace(_key) && !string.IsNullOrWhiteSpace(_region);
    public async Task<TimeSpan?> SynthToWavFileAsync(string text, string voiceName, string wavPath)
    {
      if (!IsConfigured) { Logger.Warn("[AzureTts] Non configuré (clé ou région manquante)"); return null; }
      var config = SpeechConfig.FromSubscription(_key, _region);
      config.SpeechSynthesisVoiceName = voiceName;
      var cleanText = SanitizeText(text);
      if (string.IsNullOrWhiteSpace(cleanText)) return TimeSpan.Zero;
      using var audioConfig = AudioConfig.FromWavFileOutput(wavPath);
      using var synth       = new SpeechSynthesizer(config, audioConfig);
      var result = await synth.SpeakTextAsync(cleanText);
      if (result.Reason == ResultReason.SynthesizingAudioCompleted)
      {
        try { using var reader = new WaveFileReader(wavPath); return reader.TotalTime; }
        catch { return TimeSpan.FromMilliseconds(result.AudioDuration.TotalMilliseconds); }
      }
      if (result.Reason == ResultReason.Canceled)
      {
        var details = SpeechSynthesisCancellationDetails.FromResult(result);
        Logger.Warn($"[AzureTts] Synthèse annulée — code={details.ErrorCode} — reason={details.Reason} — details='{details.ErrorDetails}'");
      }
      else
      {
        Logger.Warn($"[AzureTts] Résultat inattendu : {result.Reason}");
      }
      try { if (File.Exists(wavPath)) File.Delete(wavPath); } catch { }
      return null;
    }
    public async Task<(byte[]? wav, TimeSpan duration)> SynthToMemoryAsync(string text, string voiceName)
    {
      if (!IsConfigured) return (null, TimeSpan.Zero);
      var config = SpeechConfig.FromSubscription(_key, _region);
      config.SpeechSynthesisVoiceName = voiceName;
      config.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Riff44100Hz16BitMonoPcm);
      var cleanText = SanitizeText(text);
      if (string.IsNullOrWhiteSpace(cleanText)) return (null, TimeSpan.Zero);
      using var synth  = new SpeechSynthesizer(config, null);
      var       result = await synth.SpeakTextAsync(cleanText);
      if (result.Reason == ResultReason.SynthesizingAudioCompleted)
      {
        var wav = result.AudioData;
        try
        {
          using var ms     = new MemoryStream(wav);
          using var reader = new WaveFileReader(ms);
          return (wav, reader.TotalTime);
        }
        catch { return (wav, result.AudioDuration); }
      }
      return (null, TimeSpan.Zero);
    }
    private static string SanitizeText(string text)
    {
      if (string.IsNullOrWhiteSpace(text)) return "";
      var s = Regex.Replace(text, @"\[img\s+src=""[^""]*""\]", "", RegexOptions.IgnoreCase);
      s = s.Replace("\n", " ").Trim();
      return s;
    }
  }
}