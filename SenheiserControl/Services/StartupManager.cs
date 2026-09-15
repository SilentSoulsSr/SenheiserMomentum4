using Microsoft.Win32;

namespace SenheiserControl.Services;

/// <summary>Registers/unregisters the app in HKCU's Run key so it launches at Windows login.</summary>
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SenheiserMomentum4Control";

    private static string ExePath => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) as string == ExePath;
        }
    }

    public static void Enable()
    {
        using RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(ValueName, ExePath);
    }

    public static void Disable()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
