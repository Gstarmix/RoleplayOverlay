using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
namespace RoleplayOverlay
{
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
      Speed       = Math.Clamp(_speed, 0.1f, 4.0f);
      CropLeft    = Math.Max(0, _cropLeft);
      CropTop     = Math.Max(0, _cropTop);
      CropRight   = Math.Max(0, _cropRight);
      CropBottom  = Math.Max(0, _cropBottom);
    }
  }
}