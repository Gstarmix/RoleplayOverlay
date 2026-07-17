using System;
using System.IO;
using Newtonsoft.Json;
namespace RoleplayOverlay
{
  public static class AzureConfig
  {
    private static readonly string ConfigPath = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
      "RoleplayOverlay", "azure_tts.json");
    public static AzureTtsEngine? TryGetAzureEngine()
    {
      try
      {
        if (!File.Exists(ConfigPath)) return null;
        var cfg = JsonConvert.DeserializeObject<AzureTtsConfigDto>(
          File.ReadAllText(ConfigPath));
        if (cfg == null) return null;
        if (string.IsNullOrWhiteSpace(cfg.Key) || string.IsNullOrWhiteSpace(cfg.Region))
          return null;
        return new AzureTtsEngine(cfg.Key, cfg.Region);
      }
      catch { return null; }
    }
    public static void Save(string key, string region)
    {
      var dir = Path.GetDirectoryName(ConfigPath)!;
      Directory.CreateDirectory(dir);
      File.WriteAllText(ConfigPath,
        JsonConvert.SerializeObject(
          new AzureTtsConfigDto { Key = key, Region = region },
          Formatting.Indented));
    }
    public static (string key, string region) Load()
    {
      try
      {
        if (!File.Exists(ConfigPath)) return ("", "");
        var cfg = JsonConvert.DeserializeObject<AzureTtsConfigDto>(
          File.ReadAllText(ConfigPath));
        return (cfg?.Key ?? "", cfg?.Region ?? "");
      }
      catch { return ("", ""); }
    }
  }
  public sealed class AzureTtsConfigDto
  {
    [JsonProperty("key")]    public string Key    { get; set; } = "";
    [JsonProperty("region")] public string Region { get; set; } = "";
  }
}