using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
namespace RoleplayOverlay
{
  public partial class WebViewWindow : Window
  {
    private static readonly HttpClient Http = new HttpClient();
    public static void OpenUrl(string url)
    {
      var w = new WebViewWindow();
      w.Show();
      w.Activate();
      if (!string.IsNullOrWhiteSpace(url))
      {
        try { w.Web.Source = new Uri(url); }
        catch { w.Web.Source = new Uri("https://www.bing.com/search?q=" + Uri.EscapeDataString(url)); }
      }
    }
    public WebViewWindow()
    {
      InitializeComponent();
      InputBindings.Add(new KeyBinding(new RelayCommand(_ => Close()), new KeyGesture(Key.W, ModifierKeys.Control)));
      InputBindings.Add(new KeyBinding(new RelayCommand(_ => AddressBox.Focus()), new KeyGesture(Key.L, ModifierKeys.Control)));
      InputBindings.Add(new KeyBinding(new RelayCommand(_ => Reload()), new KeyGesture(Key.F5)));
      InputBindings.Add(new KeyBinding(new RelayCommand(_ => GoBack()), new KeyGesture(Key.Left, ModifierKeys.Alt)));
      InputBindings.Add(new KeyBinding(new RelayCommand(_ => GoForward()), new KeyGesture(Key.Right, ModifierKeys.Alt)));
      Loaded += (_, __) => OverlayWindow.Current?.BringBubblesToFront();
      Activated += (_, __) => OverlayWindow.Current?.BringBubblesToFront();
      Deactivated += (_, __) => OverlayWindow.Current?.BringBubblesToFront();
      StateChanged += (_, __) => OverlayWindow.Current?.BringBubblesToFront();
      LocationChanged += (_, __) => OverlayWindow.Current?.BringBubblesToFront();
      SizeChanged += (_, __) => OverlayWindow.Current?.BringBubblesToFront();
      Web.NavigationCompleted += (_, __) =>
      {
        UpdateNavUi();
        OverlayWindow.Current?.BringBubblesToFront();
      };
      Web.CoreWebView2InitializationCompleted += (_, e) =>
      {
        if (!e.IsSuccess || Web.CoreWebView2 == null) return;
        var core = Web.CoreWebView2;
        Try(() => core.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark);
        Try(() => core.Settings.IsStatusBarEnabled = false);
        Try(() => core.Settings.AreDefaultContextMenusEnabled = true);
        Try(() => core.Settings.IsBuiltInErrorPageEnabled = true);
        Try(() => core.Settings.AreDevToolsEnabled = true);
        core.DocumentTitleChanged += (_, __) =>
        {
          Title = string.IsNullOrWhiteSpace(core.DocumentTitle) ? "Web" : core.DocumentTitle;
        };
        core.ContainsFullScreenElementChanged += (_, __) =>
        {
          OverlayWindow.Current?.BringBubblesToFront();
        };
        core.NewWindowRequested += async (_, args) =>
        {
          args.Handled = true;
          var def = args.GetDeferral();
          try
          {
            var w = new WebViewWindow();
            w.Show();
            await w.Web.EnsureCoreWebView2Async();
            args.NewWindow = w.Web.CoreWebView2;
            if (!string.IsNullOrWhiteSpace(args.Uri)) w.AddressBox.Text = args.Uri;
            w.Activate();
          }
          catch { }
          finally { def.Complete(); }
          OverlayWindow.Current?.BringBubblesToFront();
        };
        core.HistoryChanged += (_, __) => UpdateNavUi();
      };
      _ = Web.EnsureCoreWebView2Async();
    }
    private void OnAddressKeyDown(object s, WpfKeyEventArgs e)
    {
      if (e.Key != Key.Enter) return;
      var t = AddressBox.Text?.Trim();
      if (string.IsNullOrWhiteSpace(t)) return;
      if (!t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
          !t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        t = "https://" + t;
      try { Web.Source = new Uri(t); }
      catch { Web.Source = new Uri("https://www.bing.com/search?q=" + Uri.EscapeDataString(t)); }
    }
    private void OnBack(object s, RoutedEventArgs e)    => GoBack();
    private void OnForward(object s, RoutedEventArgs e) => GoForward();
    private void OnReload(object s, RoutedEventArgs e)  => Reload();
    private void GoBack()    { var c = Web.CoreWebView2; if (c?.CanGoBack == true) c.GoBack(); }
    private void GoForward() { var c = Web.CoreWebView2; if (c?.CanGoForward == true) c.GoForward(); }
    private void Reload()
    {
      try { Web.Reload(); }
      catch { Web.CoreWebView2?.Reload(); }
    }
    private string TryGetSource()
    {
      try { return Web.CoreWebView2?.Source ?? Web.Source?.ToString() ?? ""; }
      catch { return ""; }
    }
    private void UpdateNavUi()
    {
      var c = Web.CoreWebView2;
      try
      {
        BtnBack.IsEnabled = c?.CanGoBack == true;
        BtnForward.IsEnabled = c?.CanGoForward == true;
        AddressBox.Text = TryGetSource();
      }
      catch { }
    }
    private static void Try(Action a) { try { a(); } catch { } }
    private sealed class RelayCommand : ICommand
    {
      private readonly Action<object?> _run;
      public RelayCommand(Action<object?> run) { _run = run; }
      public bool CanExecute(object? parameter) => true;
      public void Execute(object? parameter) => _run(parameter);
      public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
  }
}