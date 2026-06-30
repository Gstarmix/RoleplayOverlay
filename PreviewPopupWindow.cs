using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color              = System.Windows.Media.Color;
using Image              = System.Windows.Controls.Image;
using KeyEventArgs       = System.Windows.Input.KeyEventArgs;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment   = System.Windows.VerticalAlignment;
namespace RoleplayOverlay
{
  public sealed class PreviewPopupWindow : Window
  {
    private readonly Image _image;
    private bool _isMaximized;
    public PreviewPopupWindow(ImageSource source, string title = "Aperçu composé")
    {
      Title = $"RoleplayOverlay — {title}";
      WindowStartupLocation = WindowStartupLocation.CenterOwner;
      ResizeMode = ResizeMode.CanResize;
      WindowStyle = WindowStyle.SingleBorderWindow;
      Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0C));
      MinWidth = 640;
      MinHeight = 400;
      var screen = SystemParameters.WorkArea;
      Width  = screen.Width  * 0.8;
      Height = screen.Height * 0.8;
      _image = new Image
      {
        Source = source,
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
      };
      System.Windows.Media.RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
      var grid = new Grid();
      grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
      grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      grid.Children.Add(_image);
      Grid.SetRow(_image, 0);
      var hint = new TextBlock
      {
        Text = "Esc = fermer · Double-clic = plein écran",
        Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
        FontSize = 10,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 6, 0, 6),
      };
      grid.Children.Add(hint);
      Grid.SetRow(hint, 1);
      Content = grid;
      KeyDown += OnKeyDown;
      MouseDoubleClick += OnMouseDoubleClick;
    }
    public void UpdateImage(ImageSource source)
    {
      _image.Source = source;
    }
    public static PreviewPopupWindow FromFile(string pngPath, string title = "Aperçu composé")
    {
      var bmp = new BitmapImage();
      bmp.BeginInit();
      bmp.CacheOption = BitmapCacheOption.OnLoad;
      bmp.UriSource = new Uri(pngPath, UriKind.Absolute);
      bmp.EndInit();
      bmp.Freeze();
      return new PreviewPopupWindow(bmp, title);
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
      if (e.Key == Key.Escape)
        Close();
      else if (e.Key == Key.F11)
        ToggleFullscreen();
    }
    private void OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
      ToggleFullscreen();
    }
    private void ToggleFullscreen()
    {
      if (_isMaximized)
      {
        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.SingleBorderWindow;
        _isMaximized = false;
      }
      else
      {
        WindowStyle = WindowStyle.None;
        WindowState = WindowState.Maximized;
        _isMaximized = true;
      }
    }
  }
}