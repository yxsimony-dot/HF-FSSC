using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace FSSCWebLauncher;

internal sealed record AvailableUpdate(
    string Version,
    string Commit,
    DateTimeOffset PublishedAt,
    Uri DownloadUri,
    long PackageSize,
    string Sha256);

internal sealed record DownloadedUpdate(
    AvailableUpdate Update,
    string WorkingDirectory,
    string ContentDirectory);

internal sealed class GitHubUpdateService
{
    private const string LatestManifestUrl =
        "https://github.com/yxsimony-dot/HF-FSSC/releases/latest/download/update-manifest.json";
    private const string LatestPackageUrl =
        "https://github.com/yxsimony-dot/HF-FSSC/releases/latest/download/FSSCWebLauncher-win-x64.zip";
    private const string PackageAssetName = "FSSCWebLauncher-win-x64.zip";
    private const long MaximumPackageSize = 750L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly HttpClient HttpClient = CreateHttpClient();

    public async Task<AvailableUpdate?> CheckForUpdateAsync(
        CancellationToken cancellationToken)
    {
        UpdateManifest manifest = await DownloadManifestAsync(
            new Uri(LatestManifestUrl),
            cancellationToken);
        ValidateManifest(manifest);

        if (manifest.PackageSize <= 0
            || manifest.PackageSize > MaximumPackageSize)
        {
            throw new InvalidDataException("更新包大小无效。");
        }

        if (!IsNewerThanCurrentBuild(manifest))
        {
            return null;
        }

        return new AvailableUpdate(
            manifest.Version,
            manifest.Commit,
            manifest.PublishedAt,
            new Uri(LatestPackageUrl),
            manifest.PackageSize,
            manifest.Sha256);
    }

    public async Task<DownloadedUpdate> DownloadUpdateAsync(
        AvailableUpdate update,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        string updateRoot = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "FSSCWebLauncher",
            "Updates");
        Directory.CreateDirectory(updateRoot);
        CleanupOldDownloads(updateRoot);

        string workingDirectory = Path.Combine(
            updateRoot,
            Guid.NewGuid().ToString("N"));
        string packagePath =
            Path.Combine(workingDirectory, PackageAssetName);
        string contentDirectory =
            Path.Combine(workingDirectory, "content");

        Directory.CreateDirectory(workingDirectory);

