using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace RoleplayOverlay
{
  public sealed class MosaicThumbnailConverter : System.Windows.Data.IValueConverter
  {
    public static readonly MosaicThumbnailConverter Instance = new();
    private const int ThumbW = 86;
    private const int ThumbH = 38;
    private const int MosaicGap = 1;
    private static readonly Dictionary<string, BitmapSource?> _mosaicCache = new();
    public object? Convert(object value, Type t, object p, System.Globalization.CultureInfo c)
    {
      if (value is not Sequence seq) return null;
      if (seq.MediaItems == null || seq.MediaItems.Count <= 1)
        return MediaThumbnailConverter.Instance.Convert(seq.MediaPath, t, p, c);
      var validItems = seq.MediaItems
        .Where(m => !string.IsNullOrWhiteSpace(m.Path) && File.Exists(m.Path))
        .ToList();
      if (validItems.Count <= 1)
        return MediaThumbnailConverter.Instance.Convert(seq.MediaPath, t, p, c);
      var cacheKey = string.Join("|", validItems.Select(m => m.Path));
      if (_mosaicCache.TryGetValue(cacheKey, out var cached))
        return cached;
      var result = ComposeMosaic(validItems);
      _mosaicCache[cacheKey] = result;
      return result;
    }
    public static void InvalidateCache() => _mosaicCache.Clear();
    private static BitmapSource? ComposeMosaic(List<MediaItem> items)
    {
      try
      {
        var cells = MosaicLayout.GetCells(items.Count, ThumbW, ThumbH, MosaicGap);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
          dc.DrawRectangle(System.Windows.Media.Brushes.Black, null, new Rect(0, 0, ThumbW, ThumbH));
          for (int i = 0; i < Math.Min(items.Count, cells.Count); i++)
          {
            var item = items[i];
            var cell = cells[i];
            var rect = new Rect(cell.X, cell.Y, cell.Width, cell.Height);
            var thumb = LoadThumb(item.Path!);
            if (thumb != null)
            {
              dc.DrawImage(thumb, rect);
            }
            else
            {
              dc.DrawRectangle(
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2A, 0x2B, 0x2E)),
                null, rect);
              var icon = IsVideo(item.Path!) ? "\u25B6" : "?";
              var ft = new FormattedText(icon,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                Math.Max(8, cell.Height * 0.5),
                System.Windows.Media.Brushes.Gray,
                VisualTreeHelper.GetDpi(dv).PixelsPerDip);
              dc.DrawText(ft,
                new System.Windows.Point(cell.X + (cell.Width - ft.Width) / 2,
                          cell.Y + (cell.Height - ft.Height) / 2));
            }
          }
        }
        var rtb = new RenderTargetBitmap(ThumbW, ThumbH, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
      }
      catch
      {
        return null;
      }
    }
    private static BitmapImage? LoadThumb(string path)
    {
      try
      {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp"))
          return null;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path, UriKind.Absolute);
        bmp.DecodePixelWidth = 86;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
      }
      catch { return null; }
    }
    private static bool IsVideo(string path)
    {
      var ext = Path.GetExtension(path).ToLowerInvariant();
      return ext is ".mp4" or ".webm" or ".mov" or ".avi" or ".mkv" or ".gif";
    }
    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c)
      => throw new NotSupportedException();
  }
  public sealed class HasMosaicConverter : System.Windows.Data.IValueConverter
  {
    public static readonly HasMosaicConverter Instance = new();
    public static readonly HasMosaicConverter Inverted = new() { _invert = true };
    private bool _invert;
    public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c)
    {
      bool hasMosaic = false;
      if (value is Sequence seq && seq.MediaItems != null)
      {
        hasMosaic = seq.MediaItems.Count(m => !string.IsNullOrWhiteSpace(m.Path)
          && File.Exists(m.Path)) >= 2;
      }
      if (_invert) hasMosaic = !hasMosaic;
      return hasMosaic ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c)
      => throw new NotSupportedException();
  }
  public sealed class MosaicFileNameConverter : System.Windows.Data.IValueConverter
  {
    public static readonly MosaicFileNameConverter Instance = new();
    public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c)
    {
      if (value is not Sequence seq) return "";
      if (seq.MediaItems != null && seq.MediaItems.Count >= 2)
      {
        int valid = seq.MediaItems.Count(m => !string.IsNullOrWhiteSpace(m.Path));
        return $"{valid} medias";
      }
      if (!string.IsNullOrWhiteSpace(seq.MediaPath))
        return Path.GetFileName(seq.MediaPath);
      return "";
    }
    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c)
      => throw new NotSupportedException();
  }
}