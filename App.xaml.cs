using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
namespace RoleplayOverlay
{
  public partial class App : Application
  {
    private Audio?               _audio;
    private AudioDeviceWatcher?  _deviceWatcher;
    private SequencePlayer?      _player;
    private OverlayWindow?       _overlay;
    private EditorWindow?        _editor;
    private ProjectService?      _projectService;
    private RenderService?       _renderService;
    private AutoSaveService?     _autoSave;
    private GlobalHotkeys?       _hotkeys;
    protected override void OnStartup(StartupEventArgs e)
    {
      base.OnStartup(e);
      if (e.Args.Length >= 1 && e.Args[0].Equals("--tts", StringComparison.OrdinalIgnoreCase))
      {
        Logger.Init();
        int code = ScriptToVoice.Run(e.Args);
        Shutdown(code);
        return;
      }
      if (e.Args.Length >= 1 && e.Args[0].Equals("--render", StringComparison.OrdinalIgnoreCase))
      {
        Logger.Init();
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Dispatcher.InvokeAsync(async () =>
        {
          int code = await RenderCli.RunAsync(e.Args);
          Shutdown(code);
        });
        return;
      }
      Logger.Init();
      Logger.Section("Application Startup");
      UserPrefs.Load();
      Logger.Info($"UserPrefs loaded (TrimGuideVisible={UserPrefs.TrimGuideVisible})");
      DispatcherUnhandledException += OnDispatcherUnhandledException;
      AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
      TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
      try
      {
        StartupCore();
        Logger.Info("Startup complete — all systems OK");
      }
      catch (Exception ex)
      {
        Logger.Error("FATAL STARTUP EXCEPTION", ex);
        try
        {
          MessageBox.Show(
            $"Erreur fatale au démarrage :\n\n{ex.Message}\n\nVoir le log : {Logger.SessionPath}",
            "RoleplayOverlay — Erreur fatale", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { }
        Shutdown(1);
      }
    }
    private void StartupCore()
    {
      Logger.Info("1. Creating OverlayWindow...");
      _overlay        = new OverlayWindow();
      _overlay.Closed += OnAnyWindowClosed;
      _overlay.Show();
      Logger.Info("1. OverlayWindow OK");
      Logger.Info("2. Creating Audio...");
      _audio = new Audio(_overlay);
      Logger.Info("2. Audio OK");
      Logger.Info("3. Creating services...");
      _projectService = new ProjectService(Project.CreateDefault());
      _renderService  = new RenderService(_projectService);
      _autoSave       = new AutoSaveService(_projectService, debounceMs: 2000);
      var project = _projectService.Project;
      var uniquePrefs = project.Global.MicPreferences
                          .Where(p => !string.IsNullOrWhiteSpace(p))
                          .Distinct()
                          .ToList();
      project.Global.MicPreferences = uniquePrefs;
      Logger.Info("3. Services OK");
      Logger.Info("4. Creating AudioDeviceWatcher...");
      _deviceWatcher = new AudioDeviceWatcher(_audio, uniquePrefs);
      Logger.Info("4. AudioDeviceWatcher OK");
      Logger.Info("5. Starting mic monitor...");
      _audio.StartMicLevelMonitor(uniquePrefs);
      Logger.Info("5. Mic monitor OK");
      Logger.Info("6. Creating SequencePlayer...");
      _player = new SequencePlayer(_audio, _overlay);
      _player.Preload(project);
      Logger.Info("6. SequencePlayer OK");
      Logger.Info("7. Creating EditorWindow...");
      _editor        = new EditorWindow(_projectService, _renderService, _autoSave, _player);
      _editor.Closed += OnAnyWindowClosed;
      _editor.Show();
      Logger.Info("7. EditorWindow OK");
      Logger.Info("8. Registering hotkeys...");
      _hotkeys = new GlobalHotkeys(_overlay, _player);
      _hotkeys.RegisterFromProject(project);
      Logger.Info("8. Hotkeys OK");
      Logger.Info("9. Setting avatar images...");
      var you = string.IsNullOrWhiteSpace(project.Global.YouImage)
        ? @"C:\RoleplayOverlay\image\you.png" : project.Global.YouImage!;
      var b1 = string.IsNullOrWhiteSpace(project.Global.Bot1Image)
        ? @"C:\RoleplayOverlay\image\bot1.png" : project.Global.Bot1Image!;
      var b2 = string.IsNullOrWhiteSpace(project.Global.Bot2Image)
        ? @"C:\RoleplayOverlay\image\bot2.png" : project.Global.Bot2Image!;
      _overlay.SetYouImage(you);
      _overlay.SetBot1Image(b1);
      _overlay.SetBot2Image(b2);
      _overlay.ApplyVisibilityFrom(project.Global);
      Logger.Info("9. Avatars OK");
    }
    private void OnAnyWindowClosed(object? sender, System.EventArgs e)
    {
      var windowName = sender?.GetType().Name ?? "unknown";
      Logger.Warn($"OnAnyWindowClosed fired by: {windowName} — shutting down");
      Shutdown();
    }
    protected override void OnExit(ExitEventArgs e)
    {
      Logger.Info($"OnExit (code={e.ApplicationExitCode})");
      _hotkeys?.Dispose();
      _autoSave?.Dispose();
      _deviceWatcher?.Dispose();
      _audio?.Dispose();
      Logger.Info("Cleanup complete — goodbye");
      base.OnExit(e);
    }
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
      e.Handled = true;
      Logger.Error("DispatcherUnhandledException", e.Exception);
      try
      {
        MessageBox.Show(
          $"Exception non gérée :\n\n{e.Exception.Message}\n\nVoir : {Logger.SessionPath}",
          "RoleplayOverlay — Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
      }
      catch { }
    }
    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
      if (e.ExceptionObject is Exception ex)
      {
        Logger.Error($"DomainUnhandledException (terminating={e.IsTerminating})", ex);
        try
        {
          MessageBox.Show(
            $"Exception fatale :\n\n{ex.Message}\n\nVoir : {Logger.SessionPath}",
            "RoleplayOverlay — Erreur fatale", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { }
      }
    }
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
      e.SetObserved();
      Logger.Error("UnobservedTaskException", e.Exception?.InnerException ?? e.Exception);
    }
  }
}