using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Speech.Synthesis;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
namespace RoleplayOverlay
{
  public sealed class Audio : IDisposable
  {
    private readonly WaveOutEvent            _output;
    private          MixingSampleProvider    _mixer;
    private readonly List<IDisposable>       _toDispose = new();
    private readonly object                  _lock      = new();
    private readonly SpeechSynthesizer _speech = new();
    private WasapiCapture? _micWasapi;
    private WaveInEvent?   _micWaveIn;
    private readonly object _micLock = new();
    private readonly OverlayWindow? _overlay;
    private Action<float>? _levelSink;
    public Audio(OverlayWindow? overlay)
    {
      _overlay = overlay;
      var fmt = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
      _mixer  = new MixingSampleProvider(fmt) { ReadFully = true };
      _output = new WaveOutEvent { DesiredLatency = 120 };
      _output.Init(_mixer);
      _output.Play();
      _speech.Rate   = 0;
      _speech.Volume = 100;
    }
    public void Dispose()
    {
      StopAll();
      StopMicLocked();
      _speech.Dispose();
      _output.Dispose();
    }
    public void SetLevelSink(SpeakerKind who)
    {
      ResetAllLevels();
      _levelSink = who switch
      {
        SpeakerKind.You  => v => _overlay?.SetYouLevel(v),
        SpeakerKind.Bot1 => v => _overlay?.SetBot1Level(v),
        SpeakerKind.Bot2 => v => _overlay?.SetBot2Level(v),
        _                => v => _overlay?.SetBot1Level(v),
      };
    }
    private void ResetAllLevels()
    {
      try { _overlay?.SetYouLevel(0f);  } catch { }
      try { _overlay?.SetBot1Level(0f); } catch { }
      try { _overlay?.SetBot2Level(0f); } catch { }
    }
    public void StopAll()
    {
      try { _output.Stop(); } catch { }
      lock (_lock)
      {
        foreach (var d in _toDispose) { try { d.Dispose(); } catch { } }
        _toDispose.Clear();
        var fmt = _mixer.WaveFormat;
        _mixer = new MixingSampleProvider(fmt) { ReadFully = true };
        try { _output.Init(_mixer); } catch { }
        try { _output.Play();       } catch { }
      }
      ResetAllLevels();
    }
    public void Stop() => StopAll();
    public void PlayMp3(string path)
    {
      if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
      StopAll();
      var file = new AudioFileReader(path);
      lock (_lock)
      {
        _toDispose.Add(file);
      }
      ISampleProvider src = NormalizeToMixer(file);
      AddMeteredInput(src);
    }
    public void Speak(string text) => Speak(text, null);
    public void Speak(string text, string? voice)
    {
      if (string.IsNullOrWhiteSpace(text)) return;
      StopAll();
      SelectVoiceSafely(voice);
      var mem = new MemoryStream();
      _speech.SetOutputToWaveStream(mem);
      _speech.Speak(text);
      _speech.SetOutputToNull();
      mem.Position = 0;
      var waveReader = new WaveFileReader(mem);
      lock (_lock)
      {
        _toDispose.Add(mem);
        _toDispose.Add(waveReader);
      }
      ISampleProvider src = NormalizeToMixer(waveReader.ToSampleProvider());
      AddMeteredInput(src);
    }
    private ISampleProvider NormalizeToMixer(ISampleProvider src)
    {
      if (src.WaveFormat.Channels == 1)
        src = new MonoToStereoSampleProvider(src);
      if (src.WaveFormat.SampleRate != _mixer.WaveFormat.SampleRate)
        src = new WdlResamplingSampleProvider(src, _mixer.WaveFormat.SampleRate);
      return src;
    }
    private void AddMeteredInput(ISampleProvider src)
    {
      var met = new MeteringSampleProvider(src);
      met.StreamVolume += (_, e) =>
      {
        float lvl = e.MaxSampleValues.Length > 0 ? Math.Abs(e.MaxSampleValues[0]) : 0f;
        _levelSink?.Invoke(lvl);
      };
      var endAware = new EndAwareSampleProvider(met, () =>
      {
        try { _levelSink?.Invoke(0f); } catch { }
      });
      var vol = new VolumeSampleProvider(endAware) { Volume = 1.0f };
      lock (_lock)
      {
        _mixer.AddMixerInput(vol);
      }
    }
    private sealed class EndAwareSampleProvider : ISampleProvider
    {
      private readonly ISampleProvider _src;
      private readonly Action          _onEnd;
      private          int             _ended;
      public EndAwareSampleProvider(ISampleProvider src, Action onEnd)
      {
        _src   = src;
        _onEnd = onEnd;
        WaveFormat = src.WaveFormat;
      }
      public WaveFormat WaveFormat { get; }
      public int Read(float[] buffer, int offset, int count)
      {
        int n = _src.Read(buffer, offset, count);
        if (n == 0 && Interlocked.Exchange(ref _ended, 1) == 0)
        {
          try { _onEnd(); } catch { }
        }
        return n;
      }
    }
    private void SelectVoiceSafely(string? voice)
    {
      if (string.IsNullOrWhiteSpace(voice)) return;
      try
      {
        var ci = new CultureInfo(voice);
        var v  = _speech.GetInstalledVoices()
                        .FirstOrDefault(x => x.Enabled && x.VoiceInfo.Culture.Equals(ci));
        if (v != null) { _speech.SelectVoice(v.VoiceInfo.Name); return; }
      }
      catch {  }
      try { _speech.SelectVoice(voice); } catch { }
    }
    public void StartMicLevelMonitor(IEnumerable<string>? micPreferences)
    {
      lock (_micLock)
      {
        StopMicLocked();
      }
      TryRestartMic(micPreferences);
    }
    public bool TryRestartMic(IEnumerable<string>? micPreferences)
    {
      lock (_micLock)
      {
        StopMicLocked();
      }
      if (TryStartWasapiMic(micPreferences)) return true;
      return TryStartWaveInMic(micPreferences);
    }
    private void StopMicLocked()
    {
      var wasapi  = Interlocked.Exchange(ref _micWasapi, null);
      var waveIn  = Interlocked.Exchange(ref _micWaveIn, null);
      if (wasapi != null)
      {
        try { wasapi.StopRecording(); } catch { }
        try { wasapi.Dispose();       } catch { }
      }
      if (waveIn != null)
      {
        try { waveIn.StopRecording(); } catch { }
        try { waveIn.Dispose();       } catch { }
      }
    }
    private bool TryStartWasapiMic(IEnumerable<string>? micPreferences)
    {
      try
      {
        using var en  = new MMDeviceEnumerator();
        var all       = en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList();
        MMDevice? chosen = null;
        if (micPreferences != null)
        {
          foreach (var pref in micPreferences)
          {
            var p = pref?.Trim();
            if (string.IsNullOrEmpty(p)) continue;
            chosen = all.FirstOrDefault(d =>
              d.FriendlyName.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0 ||
              d.ID.Equals(p, StringComparison.OrdinalIgnoreCase));
            if (chosen != null) break;
          }
        }
        chosen ??= en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
        if (chosen == null) return false;
        var capture = new WasapiCapture(chosen) { ShareMode = AudioClientShareMode.Shared };
        capture.DataAvailable += OnWasapiData;
        capture.RecordingStopped += (_, _) =>
        {
          _overlay?.SetYouLevel(0f);
        };
        capture.StartRecording();
        lock (_micLock) { _micWasapi = capture; }
        return true;
      }
      catch
      {
        return false;
      }
    }
    private void OnWasapiData(object? sender, WaveInEventArgs a)
    {
      try
      {
        if (sender is not WasapiCapture cap) return;
        var wf       = cap.WaveFormat;
        int channels = Math.Max(1, wf.Channels);
        int bps      = Math.Max(1, wf.BitsPerSample / 8);
        int total    = a.BytesRecorded / bps;
        if (total <= 0) return;
        int    frames = total / channels;
        if (frames <= 0) return;
        double sum    = 0.0;
        if (wf.Encoding == WaveFormatEncoding.IeeeFloat && bps == 4)
        {
          var wb  = new WaveBuffer(a.Buffer);
          int idx = 0;
          for (int i = 0; i < frames; i++)
          {
            double acc = 0.0;
            for (int c = 0; c < channels; c++) acc += Math.Abs(wb.FloatBuffer[idx++]);
            sum += Math.Pow(acc / channels, 2);
          }
        }
        else if (wf.BitsPerSample == 16 && bps == 2)
        {
          var wb  = new WaveBuffer(a.Buffer);
          int idx = 0;
          for (int i = 0; i < frames; i++)
          {
            double acc = 0.0;
            for (int c = 0; c < channels; c++) acc += Math.Abs(wb.ShortBuffer[idx++] / 32768.0);
            sum += Math.Pow(acc / channels, 2);
          }
        }
        else return;
        float rms = (float)Math.Sqrt(sum / frames);
        _overlay?.SetYouLevel(Math.Clamp(rms, 0f, 1f));
      }
      catch { }
    }
    private bool TryStartWaveInMic(IEnumerable<string>? micPreferences)
    {
      try
      {
        int deviceNumber = -1;
        if (micPreferences != null)
        {
          for (int i = 0; i < WaveInEvent.DeviceCount; i++)
          {
            var caps = WaveIn.GetCapabilities(i);
            string name = caps.ProductName ?? "";
            if (micPreferences.Any(p =>
              !string.IsNullOrWhiteSpace(p) &&
              name.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0))
            {
              deviceNumber = i;
              break;
            }
          }
        }
        var waveIn = new WaveInEvent
        {
          DeviceNumber = deviceNumber,
          WaveFormat   = new WaveFormat(44100, 16, 1)
        };
        waveIn.DataAvailable += (_, a) =>
        {
          int bytes = a.BytesRecorded;
          if (bytes <= 0) return;
          int samples = bytes / 2;
          var wb      = new WaveBuffer(a.Buffer);
          double sum  = 0.0;
          for (int i = 0; i < samples; i++)
          {
            float f = wb.ShortBuffer[i] / 32768f;
            sum += f * f;
          }
          float rms = (float)Math.Sqrt(sum / Math.Max(1, samples));
          _overlay?.SetYouLevel(Math.Clamp(rms, 0f, 1f));
        };
        waveIn.RecordingStopped += (_, _) => _overlay?.SetYouLevel(0f);
        waveIn.StartRecording();
        lock (_micLock) { _micWaveIn = waveIn; }
        return true;
      }
      catch
      {
        return false;
      }
    }
  }
}