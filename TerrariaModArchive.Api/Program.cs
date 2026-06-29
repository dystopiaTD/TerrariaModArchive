using Microsoft.EntityFrameworkCore;
using TerrariaModArchive.Core.Data;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using TerrariaModArchive.Core.Storage;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<ArchiveDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Host=localhost;Database=TerrariaModArchive;Username=postgres;Password=postgres"));

builder.Services.AddSingleton<IStorageService, LocalDiskStorageService>();

// Add Output Caching
builder.Services.AddOutputCache();

// Add Auth
var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured.");
builder.Services.AddAuthentication("Bearer").AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = "TerrariaModArchive",
        ValidAudience = "TerrariaModArchive",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
});
builder.Services.AddAuthorization();

builder.Services.AddOpenApi();

var app = builder.Build();

app.UseOutputCache();

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

app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/token", (string username, string password, IConfiguration config) =>
{
    var validUsername = config["AdminCredentials:Username"];
    var validPassword = config["AdminCredentials:Password"];
    var jwtKeyStr = config["Jwt:Key"];

    if (string.IsNullOrEmpty(validUsername) || string.IsNullOrEmpty(validPassword) || string.IsNullOrEmpty(jwtKeyStr))
    {
        return Results.StatusCode(500);
    }

    if (username == validUsername && password == validPassword)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKeyStr));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "TerrariaModArchive",
            audience: "TerrariaModArchive",
            claims: claims,
            expires: DateTime.Now.AddHours(2),
            signingCredentials: creds
        );

        return Results.Ok(new
        {
            token = new JwtSecurityTokenHandler().WriteToken(token)
        });
    }

    return Results.Unauthorized();
});

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
}).CacheOutput(x => x.Expire(TimeSpan.FromMinutes(5)));

app.MapGet("/mods/{id}", async (int id, ArchiveDbContext db) =>
{
    var mod = await db.Mods
        .Select(m => new { m.Id, m.SteamId, m.Title, m.Description, m.CreatedAt, m.UpdatedAt })
        .FirstOrDefaultAsync(m => m.Id == id);

    return mod is not null ? Results.Ok(mod) : Results.NotFound();
}).CacheOutput(x => x.Expire(TimeSpan.FromMinutes(5)));

app.MapGet("/mods/{id}/versions", async (int id, ArchiveDbContext db) =>
{
    var versions = await db.ModVersions
        .Where(v => v.ModId == id)
        .OrderByDescending(v => v.DownloadedAt)
        .Select(v => new { v.Id, v.Version, v.TModLoaderVersion, v.Side, v.DownloadedAt })
        .ToListAsync();

    return Results.Ok(versions);
}).CacheOutput(x => x.Expire(TimeSpan.FromMinutes(5)));

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
}).RequireAuthorization();

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
}).RequireAuthorization();

app.Run();
