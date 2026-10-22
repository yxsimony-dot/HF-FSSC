using System.IO;
using System.Text;
using System.Text.Json;

namespace FSSCWebLauncher;

internal sealed record LauncherSettings(
    string? ReceiptDirectory,
    string? CompletedDirectory);

internal static class LauncherSettingsStore
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FSSCWebLauncher");

    private static readonly string SettingsPath =
        Path.Combine(SettingsDirectory, "settings.json");

    public static LauncherSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new LauncherSettings(null, null);
            }

            string json = File.ReadAllText(SettingsPath, Encoding.UTF8);
            LauncherSettings? settings =
                JsonSerializer.Deserialize<LauncherSettings>(json);

            return new LauncherSettings(
                NormalizeExistingDirectory(settings?.ReceiptDirectory),
                NormalizeExistingDirectory(settings?.CompletedDirectory));
        }
        catch
        {
            return new LauncherSettings(null, null);
        }
    }

    public static bool Save(
        string? receiptDirectory,
        string? completedDirectory)
    {
        string temporaryPath =
            $"{SettingsPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            string json = JsonSerializer.Serialize(
                new LauncherSettings(
                    NormalizeConfiguredDirectory(receiptDirectory),
                    NormalizeConfiguredDirectory(completedDirectory)),
                new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(
                temporaryPath,
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, SettingsPath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch
            {
                // The selected directory remains usable even if cleanup fails.
            }
        }
    }

    public static bool AreSameDirectory(string first, string second)
    {
        try
        {
            string normalizedFirst = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(first));
            string normalizedSecond = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(second));
            return string.Equals(
                normalizedFirst,
                normalizedSecond,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string? NormalizeExistingDirectory(string? directory)
    {
        string? normalized = NormalizeConfiguredDirectory(directory);
        return normalized is not null && Directory.Exists(normalized)
            ? normalized
            : null;
    }

    private static string? NormalizeConfiguredDirectory(string? directory)
    {
        return string.IsNullOrWhiteSpace(directory)
            ? null
            : Path.GetFullPath(directory);
    }
}