        try
        {
            using HttpResponseMessage response = await HttpClient.GetAsync(
                update.DownloadUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            long expectedSize =
                response.Content.Headers.ContentLength
                ?? update.PackageSize;
            if (expectedSize <= 0 || expectedSize > MaximumPackageSize)
            {
                throw new InvalidDataException("更新包大小无效。");
            }

            await using Stream source =
                await response.Content.ReadAsStreamAsync(cancellationToken);
            await using FileStream destination = new(
                packagePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            byte[] buffer = new byte[81920];
            long totalBytes = 0;
            int lastPercentage = -1;

            while (true)
            {
                int bytesRead = await source.ReadAsync(
                    buffer,
                    cancellationToken);
                if (bytesRead == 0)
                {
                    break;
                }

                await destination.WriteAsync(
                    buffer.AsMemory(0, bytesRead),
                    cancellationToken);
                totalBytes += bytesRead;

                int percentage = (int)Math.Min(
                    100,
                    totalBytes * 100 / expectedSize);
                if (percentage != lastPercentage)
                {
                    progress?.Report(percentage);
                    lastPercentage = percentage;
                }
            }

            await destination.FlushAsync(cancellationToken);

            if (update.PackageSize > 0
                && totalBytes != update.PackageSize)
            {
                throw new InvalidDataException("更新包下载不完整。");
            }

            string actualHash = await ComputeSha256Async(
                packagePath,
                cancellationToken);
            if (!string.Equals(
                    actualHash,
                    update.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "更新包校验失败，文件可能未完整下载。");
            }

            ExtractPackage(packagePath, contentDirectory);
            string expectedExecutable = Path.Combine(
                contentDirectory,
                "FSSCWebLauncher.exe");
            if (!File.Exists(expectedExecutable))
            {
                throw new InvalidDataException(
                    "更新包中缺少 FSSCWebLauncher.exe。");
            }

            progress?.Report(100);
            return new DownloadedUpdate(
                update,
                workingDirectory,
                contentDirectory);
        }
        catch
        {
            TryDeleteDirectory(workingDirectory);
            throw;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        HttpClient client = new()
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "FSSCWebLauncher-Updater/1.0");
        return client;
    }

    private static async Task<UpdateManifest> DownloadManifestAsync(
        Uri downloadUri,
        CancellationToken cancellationToken)
    {
        ValidateDownloadUri(downloadUri);

        using HttpResponseMessage response = await HttpClient.GetAsync(
            downloadUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidDataException(
                "GitHub 尚未生成可用的更新版本，请稍后重试。");
        }

        response.EnsureSuccessStatusCode();

        await using Stream stream =
            await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<UpdateManifest>(
                stream,
                JsonOptions,
                cancellationToken)
            ?? throw new InvalidDataException("更新清单内容无效。");
    }

    private static void ValidateManifest(UpdateManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Version)
            || string.IsNullOrWhiteSpace(manifest.Commit)
            || manifest.Commit.Length != 40
            || !manifest.Commit.All(char.IsAsciiHexDigit)
            || !string.Equals(
                manifest.AssetName,
                PackageAssetName,
                StringComparison.Ordinal)
            || manifest.PackageSize <= 0
            || manifest.PackageSize > MaximumPackageSize
            || manifest.PublishedAt == default
            || manifest.Sha256.Length != 64
            || !manifest.Sha256.All(char.IsAsciiHexDigit))
        {
            throw new InvalidDataException("更新清单内容无效。");
        }
    }

    private static bool IsNewerThanCurrentBuild(
        UpdateManifest manifest)
    {
        ApplicationBuildInfo current = ApplicationVersion.Current;
        if (!string.IsNullOrWhiteSpace(current.Commit)
            && string.Equals(
                current.Commit,
                manifest.Commit,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Version.TryParse(
                NormalizeVersion(current.Version),
                out Version? currentVersion)
            && Version.TryParse(
                NormalizeVersion(manifest.Version),
                out Version? latestVersion))
        {
            return latestVersion > currentVersion;
        }

        return true;
    }

    private static string NormalizeVersion(string version)
    {
        return version.Trim().TrimStart('v', 'V');
    }

    private static void ValidateDownloadUri(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(
                uri.Host,
                "github.com",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("更新下载地址无效。");
        }
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(
            stream,
            cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void ExtractPackage(
        string packagePath,
        string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        string destinationRoot =
            Path.GetFullPath(destinationDirectory)
            + Path.DirectorySeparatorChar;

        using ZipArchive archive = ZipFile.OpenRead(packagePath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string destinationPath = Path.GetFullPath(
                Path.Combine(
                    destinationDirectory,
                    entry.FullName.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));

            if (!destinationPath.StartsWith(
                    destinationRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "更新包包含不安全的文件路径。");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            string? parentDirectory =
                Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(parentDirectory))
            {
                Directory.CreateDirectory(parentDirectory);
            }

            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static void CleanupOldDownloads(string updateRoot)
    {
        try
        {
            DateTime cutoff = DateTime.UtcNow.AddDays(-7);
            foreach (string directory in Directory.EnumerateDirectories(
                         updateRoot))
            {
                if (Directory.GetLastWriteTimeUtc(directory) < cutoff)
                {
                    TryDeleteDirectory(directory);
                }
            }
        }
        catch
        {
            // Old update files do not block a new update.
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // The next cleanup pass can remove files still in use.
        }
    }

    private sealed class UpdateManifest
    {
        public string Version { get; init; } = string.Empty;

        public string Commit { get; init; } = string.Empty;

        public DateTimeOffset PublishedAt { get; init; }

        public string AssetName { get; init; } = string.Empty;

        public long PackageSize { get; init; }

        public string Sha256 { get; init; } = string.Empty;
    }
}
