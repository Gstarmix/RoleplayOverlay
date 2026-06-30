using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
namespace RoleplayOverlay
{
  public static class ScriptToVoice
  {
    [DllImport("kernel32.dll")] private static extern bool AttachConsole(int dwProcessId);
    private const int ATTACH_PARENT_PROCESS = -1;
    public static (bool ok, string message, string? outPath, double seconds) Generate(
      string input, bool azure, string? voice, string? outPath)
    {
      try
      {
        if (string.IsNullOrWhiteSpace(input) || !File.Exists(input))
          return (false, "Script introuvable.", null, 0);
        string spoken = ExtractSpoken(ReadText(input));
        if (string.IsNullOrWhiteSpace(spoken))
          return (false, "Rien a dire dans ce script (que des reperes ?).", null, 0);
        voice ??= azure ? AzureTtsEngine.DefaultVoiceFR : "fr-FR";
        string key = "", region = "";
        bool useAzure = azure;
        if (useAzure)
        {
          (key, region) = AzureConfig.Load();
          if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(region))
            useAzure = false;
        }
        string clean  = TtsHelper.SanitizeForTts(spoken, voice);
        string outWav = string.IsNullOrWhiteSpace(outPath) ? Path.ChangeExtension(input, ".wav") : outPath!;
        string tempDir = Path.Combine(Path.GetTempPath(), "RoleplayOverlay_tts");
        using var engine = new OfflineAudioEngine(tempDir, useAzure, key, region);
        engine.Speak(clean, voice);
        if (engine.LastAudioPath == null || !File.Exists(engine.LastAudioPath))
          return (false, "Echec de la synthese (aucun audio produit).", null, 0);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outWav))!);
        File.Copy(engine.LastAudioPath, outWav, overwrite: true);
        try { File.Delete(engine.LastAudioPath); } catch { }
        double secs = engine.LastDuration?.TotalSeconds ?? 0;
        try { Logger.Info($"[ScriptToVoice] OK '{input}' -> '{outWav}' ({secs:F1}s, azure={useAzure})"); } catch { }
        string moteur = useAzure ? "Azure" : "SAPI";
        return (true, $"OK ({moteur}) -> {outWav}  ({secs:F1} s)", outWav, secs);
      }
      catch (Exception ex)
      {
        try { Logger.Error("[ScriptToVoice] echec", ex); } catch { }
        return (false, "Erreur : " + ex.Message, null, 0);
      }
    }
    public static string? FindScriptsDir()
    {
      try
      {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && d != null; i++, d = d.Parent)
        {
          string s = Path.Combine(d.FullName, "scripts");
          if (Directory.Exists(s)) return s;
        }
      }
      catch { }
      return null;
    }
    public static int Run(string[] args)
    {
      AttachConsole(ATTACH_PARENT_PROCESS);
      string? input = null, outPath = null, voice = null;
      bool azure = false;
      for (int i = 1; i < args.Length; i++)
      {
        string a = args[i];
        switch (a.ToLowerInvariant())
        {
          case "--azure": case "--prod": azure = true; break;
          case "--voix": case "--voice": if (i + 1 < args.Length) voice = args[++i]; break;
          case "--out": if (i + 1 < args.Length) outPath = args[++i]; break;
          default: if (!a.StartsWith("--") && input == null) input = a; break;
        }
      }
      if (string.IsNullOrWhiteSpace(input))
      {
        Console.WriteLine("Usage : RoleplayOverlay.exe --tts <script.txt> [--azure] [--voix <nom>] [--out <sortie.wav>]");
        Console.WriteLine("Defaut : SAPI (hors-ligne). --azure : voix Azure (cle requise).");
        return 2;
      }
      var res = Generate(input!, azure, voice, outPath);
      Console.WriteLine(res.message);
      return res.ok ? 0 : 1;
    }
    private static string ReadText(string path)
    {
      string text = File.ReadAllText(path);
      if (text.Contains('�'))
        text = File.ReadAllText(path, Encoding.Latin1);
      return text;
    }
    private static string ExtractSpoken(string raw)
    {
      var sb = new StringBuilder();
      foreach (string line in raw.Replace("\r\n", "\n").Split('\n'))
      {
        string t = line.Trim();
        if (t.Length == 0) continue;
        if (t[0] == '[' || t[0] == '#' || t[0] == '>') continue;
        sb.AppendLine(t);
      }
      return sb.ToString().Trim();
    }
  }
}