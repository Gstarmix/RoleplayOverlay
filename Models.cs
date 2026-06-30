using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
namespace RoleplayOverlay
{
  public enum SpeakerKind { You, Bot1, Bot2 }
  public sealed class Sequence : INotifyPropertyChanged
  {
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
      => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
      if (EqualityComparer<T>.Default.Equals(field, value)) return false;
      field = value;
      OnPropertyChanged(name);
      return true;
    }
    private string? _id;
    private string? _mode = "tts";
    private string? _mp3;
    private string? _camClipPath;
    private string? _text;
    private bool    _showText = true;
    private string? _voice;
    private string? _speaker = "bot1";
    private string? _note;
    private string? _pairId;
    private bool    _mediaUnlinked;
    private string? _mediaPath;
    [JsonProperty("mediaSourcePath", NullValueHandling = NullValueHandling.Ignore)]
    public string? MediaSourcePath { get; set; }
    [JsonProperty("mp3SourcePath", NullValueHandling = NullValueHandling.Ignore)]
    public string? Mp3SourcePath { get; set; }
    private float   _mediaScale       = 0.80f;
    private float   _mediaSpeed       = 1.0f;
    private bool    _mediaLoop        = true;
    private string  _mediaBorderColor = "#FFFFFF";
    private int     _mediaBorderPx    = 6;
    private int     _mediaShadowBlur  = 18;
    private float   _mediaShadowAlpha = 0.55f;
    private float?  _mediaTrimIn;
    private float?  _mediaTrimOut;
    private int     _mediaCropLeft;
    private int     _mediaCropTop;
    private int     _mediaCropRight;
    private int     _mediaCropBottom;
    private string  _mediaCropGravity = "center";
    private int _mediaGap = 6;
    [JsonProperty("id")]
    public string? Id { get => _id; set => SetField(ref _id, value); }
    [JsonProperty("mode")]
    public string? Mode { get => _mode; set => SetField(ref _mode, value); }
    [JsonProperty("mp3")]
    public string? Mp3 { get => _mp3; set => SetField(ref _mp3, value); }
    [JsonProperty("camClipPath", NullValueHandling = NullValueHandling.Ignore)]
    public string? CamClipPath { get => _camClipPath; set => SetField(ref _camClipPath, value); }
    [JsonProperty("text")]
    public string? Text { get => _text; set => SetField(ref _text, value); }
    [JsonProperty("showText")]
    public bool ShowText { get => _showText; set => SetField(ref _showText, value); }
    [JsonProperty("voice")]
    public string? Voice { get => _voice; set => SetField(ref _voice, value); }
    [JsonProperty("speaker")]
    public string? Speaker { get => _speaker; set => SetField(ref _speaker, value); }
    [JsonProperty("note")]
    public string? Note { get => _note; set => SetField(ref _note, value); }
    [JsonProperty("pairId", NullValueHandling = NullValueHandling.Ignore)]
    public string? PairId
    {
      get => _pairId;
      set { if (SetField(ref _pairId, value)) OnPropertyChanged(nameof(IsPaired)); }
    }
    [JsonProperty("mediaUnlinked", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public bool MediaUnlinked { get => _mediaUnlinked; set => SetField(ref _mediaUnlinked, value); }
    [JsonIgnore]
    public bool IsPaired => !string.IsNullOrWhiteSpace(_pairId);
    private TimeSpan _estimatedDuration;
    private bool     _durationIsExact;
    [JsonIgnore]
    public TimeSpan EstimatedDuration
    {
      get => _estimatedDuration;
      set { if (SetField(ref _estimatedDuration, value)) OnPropertyChanged(nameof(DurationDisplay)); }
    }
    [JsonIgnore]
    public bool DurationIsExact
    {
      get => _durationIsExact;
      set { if (SetField(ref _durationIsExact, value)) OnPropertyChanged(nameof(DurationDisplay)); }
    }
    [JsonIgnore]
    public string DurationDisplay
    {
      get
      {
        if (_estimatedDuration == TimeSpan.Zero) return "";
        var sec = _estimatedDuration.TotalSeconds;
        return _durationIsExact ? $"{sec:F1}s" : $"~{sec:F1}s";
      }
    }
    [JsonProperty("mediaPath")]
    public string? MediaPath { get => _mediaPath; set => SetField(ref _mediaPath, value); }
    [JsonProperty("mediaScale")]
    public float MediaScale { get => _mediaScale; set => SetField(ref _mediaScale, value); }
    [JsonProperty("mediaSpeed")]
    public float MediaSpeed { get => _mediaSpeed; set => SetField(ref _mediaSpeed, value); }
    [JsonProperty("mediaLoop")]
    public bool MediaLoop { get => _mediaLoop; set => SetField(ref _mediaLoop, value); }
    [JsonProperty("mediaBorderColor")]
    public string MediaBorderColor { get => _mediaBorderColor; set => SetField(ref _mediaBorderColor, value); }
    [JsonProperty("mediaBorderPx")]
    public int MediaBorderPx { get => _mediaBorderPx; set => SetField(ref _mediaBorderPx, value); }
    [JsonProperty("mediaShadowBlur")]
    public int MediaShadowBlur { get => _mediaShadowBlur; set => SetField(ref _mediaShadowBlur, value); }
    [JsonProperty("mediaShadowAlpha")]
    public float MediaShadowAlpha { get => _mediaShadowAlpha; set => SetField(ref _mediaShadowAlpha, value); }
    [JsonProperty("mediaTrimIn", NullValueHandling = NullValueHandling.Ignore)]
    public float? MediaTrimIn { get => _mediaTrimIn; set => SetField(ref _mediaTrimIn, value); }
    [JsonProperty("mediaTrimOut", NullValueHandling = NullValueHandling.Ignore)]
    public float? MediaTrimOut { get => _mediaTrimOut; set => SetField(ref _mediaTrimOut, value); }
    [JsonIgnore]
    public float? MediaTrimDuration =>
      (_mediaTrimIn.HasValue && _mediaTrimOut.HasValue && _mediaTrimOut > _mediaTrimIn)
        ? _mediaTrimOut.Value - _mediaTrimIn.Value
        : null;
    [JsonProperty("mediaCropLeft", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public int MediaCropLeft { get => _mediaCropLeft; set => SetField(ref _mediaCropLeft, value); }
    [JsonProperty("mediaCropTop", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public int MediaCropTop { get => _mediaCropTop; set => SetField(ref _mediaCropTop, value); }
    [JsonProperty("mediaCropRight", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public int MediaCropRight { get => _mediaCropRight; set => SetField(ref _mediaCropRight, value); }
    [JsonProperty("mediaCropBottom", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public int MediaCropBottom { get => _mediaCropBottom; set => SetField(ref _mediaCropBottom, value); }
    [JsonIgnore]
    public bool HasCrop => _mediaCropLeft > 0 || _mediaCropTop > 0 || _mediaCropRight > 0 || _mediaCropBottom > 0;
    [JsonProperty("mediaCropGravity", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string MediaCropGravity { get => _mediaCropGravity; set => SetField(ref _mediaCropGravity, value); }
    [JsonProperty("mediaGap", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public int MediaGap { get => _mediaGap; set => SetField(ref _mediaGap, value); }
    [JsonProperty("mediaItems", NullValueHandling = NullValueHandling.Ignore)]
    public List<MediaItem>? MediaItems { get; set; }
    [JsonIgnore]
    public bool HasMediaItems =>
      MediaItems != null && MediaItems.Count > 0 &&
      MediaItems.Any(m => !string.IsNullOrWhiteSpace(m.Path));
    [JsonIgnore]
    public int MediaItemCount => MediaItems?.Count(m => !string.IsNullOrWhiteSpace(m.Path)) ?? 0;
    private static readonly Regex ImgTagRegex = new(
      @"\[img\s+src=""([^""]+)""\]",
      RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public Sequence Clone()
    {
      var clone = new Sequence
      {
        _id = _id, _mode = _mode, _mp3 = _mp3, _text = _text,
        _showText = _showText, _voice = _voice, _speaker = _speaker, _note = _note,
        _pairId = _pairId, _mediaUnlinked = _mediaUnlinked,
        _mediaPath = _mediaPath, _mediaScale = _mediaScale, _mediaSpeed = _mediaSpeed,
        _mediaLoop = _mediaLoop, _mediaBorderColor = _mediaBorderColor,
        _mediaBorderPx = _mediaBorderPx, _mediaShadowBlur = _mediaShadowBlur,
        _mediaShadowAlpha = _mediaShadowAlpha,
        _mediaTrimIn = _mediaTrimIn, _mediaTrimOut = _mediaTrimOut,
        _mediaCropLeft = _mediaCropLeft, _mediaCropTop = _mediaCropTop,
        _mediaCropRight = _mediaCropRight, _mediaCropBottom = _mediaCropBottom,
        _mediaCropGravity = _mediaCropGravity,
        _mediaGap = _mediaGap,
        MediaItems = MediaItems?.Select(m => m.Clone()).ToList(),
      };
      if (string.IsNullOrWhiteSpace(clone._mediaPath) && !string.IsNullOrWhiteSpace(clone._text))
      {
        var m = ImgTagRegex.Match(clone._text);
        if (m.Success)
        {
          clone._mediaPath = m.Groups[1].Value;
          clone._text = ImgTagRegex.Replace(clone._text, "").Trim();
        }
      }
      return clone;
    }
    public void Normalize()
    {
      if (string.IsNullOrWhiteSpace(_mode))
        Mode = string.IsNullOrWhiteSpace(_mp3) ? "tts" : "mp3";
      if (string.IsNullOrWhiteSpace(_speaker)) Speaker = "bot1";
      Note ??= "";
      MediaScale       = Math.Clamp(_mediaScale, 0.1f, 1.0f);
      MediaSpeed       = Math.Clamp(_mediaSpeed, 0.1f, 4.0f);
      MediaBorderPx    = Math.Max(0, _mediaBorderPx);
      MediaShadowBlur  = Math.Max(0, _mediaShadowBlur);
      MediaShadowAlpha = Math.Clamp(_mediaShadowAlpha, 0f, 1f);
      MediaCropLeft    = Math.Max(0, _mediaCropLeft);
      MediaCropTop     = Math.Max(0, _mediaCropTop);
      MediaCropRight   = Math.Max(0, _mediaCropRight);
      MediaCropBottom  = Math.Max(0, _mediaCropBottom);
      MediaGap = Math.Clamp(_mediaGap, 0, 30);
      if (MediaItems == null && !string.IsNullOrWhiteSpace(_mediaPath))
      {
        MediaItems = new List<MediaItem>
        {
          MediaItem.FromLegacyFields(
            _mediaPath, MediaSourcePath,
            _mediaTrimIn, _mediaTrimOut,
            _mediaCropLeft, _mediaCropTop, _mediaCropRight, _mediaCropBottom,
            _mediaCropGravity, _mediaSpeed)
        };
      }
      if (MediaItems != null)
      {
        foreach (var mi in MediaItems)
          mi.Normalize();
      }
    }
  }
  public sealed class Scene
  {
    [JsonProperty("id")]        public string Id { get; set; } = "default";
    [JsonProperty("name")]      public string Name { get; set; } = "Default";
    [JsonProperty("sequences")] public List<Sequence> Sequences { get; set; } = new();
    [JsonProperty("slidesDirectory", NullValueHandling = NullValueHandling.Include)]
    public string? SlidesDirectory { get; set; }
    public override string ToString() => Name;
  }
  public sealed class VADSettings
  {
    [JsonProperty("thresholdDb")]     public double ThresholdDb { get; set; } = -35.0;
    [JsonProperty("attackMs")]        public int AttackMs { get; set; } = 60;
    [JsonProperty("releaseMs")]       public int ReleaseMs { get; set; } = 180;
    [JsonProperty("autocalibration")] public bool Autocalibration { get; set; } = true;
  }
  public sealed class GlobalSettings
  {
    [JsonProperty("ttsProvider")] public string TtsProvider { get; set; } = "LocalTts";
    [JsonProperty("voice")]       public string Voice { get; set; } = "fr-FR";
    [JsonProperty("nextHotkey")]       public string? NextHotkey { get; set; } = "Ctrl+Alt+Right";
    [JsonProperty("prevHotkey")]       public string? PrevHotkey { get; set; } = "Ctrl+Alt+Left";
    [JsonProperty("stopHotkey")]       public string? StopHotkey { get; set; } = "Ctrl+Alt+Space";
    [JsonProperty("toggleHotkey")]     public string? ToggleHotkey { get; set; } = "Ctrl+Alt+O";
    [JsonProperty("textToggleHotkey")] public string? TextToggleHotkey { get; set; } = "Ctrl+Alt+T";
    [JsonProperty("layoutModeHotkey")] public string? LayoutModeHotkey { get; set; } = "Ctrl+Alt+M";
    [JsonProperty("nextHotkeyYou")]  public string? NextHotkeyYou { get; set; }
    [JsonProperty("prevHotkeyYou")]  public string? PrevHotkeyYou { get; set; }
    [JsonProperty("nextHotkeyBot1")] public string? NextHotkeyBot1 { get; set; }
    [JsonProperty("prevHotkeyBot1")] public string? PrevHotkeyBot1 { get; set; }
    [JsonProperty("nextHotkeyBot2")] public string? NextHotkeyBot2 { get; set; } = "Alt+Shift+Right";
    [JsonProperty("prevHotkeyBot2")] public string? PrevHotkeyBot2 { get; set; } = "Alt+Shift+Left";
    [JsonProperty("vad")]       public VADSettings Vad { get; set; } = new();
    [JsonProperty("duckingDb")] public double DuckingDb { get; set; } = -12;
    [JsonProperty("youImage")]  public string? YouImage  { get; set; } = @"C:\RoleplayOverlay\image\you.png";
    [JsonProperty("bot1Image")] public string? Bot1Image { get; set; } = @"C:\RoleplayOverlay\image\bot1.png";
    [JsonProperty("bot2Image")] public string? Bot2Image { get; set; } = @"C:\RoleplayOverlay\image\bot2.png";
    [JsonProperty("micPreferences")] public List<string> MicPreferences { get; set; } = new() { "ME6S", "HyperX" };
    [JsonProperty("screenDevice")]   public string? ScreenDevice { get; set; }
    [JsonProperty("screenIndex")]    public int? ScreenIndex { get; set; }
    [JsonProperty("showYou")]  public bool ShowYou  { get; set; } = false;
    [JsonProperty("showBot1")] public bool ShowBot1 { get; set; } = false;
    [JsonProperty("showBot2")] public bool ShowBot2 { get; set; } = false;
  }
  public sealed class Project
  {
    [JsonProperty("projectName")]    public string ProjectName { get; set; } = "Roleplay Demo";
    [JsonProperty("global")]         public GlobalSettings Global { get; set; } = new();
    [JsonProperty("scenes")]         public List<Scene> Scenes { get; set; } = new();
    [JsonProperty("currentSceneId")] public string? CurrentSceneId { get; set; } = "intro";
    [JsonIgnore]
    public Scene? CurrentScene
    {
      get
      {
        if (Scenes == null || Scenes.Count == 0) return null;
        if (!string.IsNullOrWhiteSpace(CurrentSceneId))
        {
          var s = Scenes.Find(x => string.Equals(x.Id, CurrentSceneId, StringComparison.OrdinalIgnoreCase));
          if (s != null) return s;
        }
        return Scenes[0];
      }
    }
    public static Project CreateDefault() => new()
    {
      ProjectName = "Roleplay Demo",
      Global = new GlobalSettings(),
      Scenes = new List<Scene>
      {
        new Scene
        {
          Id = "intro", Name = "Introduction",
          Sequences = new List<Sequence>
          {
            new Sequence { Id="s1", Speaker="bot1", Mode="tts", Text="Bonjour !" },
            new Sequence { Id="s2", Speaker="bot2", Mode="tts", Text="Salut, prêt ?" },
            new Sequence { Id="s3", Speaker="you",  Mode="tts", Text="On y va." }
          }
        }
      },
      CurrentSceneId = "intro"
    };
    public static Project Load(string path)
    {
      try
      {
        if (!File.Exists(path)) return CreateDefault();
        var json = File.ReadAllText(path);
        var proj = JsonConvert.DeserializeObject<Project>(json) ?? CreateDefault();
        foreach (var scene in proj.Scenes)
          foreach (var seq in scene.Sequences)
            seq.Normalize();
        return proj;
      }
      catch { return CreateDefault(); }
    }
    public void Save(string path)
    {
      try
      {
        var json = JsonConvert.SerializeObject(this, Formatting.Indented);
        File.WriteAllText(path, json);
      }
      catch { }
    }
  }
}