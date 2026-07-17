using System;
using System.IO;
using System.Text;
namespace RoleplayOverlay
{
  public static class Logger
  {
    private static readonly string LogDir =
      Path.Combine(@"C:\RoleplayOverlay", "logs");
    private static readonly int MaxAgeDays = 7;
    private static string? _sessionPath;
    private static readonly object _lock = new();
    public static void Init()
    {
      try
      {
        Directory.CreateDirectory(LogDir);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _sessionPath = Path.Combine(LogDir, $"session_{timestamp}.log");
        var sb = new StringBuilder();
        sb.AppendLine("╔══════════════════════════════════════════════════════════════╗");
        sb.AppendLine($"║  RoleplayOverlay — Session {timestamp}");
        sb.AppendLine($"║  Machine: {Environment.MachineName} / {Environment.OSVersion}");
        sb.AppendLine($"║  .NET: {Environment.Version}");
        sb.AppendLine("╚══════════════════════════════════════════════════════════════╝");
        sb.AppendLine();
        File.WriteAllText(_sessionPath, sb.ToString());
        CleanOldSessions();
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[Logger.Init] Failed: {ex.Message}");
      }
    }
    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg, Exception? ex = null)
    {
      if (ex != null)
      {
        var inner = ex.InnerException != null
          ? $"\n         Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}"
          : "";
        Write("ERROR", $"{msg}\n         {ex.GetType().Name}: {ex.Message}{inner}\n{IndentStack(ex.StackTrace)}");
      }
      else
        Write("ERROR", msg);
    }
    public static void Section(string title)
    {
      var line = $"─── {title} ───";
      Write("", line);
    }
    public static void FfmpegResult(string context, string exe, string args, int? exitCode, string? stderr, string? outputFile)
    {
      var sb = new StringBuilder();
      sb.AppendLine($"[FFMPEG] {context}");
      sb.AppendLine($"         exe:  {exe}");
      sb.AppendLine($"         args: {args}");
      sb.AppendLine($"         exit: {exitCode?.ToString() ?? "timeout/null"}");
      if (!string.IsNullOrWhiteSpace(outputFile))
      {
        var exists = File.Exists(outputFile);
        var size = exists ? new FileInfo(outputFile).Length : 0;
        sb.AppendLine($"         output: {outputFile} (exists={exists}, size={size})");
      }
      if (!string.IsNullOrWhiteSpace(stderr))
      {
        var truncated = stderr.Length > 2000
          ? stderr[..1000] + "\n         [...tronqué...]\n" + stderr[^800..]
          : stderr;
        sb.AppendLine($"         stderr:\n{IndentBlock(truncated)}");
      }
      WriteRaw(sb.ToString());
    }
    public static string? SessionPath => _sessionPath;
    private static void Write(string level, string msg)
    {
      var ts = DateTime.Now.ToString("HH:mm:ss.fff");
      var prefix = string.IsNullOrEmpty(level) ? "" : $"[{level}] ";
      var line = $"[{ts}] {prefix}{msg}";
      Console.Error.WriteLine(line);
      System.Diagnostics.Debug.WriteLine(line);
      WriteRaw(line + "\n");
    }
    private static void WriteRaw(string text)
    {
      if (_sessionPath == null) return;
      lock (_lock)
      {
        try
        {
          File.AppendAllText(_sessionPath, text);
        }
        catch { }
      }
    }
    private static void CleanOldSessions()
    {
      try
      {
        var cutoff = DateTime.Now.AddDays(-MaxAgeDays);
        foreach (var f in Directory.GetFiles(LogDir, "session_*.log"))
        {
          try
          {
            if (File.GetLastWriteTime(f) < cutoff)
              File.Delete(f);
          }
          catch { }
        }
      }
      catch { }
    }
    private static string IndentStack(string? stack)
    {
      if (string.IsNullOrWhiteSpace(stack)) return "         (no stack)";
      var lines = stack.Split('\n');
      var sb = new StringBuilder();
      foreach (var l in lines)
        sb.AppendLine($"           {l.TrimEnd()}");
      return sb.ToString().TrimEnd();
    }
    private static string IndentBlock(string text)
    {
      var lines = text.Split('\n');
      var sb = new StringBuilder();
      foreach (var l in lines)
        sb.AppendLine($"           {l.TrimEnd()}");
      return sb.ToString().TrimEnd();
    }
  }
}