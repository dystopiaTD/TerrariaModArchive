using Microsoft.EntityFrameworkCore;
using TerrariaModArchive.Core.Data;

using Microsoft.AspNetCore.Mvc;
using TerrariaModArchive.Core.Storage;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<ArchiveDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Host=localhost;Database=TerrariaModArchive;Username=postgres;Password=postgres"));

builder.Services.AddSingleton<IStorageService, LocalDiskStorageService>();

builder.Services.AddOpenApi();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ArchiveDbContext>();
    dbContext.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/mods", async ([FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, ArchiveDbContext db) =>
{
    var p = (page ?? 1) <= 0 ? 1 : (page ?? 1);
    var ps = (pageSize ?? 20) <= 0 ? 20 : (pageSize ?? 20);

    var query = db.Mods.AsQueryable();

    if (!string.IsNullOrWhiteSpace(search))
    {
        query = query.Where(m => m.Title.ToLower().Contains(search.ToLower()) ||
                                 m.SteamId.Contains(search));
    }

    var totalItems = await query.CountAsync();
    var mods = await query
        .OrderByDescending(m => m.UpdatedAt)
        .Skip((p - 1) * ps)
        .Take(ps)
        .Select(m => new { m.Id, m.SteamId, m.Title, m.Description, m.UpdatedAt })
        .ToListAsync();

    return Results.Ok(new { TotalItems = totalItems, Page = p, PageSize = ps, Items = mods });
});

app.MapGet("/mods/{id}", async (int id, ArchiveDbContext db) =>
{
    var mod = await db.Mods
        .Select(m => new { m.Id, m.SteamId, m.Title, m.Description, m.CreatedAt, m.UpdatedAt })
        .FirstOrDefaultAsync(m => m.Id == id);

    return mod is not null ? Results.Ok(mod) : Results.NotFound();
});

app.MapGet("/mods/{id}/versions", async (int id, ArchiveDbContext db) =>
{
    var versions = await db.ModVersions
        .Where(v => v.ModId == id)
        .OrderByDescending(v => v.DownloadedAt)
        .Select(v => new { v.Id, v.Version, v.TModLoaderVersion, v.Side, v.DownloadedAt })
        .ToListAsync();

    return Results.Ok(versions);
});

app.MapGet("/mods/{id}/versions/latest/download", async (int id, ArchiveDbContext db, IStorageService storage) =>
{
    var latestVersion = await db.ModVersions
        .Where(v => v.ModId == id)
        .OrderByDescending(v => v.DownloadedAt)
        .FirstOrDefaultAsync();

    if (latestVersion == null)
        return Results.NotFound("No versions found for this mod.");

    var stream = await storage.GetFileAsync(latestVersion.FilePath);
    if (stream == null)
        return Results.NotFound("File not found on storage.");

    return Results.File(stream, "application/octet-stream", $"{id}_{latestVersion.Version}.tmod");
});

app.MapGet("/mods/{id}/versions/{version}/download", async (int id, string version, ArchiveDbContext db, IStorageService storage) =>
{
    var modVersion = await db.ModVersions
        .FirstOrDefaultAsync(v => v.ModId == id && v.Version == version);

    if (modVersion == null)
        return Results.NotFound("Version not found.");

    var stream = await storage.GetFileAsync(modVersion.FilePath);
    if (stream == null)
        return Results.NotFound("File not found on storage.");

    return Results.File(stream, "application/octet-stream", $"{id}_{version}.tmod");
});

app.Run();
