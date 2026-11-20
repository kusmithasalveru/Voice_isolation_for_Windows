using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace IsoVoice.Tray
{
    internal static class SettingsManager
    {
        private static readonly string SettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IsoVoice");
        
        private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");
        
        public static AppSettings Load()
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                
                if (File.Exists(SettingsFile))
                {
                    try
                    {
                        string json = File.ReadAllText(SettingsFile);
                        var node = JsonNode.Parse(json);
                        if (node != null)
                        {
                            return new AppSettings
                            {
                                IsEnabled = node["IsEnabled"]?.GetValue<bool>() ?? false,
                                CaptureDeviceId = node["CaptureDeviceId"]?.GetValue<string>() ?? "",
                                RenderDeviceId = node["RenderDeviceId"]?.GetValue<string>() ?? "",
                                BufferMs = node["BufferMs"]?.GetValue<int>() ?? 10,
                                AutoStart = node["AutoStart"]?.GetValue<bool>() ?? false
                            };
                        }
                    }
                    catch (Exception ex) when (ex is JsonException || ex is InvalidDataException)
                    {
                        // Corrupted JSON - fall back to defaults
                        File.Delete(SettingsFile);
                    }
                }
            }
            catch (Exception)
            {
                // Can't access settings directory - use defaults
            }
            
            return new AppSettings();
        }
        
        public static void Save(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                
                var obj = new JsonObject
                {
                    ["IsEnabled"] = settings.IsEnabled,
                    ["CaptureDeviceId"] = settings.CaptureDeviceId ?? "",
                    ["RenderDeviceId"] = settings.RenderDeviceId ?? "",
                    ["BufferMs"] = settings.BufferMs,
                    ["AutoStart"] = settings.AutoStart
                };
                
                string json = obj.ToJsonString(new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
                });
                
                File.WriteAllText(SettingsFile, json, new System.Text.UTF8Encoding(false));
            }
            catch (Exception)
            {
                // Silently fail - settings are not critical
            }
        }
        
        public static void Delete()
        {
            try
            {
                if (File.Exists(SettingsFile))
                    File.Delete(SettingsFile);
            }
            catch (Exception) { /* Ignore */ }
        }
    }
    
    public class AppSettings
    {
        public bool IsEnabled { get; set; }
        public string CaptureDeviceId { get; set; } = "";
        public string RenderDeviceId { get; set; } = "";
        public int BufferMs { get; set; } = 10;
        public bool AutoStart { get; set; }
    }
}
