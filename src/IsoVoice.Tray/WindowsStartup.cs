using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace IsoVoice.Tray
{
    internal static class WindowsStartup
    {
        private static readonly string RegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private static readonly string AppName = "IsoVoice";
        
        public static bool IsRegistered()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, false);
                return key?.GetValue(AppName) != null;
            }
            catch (Exception)
            {
                return false;
            }
        }
        
        public static void Register()
        {
            try
            {
                string exePath = System.Windows.Forms.Application.ExecutablePath;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    Debug.WriteLine("[IsoVoice] Auto-start registration failed: Executable path invalid: " + exePath);
                    return;
                }

                Debug.WriteLine("[IsoVoice] Attempting to create registry key: " + RegistryKey);
                using var key = Registry.CurrentUser.CreateSubKey(RegistryKey);
                Debug.WriteLine("[IsoVoice] Registry key created/opened successfully.");

                Debug.WriteLine("[IsoVoice] Setting registry value: " + AppName + " = \"" + exePath + "\"");
                key.SetValue(AppName, "\"" + exePath + "\"", RegistryValueKind.String);
                Debug.WriteLine("[IsoVoice] Successfully set registry value.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[IsoVoice] Auto-start registration exception: " + ex.Message);
                Debug.WriteLine("[IsoVoice] StackTrace: " + ex.StackTrace);
            }
        }
        
        public static void Unregister()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, true);
                key?.DeleteValue(AppName, false);
            }
            catch (Exception)
            {
                // Not critical
            }
        }
    }
}
