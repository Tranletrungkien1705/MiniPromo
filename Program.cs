using Microsoft.EntityFrameworkCore;
using MiniPromo.Data;
using MiniPromo.Models;
using MiniPromo.Services;
using Serilog;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
FleetObs.ConfigureLogger("minipromo");

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();
builder.WebHost.UseUrls($"http://0.0.0.0:{Environment.GetEnvironmentVariable("PORT") ?? "8080"}");

var conn = Environment.GetEnvironmentVariable("CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=minipromo.db";
builder.Services.AddDbContext<AppDbContext>(o =>
{
    if (DbUtil.IsPostgres(conn)) o.UseNpgsql(DbUtil.ToNpgsql(conn));
    else o.UseSqlite(conn);
});
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<IPromoService, PromoService>();
builder.Services.AddFleetObs();
builder.Services.AddControllersWithViews();

var app = builder.Build();
using (var scope = app.Services.CreateScope())
    await Seeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());

app.UseFleetObs();
FleetObs.ReportLicense(Environment.GetEnvironmentVariable("SSO_AUTHORITY") ?? "https://minisso.onrender.com", "minipromo");

app.Use(async (ctx, next) =>
{
    var key = ctx.Request.Headers["X-Api-Key"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(key)) ctx.Request.Cookies.TryGetValue(TenantContext.CookieName, out key);
    if (!string.IsNullOrWhiteSpace(key))
    {
        using var lookup = app.Services.CreateScope();
        var ldb = lookup.ServiceProvider.GetRequiredService<AppDbContext>();
        var org = await ldb.Orgs.FirstOrDefaultAsync(o => o.ApiKey == key);
        if (org != null) ctx.RequestServices.GetRequiredService<ITenantContext>().OrgId = org.Id;
    }
    await next();
});

app.UseStaticFiles();
app.MapGet("/healthz", () => "ok");
app.MapGet("/api/summary", async (IPromoService svc) =>
{
    var d = await svc.DashboardAsync();
    return Results.Ok(new { campaigns = d.Campaigns, running = d.Running, plays = d.TotalPlays, wins = d.TotalWins, valueAwarded = d.ValueAwarded });
});

// Người tiêu dùng quét mã tem → quay số (công khai, xuyên tenant qua mã chiến dịch).
app.MapPost("/api/play", async (PlayDto dto, IPromoService svc) =>
{
    var r = await svc.PlayAsync(dto.CampaignCode ?? "", dto.Code ?? "", dto.Name, dto.Phone);
    return Results.Ok(new { ok = r.ok, msg = r.msg, win = r.win, prize = r.prizeName, value = r.prizeValue });
});

app.MapPost("/api/orgs/register", async (RegisterOrgDto dto, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(dto.Name)) return Results.BadRequest(new { error = "Cần Name." });
    var org = new Org { Name = dto.Name.Trim(), ApiKey = "promo_" + Guid.NewGuid().ToString("N") };
    db.Orgs.Add(org); await db.SaveChangesAsync();
    return Results.Ok(new { orgId = org.Id, apiKey = org.ApiKey });
});

// Import chiến dịch khuyến mãi thật từ HTC (dedupe theo Code) — kèm prizes + entries
app.MapPost("/api/import/campaigns", async (List<ImportCampaignDto> rows, AppDbContext db, ITenantContext tc) =>
{
    if (rows == null || rows.Count == 0) return Results.BadRequest(new { error = "Không có dữ liệu." });
    int added = 0, skipped = 0;
    var orgId = tc.OrgId;
    foreach (var row in rows)
    {
        if (string.IsNullOrWhiteSpace(row.Code)) { skipped++; continue; }
        if (await db.Campaigns.AnyAsync(c => c.OrgId == orgId && c.Code == row.Code.Trim())) { skipped++; continue; }
        var camp = new Campaign
        {
            OrgId = orgId, Code = row.Code.Trim(), Name = row.Name ?? row.Code.Trim(),
            Description = row.Description,
            FromDate = row.FromDate ?? DateTime.Today.AddDays(-30),
            ToDate = row.ToDate ?? DateTime.Today.AddDays(30),
            Status = (CampaignStatus)(row.Status ?? 0),
            LoseWeight = row.LoseWeight > 0 ? row.LoseWeight : 70
        };
        db.Campaigns.Add(camp);
        await db.SaveChangesAsync();
        if (row.Prizes != null)
            foreach (var p in row.Prizes)
                db.Prizes.Add(new Prize { OrgId = orgId, CampaignId = camp.Id, Name = p.Name ?? "", Tier = p.Tier ?? "Giải", Value = p.Value, Quantity = p.Quantity > 0 ? p.Quantity : 1, Weight = p.Weight > 0 ? p.Weight : 5 });
        if (row.Entries != null)
            foreach (var e in row.Entries)
                db.Entries.Add(new Entry { OrgId = orgId, CampaignId = camp.Id, Code = e.Code ?? Guid.NewGuid().ToString("N")[..12], CustomerName = e.CustomerName, Phone = e.Phone, Result = e.Win ? PlayResult.Win : PlayResult.Lose, PrizeName = e.Win ? (row.Prizes?.FirstOrDefault()?.Name) : null });
        await db.SaveChangesAsync();
        added++;
    }
    return Results.Ok(new { added, skipped, total = added + skipped });
});

app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
app.Run();

record PlayDto(string? CampaignCode, string? Code, string? Name, string? Phone);
record RegisterOrgDto(string Name);
record ImportCampaignDto(string? Code, string? Name, string? Description, DateTime? FromDate, DateTime? ToDate, int? Status, int LoseWeight, List<ImportPrizeDto>? Prizes, List<ImportEntryDto>? Entries);
record ImportPrizeDto(string? Name, string? Tier, decimal Value, int Quantity, int Weight);
record ImportEntryDto(string? Code, string? CustomerName, string? Phone, bool Win);
