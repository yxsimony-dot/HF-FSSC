using System.Reflection;

namespace FSSCWebLauncher;

internal sealed record ApplicationBuildInfo(
    string Version,
    string? Commit);

internal static class ApplicationVersion
{
    public static ApplicationBuildInfo Current { get; } = Create();

    private static ApplicationBuildInfo Create()
    {
        Assembly assembly = typeof(ApplicationVersion).Assembly;
        string informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";

        string version = informationalVersion.Split('+', 2)[0];
        string? commit = ExtractCommit(informationalVersion);
        return new ApplicationBuildInfo(version, commit);
    }

    private static string? ExtractCommit(string informationalVersion)
    {
        int metadataSeparator = informationalVersion.IndexOf('+');
        if (metadataSeparator < 0
            || metadataSeparator == informationalVersion.Length - 1)
        {
            return null;
        }

        string metadata = informationalVersion[(metadataSeparator + 1)..];
        return metadata
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(IsGitCommit);
    }

    private static bool IsGitCommit(string value)
    {
        return value.Length is >= 7 and <= 64
            && value.All(char.IsAsciiHexDigit);
    }
}
