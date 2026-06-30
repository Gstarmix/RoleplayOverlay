using System;
using System.Threading;
namespace RoleplayOverlay
{
  public sealed class AutoSaveService : IDisposable
  {
    private readonly ProjectService _projectService;
    private readonly System.Threading.Timer _timer;
    private readonly int            _debounceMs;
    private          bool           _disposed;
    public event EventHandler? Saving;
    public event EventHandler<string>? Saved;
    public event EventHandler<string>? Failed;
    public event EventHandler? Pending;
    public AutoSaveService(ProjectService projectService, int debounceMs = 2000)
    {
      _projectService = projectService ?? throw new ArgumentNullException(nameof(projectService));
      _debounceMs     = Math.Max(500, debounceMs);
      _timer = new System.Threading.Timer(OnTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
      _projectService.DirtyChanged += OnDirtyChanged;
      _projectService.ProjectSaved += OnManualSave;
    }
    private void OnDirtyChanged(object? sender, bool isDirty)
    {
      if (_disposed) return;
      if (isDirty)
      {
        _timer.Change(_debounceMs, Timeout.Infinite);
        Pending?.Invoke(this, EventArgs.Empty);
      }
      else
      {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
      }
    }
    private void OnManualSave(object? sender, string path)
    {
      if (!_disposed)
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }
    private void OnTimerElapsed(object? state)
    {
      if (_disposed) return;
      if (string.IsNullOrWhiteSpace(_projectService.FilePath))
      {
        Failed?.Invoke(this, "Auto-save ignoré : aucun fichier de projet défini.");
        return;
      }
      if (!_projectService.IsDirty)
        return;
      try
      {
        Saving?.Invoke(this, EventArgs.Empty);
        if (_projectService.Save())
        {
          Saved?.Invoke(this, _projectService.FilePath!);
        }
        else
        {
          Failed?.Invoke(this, "Auto-save échoué.");
        }
      }
      catch (Exception ex)
      {
        Failed?.Invoke(this, $"Auto-save erreur : {ex.Message}");
      }
    }
    public void FlushNow()
    {
      if (_disposed) return;
      _timer.Change(Timeout.Infinite, Timeout.Infinite);
      OnTimerElapsed(null);
    }
    public void Cancel()
    {
      if (!_disposed)
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }
    public void Dispose()
    {
      if (_disposed) return;
      _disposed = true;
      _projectService.DirtyChanged -= OnDirtyChanged;
      _projectService.ProjectSaved -= OnManualSave;
      _timer.Change(Timeout.Infinite, Timeout.Infinite);
      _timer.Dispose();
    }
  }
}