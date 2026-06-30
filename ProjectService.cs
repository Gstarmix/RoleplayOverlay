using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
namespace RoleplayOverlay
{
  public sealed class ProjectService
  {
    private Project _project;
    private string? _filePath;
    private bool    _isDirty;
    public Project Project => _project;
    public string? FilePath
    {
      get => _filePath;
      private set
      {
        if (_filePath == value) return;
        _filePath = value;
        FilePathChanged?.Invoke(this, value);
      }
    }
    public bool IsDirty
    {
      get => _isDirty;
      private set
      {
        if (_isDirty == value) return;
        _isDirty = value;
        DirtyChanged?.Invoke(this, value);
      }
    }
    public event EventHandler<Project>? ProjectLoaded;
    public event EventHandler<bool>? DirtyChanged;
    public event EventHandler<string>? ProjectSaved;
    public event EventHandler<string?>? FilePathChanged;
    public event EventHandler<string>? ErrorOccurred;
    public ProjectService()
    {
      _project = Project.CreateDefault();
    }
    public ProjectService(Project initialProject)
    {
      _project = initialProject ?? Project.CreateDefault();
    }
    public bool Load(string path)
    {
      if (string.IsNullOrWhiteSpace(path))
      {
        ErrorOccurred?.Invoke(this, "Chemin de fichier vide.");
        return false;
      }
      if (!File.Exists(path))
      {
        ErrorOccurred?.Invoke(this, "Fichier introuvable.");
        return false;
      }
      try
      {
        var json = File.ReadAllText(path);
        var loaded = JsonConvert.DeserializeObject<Project>(json);
        if (loaded == null)
        {
          ErrorOccurred?.Invoke(this, "JSON invalide.");
          return false;
        }
        foreach (var scene in loaded.Scenes)
          foreach (var seq in scene.Sequences)
            seq.Normalize();
        _project = loaded;
        FilePath = path;
        IsDirty  = false;
        ProjectLoaded?.Invoke(this, _project);
        return true;
      }
      catch (Exception ex)
      {
        ErrorOccurred?.Invoke(this, $"Erreur de chargement\n{ex.Message}");
        return false;
      }
    }
    public void SetProject(Project project, string? filePath = null)
    {
      _project = project ?? Project.CreateDefault();
      FilePath = filePath;
      IsDirty  = false;
      ProjectLoaded?.Invoke(this, _project);
    }
    public bool Save()
    {
      if (string.IsNullOrWhiteSpace(_filePath))
        return false;
      return SaveAs(_filePath!);
    }
    public bool SaveAs(string path)
    {
      try
      {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
          Directory.CreateDirectory(dir);
        var json = JsonConvert.SerializeObject(_project, Formatting.Indented);
        File.WriteAllText(path, json);
        FilePath = path;
        IsDirty  = false;
        ProjectSaved?.Invoke(this, path);
        return true;
      }
      catch (Exception ex)
      {
        ErrorOccurred?.Invoke(this, $"Erreur de sauvegarde\n{ex.Message}");
        return false;
      }
    }
    public void SyncFromGrid(IEnumerable<Sequence> gridItems)
    {
      var scene = _project.CurrentScene;
      if (scene == null)
      {
        scene = new Scene();
        _project.Scenes.Add(scene);
        _project.CurrentSceneId = scene.Id;
      }
      scene.Sequences = gridItems.Select(s => s.Clone()).ToList();
    }
    public void MarkDirty()
    {
      IsDirty = true;
    }
    public void SyncAndMarkDirty(IEnumerable<Sequence> gridItems)
    {
      SyncFromGrid(gridItems);
      MarkDirty();
    }
    public void LoadCurrentSceneInto(ObservableCollection<Sequence> target)
    {
      target.Clear();
      var scene = _project.CurrentScene;
      if (scene?.Sequences == null) return;
      foreach (var s in scene.Sequences)
        target.Add(s.Clone());
    }
    public Scene AddScene(string? name = null)
    {
      var scene = new Scene
      {
        Id        = Guid.NewGuid().ToString("N"),
        Name      = name ?? $"Scène {_project.Scenes.Count + 1}",
        Sequences = new List<Sequence>()
      };
      _project.Scenes.Add(scene);
      _project.CurrentSceneId = scene.Id;
      MarkDirty();
      return scene;
    }
    public void RenameScene(Scene scene, string newName)
    {
      if (scene == null || string.IsNullOrWhiteSpace(newName)) return;
      scene.Name = newName.Trim();
      MarkDirty();
    }
    public bool DeleteScene(Scene scene)
    {
      if (scene == null || _project.Scenes.Count <= 1) return false;
      var idx = _project.Scenes.IndexOf(scene);
      if (idx < 0) return false;
      _project.Scenes.Remove(scene);
      if (_project.CurrentSceneId == scene.Id)
      {
        var newIdx = Math.Min(idx, _project.Scenes.Count - 1);
        _project.CurrentSceneId = _project.Scenes[newIdx].Id;
      }
      MarkDirty();
      return true;
    }
    public Scene DuplicateScene(Scene scene)
    {
      if (scene == null) return AddScene();
      var json   = Newtonsoft.Json.JsonConvert.SerializeObject(scene.Sequences);
      var seqCopy = Newtonsoft.Json.JsonConvert.DeserializeObject<List<Sequence>>(json) ?? new List<Sequence>();
      foreach (var s in seqCopy) s.Id = Guid.NewGuid().ToString("N");
      var copy = new Scene
      {
        Id        = Guid.NewGuid().ToString("N"),
        Name      = scene.Name + " (copie)",
        Sequences = seqCopy,
      };
      var idx = _project.Scenes.IndexOf(scene);
      _project.Scenes.Insert(idx + 1, copy);
      _project.CurrentSceneId = copy.Id;
      MarkDirty();
      return copy;
    }
    public bool MoveScene(Scene scene, int direction)
    {
      if (scene == null) return false;
      var idx = _project.Scenes.IndexOf(scene); if (idx < 0) return false;
      var newIdx = idx + direction;
      if (newIdx < 0 || newIdx >= _project.Scenes.Count) return false;
      _project.Scenes.RemoveAt(idx);
      _project.Scenes.Insert(newIdx, scene);
      MarkDirty();
      return true;
    }
    public void SelectScene(Scene scene)
    {
      if (scene != null)
        _project.CurrentSceneId = scene.Id;
    }
    public string WindowTitle
    {
      get
      {
        var fileName = string.IsNullOrWhiteSpace(_filePath)
          ? "Sans titre"
          : Path.GetFileName(_filePath);
        var dirty = _isDirty ? " ●" : "";
        return $"RoleplayOverlay — Studio — {fileName}{dirty}";
      }
    }
  }
}