using System.Diagnostics;
using System.IO.Compression;

namespace TerrariaModArchive.Scraper.Steam;

public class SteamCmdSetupService
{
    private readonly ILogger<SteamCmdSetupService> _logger;
    private readonly string _steamCmdDir;
    private readonly string _steamCmdExe;

    public SteamCmdSetupService(ILogger<SteamCmdSetupService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _steamCmdDir = configuration["Steam:CmdPath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "steamcmd");

        if (OperatingSystem.IsWindows())
        {
            _steamCmdExe = Path.Combine(_steamCmdDir, "steamcmd.exe");
        }
        else
        {
            _steamCmdExe = Path.Combine(_steamCmdDir, "steamcmd.sh");
        }
    }

    public string GetExecutablePath() => _steamCmdExe;

    public async Task EnsureInstalledAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(_steamCmdExe))
        {
            _logger.LogInformation("SteamCMD found at {Path}", _steamCmdExe);
            return;
        }

        _logger.LogInformation("SteamCMD not found. Downloading and extracting to {Path}...", _steamCmdDir);

        if (!Directory.Exists(_steamCmdDir))
        {
            Directory.CreateDirectory(_steamCmdDir);
        }

        using var httpClient = new HttpClient();

        if (OperatingSystem.IsWindows())
        {
            await SetupWindowsAsync(httpClient, cancellationToken);
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            await SetupLinuxMacAsync(httpClient, cancellationToken);
        }
        else
        {
            throw new PlatformNotSupportedException("Unsupported operating system for SteamCMD automatic setup.");
        }

        _logger.LogInformation("SteamCMD successfully installed. Running initial update...");
        await RunInitialUpdateAsync(cancellationToken);
    }

    private async Task SetupWindowsAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        var downloadUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";
        var zipPath = Path.Combine(_steamCmdDir, "steamcmd.zip");

        var response = await httpClient.GetAsync(downloadUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using (var fs = new FileStream(zipPath, FileMode.Create))
        {
            await response.Content.CopyToAsync(fs, cancellationToken);
        }

        ZipFile.ExtractToDirectory(zipPath, _steamCmdDir, overwriteFiles: true);
        File.Delete(zipPath);
    }

    private async Task SetupLinuxMacAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        // Linux/macOS typically uses the tar.gz
        var downloadUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz";
        if (OperatingSystem.IsMacOS())
        {
            downloadUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_osx.tar.gz";
        }

        var tarPath = Path.Combine(_steamCmdDir, "steamcmd.tar.gz");

        var response = await httpClient.GetAsync(downloadUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using (var fs = new FileStream(tarPath, FileMode.Create))
        {
            await response.Content.CopyToAsync(fs, cancellationToken);
        }

        // Use bash to extract since tar is natively available on linux/mac
        var processInfo = new ProcessStartInfo
        {
            FileName = "tar",
            Arguments = $"-xzf {tarPath} -C {_steamCmdDir}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process != null)
        {
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync(cancellationToken);
                throw new Exception($"Failed to extract steamcmd: {error}");
            }
        }

        File.Delete(tarPath);
    }

    private async Task RunInitialUpdateAsync(CancellationToken cancellationToken)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = _steamCmdExe,
            Arguments = "+quit",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process != null)
        {
            // Initial update might take some time
            await process.WaitForExitAsync(cancellationToken);
        }
    }
}
