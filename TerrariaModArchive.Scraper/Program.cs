using TerrariaModArchive.Scraper;

using Microsoft.EntityFrameworkCore;
using TerrariaModArchive.Core.Data;
using TerrariaModArchive.Core.Storage;
using TerrariaModArchive.Scraper.Steam;
using TerrariaModArchive.Scraper.Workers;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<ArchiveDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Host=localhost;Database=TerrariaModArchive;Username=postgres;Password=postgres",
    npgsqlOptionsAction: sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 10,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorCodesToAdd: null);
    }));

builder.Services.AddSingleton<IStorageService, LocalDiskStorageService>();
builder.Services.AddSingleton<SteamCmdSetupService>();

builder.Services.AddHostedService<SteamWorkshopScraperWorker>();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ArchiveDbContext>();
    dbContext.Database.Migrate();
}

host.Run();
