using Microsoft.Win32;

namespace Devlight;

internal sealed class StartupRegistration
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue("Devlight") as string;
    }

    internal void Set(bool enabled) => Restore(enabled ? $"\"{Environment.ProcessPath ?? throw new IOException("Executable path unavailable.")}\"" : null);

    internal void Restore(string? command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, true);
        if (command is null) key.DeleteValue("Devlight", false);
        else key.SetValue("Devlight", command, RegistryValueKind.String);
    }
}
