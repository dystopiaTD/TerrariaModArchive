using System.Diagnostics;
using System.Text.Json;
using TerrariaModArchive.Core.Data;
using TerrariaModArchive.Core.Models;
using TerrariaModArchive.Core.Storage;
using TerrariaModArchive.Scraper.Steam;
using TerrariaModArchive.Scraper.TModLoader;

namespace TerrariaModArchive.Scraper.Workers;

public class SteamWorkshopScraperWorker : BackgroundService
{
    private readonly ILogger<SteamWorkshopScraperWorker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly SteamCmdSetupService _steamCmd;
    private readonly string _downloadDir;

    // Terraria AppId is 105600, tModLoader AppId is 1281930
    private const string TModLoaderAppId = "1281930";

    public SteamWorkshopScraperWorker(
        ILogger<SteamWorkshopScraperWorker> logger,
        IServiceProvider serviceProvider,
        SteamCmdSetupService steamCmd)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _steamCmd = steamCmd;
        _downloadDir = Path.Combine(Directory.GetCurrentDirectory(), "temp_downloads");

        if (!Directory.Exists(_downloadDir))
        {
            Directory.CreateDirectory(_downloadDir);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Steam Workshop Scraper Worker starting.");

        // Ensure SteamCMD is ready
        await _steamCmd.EnsureInstalledAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Starting scrape cycle at: {Time}", DateTimeOffset.Now);

            try
            {
                var client = new HttpClient();
                var allModsToProcess = new Dictionary<string, string>(); // SteamId -> Title

                // 1. Get Top 200 items (2 pages of 100)
                for (int page = 0; page < 2; page++)
                {
                    // IPublishedFileService/QueryFiles - query_type=1 (ranked by trend), numperpage=100
                    var queryUrl = $"https://api.steampowered.com/IPublishedFileService/QueryFiles/v1/?appid={TModLoaderAppId}&query_type=1&page={page}&numperpage=100&return_short_description=true";
                    var response = await client.GetAsync(queryUrl, stoppingToken);

                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync(stoppingToken);
                        using var jsonDocument = JsonDocument.Parse(content);

                        var root = jsonDocument.RootElement;
                        if (root.TryGetProperty("response", out var responseElement) &&
                            responseElement.TryGetProperty("publishedfiledetails", out var files))
                        {
                            foreach (var file in files.EnumerateArray())
                            {
                                var workshopId = file.GetProperty("publishedfileid").GetString();
                                var title = file.GetProperty("title").GetString();

                                if (!string.IsNullOrEmpty(workshopId) && !string.IsNullOrEmpty(title))
                                {
                                    allModsToProcess[workshopId] = title;
                                }
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Failed to query Steam Workshop API. Status code: {StatusCode}", response.StatusCode);
                    }
                }

                // 2. Add historically tracked mods from DB
                using (var scope = _serviceProvider.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<ArchiveDbContext>();
                    var trackedMods = dbContext.Mods.Select(m => new { m.SteamId, m.Title }).ToList();

                    foreach (var trackedMod in trackedMods)
                    {
                        if (!allModsToProcess.ContainsKey(trackedMod.SteamId))
                        {
                            allModsToProcess[trackedMod.SteamId] = trackedMod.Title;
                        }
                    }
                }

                _logger.LogInformation("Found {Count} total mods to process (Top 200 + Tracked).", allModsToProcess.Count);

                // 3. Process all mods
                foreach (var modItem in allModsToProcess)
                {
                    await ProcessModAsync(modItem.Key, modItem.Value, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during the scrape cycle.");
            }

            // Wait before next cycle (e.g., 24 hours). For demo, wait 1 hour.
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task ProcessModAsync(string workshopId, string title, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing Mod {WorkshopId} - {Title}", workshopId, title);

        // 1. Download via SteamCMD
        var steamCmdExe = _steamCmd.GetExecutablePath();

        var arguments = $"+login anonymous +workshop_download_item {TModLoaderAppId} {workshopId} +quit";
        var processInfo = new ProcessStartInfo
        {
            FileName = steamCmdExe,
            Arguments = arguments,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process != null)
        {
            await process.WaitForExitAsync(cancellationToken);
        }

        // SteamCMD downloads workshop items to:
        // [steamcmd_dir]/steamapps/workshop/content/[appid]/[workshopid]/
        var steamCmdDir = Path.GetDirectoryName(steamCmdExe)!;
        var workshopContentDir = Path.Combine(steamCmdDir, "steamapps", "workshop", "content", TModLoaderAppId, workshopId);

        if (!Directory.Exists(workshopContentDir))
        {
            _logger.LogWarning("Download failed or directory not found: {Path}", workshopContentDir);
            return;
        }

        // 2. Find .tmod file
        var tmodFile = Directory.GetFiles(workshopContentDir, "*.tmod", SearchOption.AllDirectories).FirstOrDefault();
        if (tmodFile == null)
        {
            _logger.LogWarning("No .tmod file found in {Path}", workshopContentDir);
            return;
        }

        // 3. Parse Metadata
        var metadata = await TModParser.ParseAsync(tmodFile, cancellationToken);

        // Fallbacks if parse fails
        if (string.IsNullOrEmpty(metadata.Version)) metadata.Version = "1.0.0";
        if (string.IsNullOrEmpty(metadata.TModLoaderVersion)) metadata.TModLoaderVersion = "1.4.4";

        // 4. Save to Database and Storage
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ArchiveDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IStorageService>();

        var mod = dbContext.Mods.FirstOrDefault(m => m.SteamId == workshopId);
        if (mod == null)
        {
            mod = new Mod
            {
                SteamId = workshopId,
                Title = title,
                Description = "Auto-imported from Steam Workshop",
            };
            dbContext.Mods.Add(mod);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var versionExists = dbContext.ModVersions.Any(mv => mv.ModId == mod.Id && mv.Version == metadata.Version && mv.TModLoaderVersion == metadata.TModLoaderVersion);

        if (!versionExists)
        {
            var fileName = $"{mod.SteamId}_{metadata.Version}.tmod";

            await using var fs = new FileStream(tmodFile, FileMode.Open, FileAccess.Read);
            var savedPath = await storage.SaveFileAsync(fileName, fs, cancellationToken);

            var modVersion = new ModVersion
            {
                ModId = mod.Id,
                Version = metadata.Version,
                TModLoaderVersion = metadata.TModLoaderVersion,
                Side = metadata.Side,
                FilePath = savedPath
            };

            dbContext.ModVersions.Add(modVersion);
            mod.UpdatedAt = DateTime.UtcNow;

            await dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Saved new version {Version} for {Title}", metadata.Version, title);
        }
        else
        {
            _logger.LogInformation("Version {Version} for {Title} already exists.", metadata.Version, title);
        }

        // 5. Cleanup SteamCMD cache to save disk space
        try
        {
            if (Directory.Exists(workshopContentDir))
            {
                Directory.Delete(workshopContentDir, recursive: true);
                _logger.LogDebug("Cleaned up SteamCMD cache directory: {Path}", workshopContentDir);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up SteamCMD cache directory: {Path}", workshopContentDir);
        }
    }
}
