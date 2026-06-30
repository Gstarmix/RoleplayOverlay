using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
namespace RoleplayOverlay
{
  internal static class RenderCli
  {
    [DllImport("kernel32.dll")] private static extern bool AttachConsole(int dwProcessId);
    private const int ATTACH_PARENT_PROCESS = -1;
    public static async Task<int> RunAsync(string[] args)
    {
      AttachConsole(ATTACH_PARENT_PROCESS);
      try
      {
        string? projectPath = null, slidesDir = null, outPath = null;
        bool portrait = false, subs = false, azure = false;
        for (int i = 1; i < args.Length; i++)
        {
          var a = args[i];
          switch (a.ToLowerInvariant())
          {
            case "--portrait": portrait = true; break;
            case "--subs":     subs = true; break;
            case "--azure":    azure = true; break;
            case "--slides":   if (i + 1 < args.Length) slidesDir = args[++i]; break;
            case "--out":      if (i + 1 < args.Length) outPath = args[++i]; break;
            default:
              if (!a.StartsWith("--") && projectPath == null) projectPath = a;
              break;
          }
        }
        if (projectPath == null || !File.Exists(projectPath))
        {
          Console.Error.WriteLine("Usage: --render <project.json> [--portrait] [--slides <dir>] [--out <mp4>] [--subs] [--azure]");
          Console.Error.WriteLine("  projet introuvable : " + (projectPath ?? "(aucun)"));
          return 2;
        }
        projectPath = Path.GetFullPath(projectPath);
        var project = Project.Load(projectPath);
        if (project.CurrentScene == null)
        {
          Console.Error.WriteLine("[render] projet sans scene.");
          return 3;
        }
        slidesDir = Path.GetFullPath(slidesDir ?? Path.GetDirectoryName(projectPath) ?? ".");
        var manifest = RenderManifest.ScaffoldFromProject(project, slidesDir);
        if (manifest.Segments.Count == 0)
        {
          Console.Error.WriteLine("[render] aucun segment (verifie les notes 'slide N' des sequences).");
          return 4;
        }
        if (outPath == null)
        {
          var outDir = Path.Combine(Path.GetDirectoryName(projectPath) ?? ".", "exports");
          Directory.CreateDirectory(outDir);
          outPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(projectPath) + "_render.mp4");
        }
        outPath = Path.GetFullPath(outPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        var settings = new RenderSettings
        {
          Aspect           = portrait ? VideoAspect.Portrait : VideoAspect.Landscape,
          ShowAvatars      = false,
          BurnSubtitles    = subs,
          UseAzureTts      = azure,
          FfmpegBinaryPath = Directory.Exists(@"C:\ffmpeg\bin") ? @"C:\ffmpeg\bin" : null,
        };
        var rs      = new RenderService(new ProjectService(project));
        var tempDir = Path.Combine(Path.GetTempPath(), "ro_render_cli");
        Directory.CreateDirectory(tempDir);
        var options = rs.BuildOptions(settings, outPath, tempDir);
        Console.WriteLine($"[render] projet={Path.GetFileName(projectPath)} slides={slidesDir} " +
                          $"format={(portrait ? "1080x1920" : "1920x1080")} segments={manifest.Segments.Count}");
        Console.WriteLine($"[render] sortie -> {outPath}");
        var result = await RenderPipeline.BuildAsync(manifest, project, options);
        if (result.Success && result.OutputPath != null && File.Exists(result.OutputPath))
        {
          Console.WriteLine($"[render] OK ({result.SegmentCount} segments, {result.ElapsedTime.TotalSeconds:F1}s) : {result.OutputPath}");
          return 0;
        }
        Console.Error.WriteLine("[render] echec : " + (result.ErrorMessage ?? "(inconnu)"));
        return 5;
      }
      catch (Exception ex)
      {
        Console.Error.WriteLine("[render] exception : " + ex);
        return 1;
      }
    }
  }
}