using System;
using System.IO;
using Newtonsoft.Json;
namespace RoleplayOverlay
{
  public static class ProjectStorage
  {
    private static string DefaultFile =>
      Path.Combine(AppContext.BaseDirectory, "project.json");
    public static Project LoadOrCreateDefault()
    {
      return LoadOrCreateDefault(DefaultFile);
    }
    public static Project LoadOrCreateDefault(string path)
    {
      try
      {
        if (File.Exists(path))
        {
          var json = File.ReadAllText(path);
          var proj = JsonConvert.DeserializeObject<Project>(json);
          if (proj != null)
          {
            foreach (var scene in proj.Scenes)
              foreach (var seq in scene.Sequences)
                seq.Normalize();
            return proj;
          }
        }
      }
      catch {  }
      var def = Project.CreateDefault();
      Save(def, path);
      return def;
    }
    public static void Save(Project project)
    {
      Save(project, DefaultFile);
    }
    public static void Save(Project project, string path)
    {
      Directory.CreateDirectory(Path.GetDirectoryName(path)!);
      var json = JsonConvert.SerializeObject(project, Formatting.Indented);
      File.WriteAllText(path, json);
    }
    public static Project Load(string path)
    {
      var json = File.ReadAllText(path);
      var proj = JsonConvert.DeserializeObject<Project>(json) ?? new Project();
      foreach (var scene in proj.Scenes)
        foreach (var seq in scene.Sequences)
          seq.Normalize();
      return proj;
    }
    public static void SaveAs(Project project, string path) => Save(project, path);
  }
}