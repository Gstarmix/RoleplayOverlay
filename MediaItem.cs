using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
namespace RoleplayOverlay
{
  public sealed class MediaText
  {
    [JsonProperty("content")]    public string Content    { get; set; } = "";
    [JsonProperty("fontSizePx")] public int    FontSizePx { get; set; } = 90;
    [JsonProperty("colorHex")]   public string ColorHex   { get; set; } = "#FFFFFF";
    [JsonProperty("bold")]       public bool   Bold       { get; set; } = true;
    [JsonProperty("outline")]    public bool   Outline    { get; set; } = true;
    [JsonProperty("align")]      public string Align      { get; set; } = "center";
    public MediaText Clone() => new MediaText
    {
      Content = Content, FontSizePx = FontSizePx, ColorHex = ColorHex,
      Bold = Bold, Outline = Outline, Align = Align,
    };
  }
  public sealed class MediaItem : INotifyPropertyChanged
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
    private string? _path;
    private string? _sourcePath;
    private float?  _trimIn;
    private float?  _trimOut;
    private int     _cropLeft;
    private int     _cropTop;
    private int     _cropRight;
    private int     _cropBottom;
    private string  _cropGravity = "center";
    private float   _speed = 1.0f;
    private double  _appearAt;
    private double  _appearDur;
    private double  _posX = 0.5;
    private double  _posY = 0.5;
    private double  _scale = 0.8;
    private double  _panX;
    private double  _panY;
    private string  _anim = "none";
    private bool    _loop = true;
    private double  _cropFL, _cropFT, _cropFR, _cropFB;
    private bool    _border = true;
    private bool    _contain;
    private bool    _panAnim;
    private double  _panX2, _panY2;
    private double  _panT1, _panDur;
    private MediaText? _text;
    [JsonProperty("path")]
    public string? Path { get => _path; set => SetField(ref _path, value); }
    [JsonProperty("sourcePath", NullValueHandling = NullValueHandling.Ignore)]
    public string? SourcePath { get => _sourcePath; set => SetField(ref _sourcePath, value); }
    [JsonProperty("trimIn", NullValueHandling = NullValueHandling.Ignore)]
    public float? TrimIn { get => _trimIn; set => SetField(ref _trimIn, value); }
    [JsonProperty("trimOut", NullValueHandling = NullValueHandling.Ignore)]
    public float? TrimOut { get => _trimOut; set => SetField(ref _trimOut, value); }
    [JsonProperty("cropLeft", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public int CropLeft { get => _cropLeft; set => SetField(ref _cropLeft, value); }
    [JsonProperty("cropTop", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public int CropTop { get => _cropTop; set => SetField(ref _cropTop, value); }
    [JsonProperty("cropRight", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public int CropRight { get => _cropRight; set => SetField(ref _cropRight, value); }
    [JsonProperty("cropBottom", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public int CropBottom { get => _cropBottom; set => SetField(ref _cropBottom, value); }
    [JsonProperty("cropGravity", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string CropGravity { get => _cropGravity; set => SetField(ref _cropGravity, value); }
    [JsonProperty("speed", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public float Speed { get => _speed; set => SetField(ref _speed, value); }
    [JsonProperty("appearAt", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public double AppearAt { get => _appearAt; set => SetField(ref _appearAt, value); }
    [JsonProperty("appearDur", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public double AppearDur { get => _appearDur; set => SetField(ref _appearDur, value); }
    [JsonProperty("posX", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public double PosX { get => _posX; set => SetField(ref _posX, value); }
    [JsonProperty("posY", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public double PosY { get => _posY; set => SetField(ref _posY, value); }
    [JsonProperty("scale", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public double Scale { get => _scale; set => SetField(ref _scale, value); }
    [JsonProperty("panX", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public double PanX { get => _panX; set => SetField(ref _panX, value); }
    [JsonProperty("panY", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public double PanY { get => _panY; set => SetField(ref _panY, value); }
    [JsonProperty("anim", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string Anim { get => _anim; set => SetField(ref _anim, value); }
    [JsonProperty("loop")]
    public bool Loop { get => _loop; set => SetField(ref _loop, value); }
    [JsonProperty("cropFL", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public double CropFL { get => _cropFL; set => SetField(ref _cropFL, value); }
    [JsonProperty("cropFT", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public double CropFT { get => _cropFT; set => SetField(ref _cropFT, value); }
    [JsonProperty("cropFR", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public double CropFR { get => _cropFR; set => SetField(ref _cropFR, value); }
    [JsonProperty("cropFB", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public double CropFB { get => _cropFB; set => SetField(ref _cropFB, value); }
    [JsonProperty("border")]
    public bool Border { get => _border; set => SetField(ref _border, value); }
    [JsonProperty("contain", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public bool Contain { get => _contain; set => SetField(ref _contain, value); }
    [JsonProperty("panAnim", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public bool PanAnim { get => _panAnim; set => SetField(ref _panAnim, value); }
    [JsonProperty("panX2", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public double PanX2 { get => _panX2; set => SetField(ref _panX2, value); }
    [JsonProperty("panY2", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public double PanY2 { get => _panY2; set => SetField(ref _panY2, value); }
    [JsonProperty("panT1", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public double PanT1 { get => _panT1; set => SetField(ref _panT1, value); }
    [JsonProperty("panDur", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public double PanDur { get => _panDur; set => SetField(ref _panDur, value); }
    [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
    public MediaText? Text { get => _text; set => SetField(ref _text, value); }
    public MediaItem Clone() => new MediaItem
    {
      _path        = _path,
      _sourcePath  = _sourcePath,
      _trimIn      = _trimIn,
      _trimOut     = _trimOut,
      _cropLeft    = _cropLeft,
      _cropTop     = _cropTop,
      _cropRight   = _cropRight,
      _cropBottom  = _cropBottom,
      _cropGravity = _cropGravity,
      _speed       = _speed,
      _appearAt    = _appearAt,
      _appearDur   = _appearDur,
      _posX        = _posX,
      _posY        = _posY,
      _scale       = _scale,
      _panX        = _panX,
      _panY        = _panY,
      _anim        = _anim,
      _loop        = _loop,
      _cropFL      = _cropFL,
      _cropFT      = _cropFT,
      _cropFR      = _cropFR,
      _cropFB      = _cropFB,
      _border      = _border,
      _contain     = _contain,
      _panAnim     = _panAnim,
      _panX2       = _panX2,
      _panY2       = _panY2,
      _panT1       = _panT1,
      _panDur      = _panDur,
      _text        = _text?.Clone(),
    };
    public static MediaItem FromLegacyFields(
      string? path, string? sourcePath,
      float? trimIn, float? trimOut,
      int cropLeft, int cropTop, int cropRight, int cropBottom,
      string cropGravity, float speed)
    {
      return new MediaItem
      {
        _path        = path,
        _sourcePath  = sourcePath,
        _trimIn      = trimIn,
        _trimOut     = trimOut,
        _cropLeft    = cropLeft,
        _cropTop     = cropTop,
        _cropRight   = cropRight,
        _cropBottom  = cropBottom,
        _cropGravity = cropGravity ?? "center",
        _speed       = speed,
      };
    }
    public void Normalize()
    {
      if (Math.Abs(_scale - 0.5) < 1e-6) Scale = 0.8;
      Speed       = Math.Clamp(_speed, 0.1f, 4.0f);
      CropLeft    = Math.Max(0, _cropLeft);
      CropTop     = Math.Max(0, _cropTop);
      CropRight   = Math.Max(0, _cropRight);
      CropBottom  = Math.Max(0, _cropBottom);
    }
  }
}