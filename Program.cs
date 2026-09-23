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
builder.Services.AddScoped<IVoucherService, VoucherService>();
builder.Services.AddScoped<IVoucherProgramService, VoucherProgramService>();
builder.Services.AddScoped<ICarPromotionService, CarPromotionService>();
builder.Services.AddScoped<IPromotionProgramService, PromotionProgramService>();
builder.Services.AddScoped<ICarRecommendService, CarRecommendService>();
builder.Services.AddScoped<ICardPromotionProgramService, CardPromotionProgramService>();
builder.Services.AddScoped<IBirthdayPolicyService, BirthdayPolicyService>();
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

// Người tiêu dùng dùng mã giảm giá / điểm voucher (công khai, xuyên tenant qua mã voucher).
app.MapPost("/api/voucher/redeem", async (RedeemDto dto, IVoucherService svc) =>
{
    var r = await svc.RedeemAsync(dto.Code ?? "", dto.Amount, dto.MemberNo);
    return Results.Ok(new { ok = r.ok, msg = r.msg, pointUsed = r.pointUsed, pointRemain = r.pointRemain, qtyUseRemain = r.qtyUseRemain });
});

// Tính giá trị voucher cho một xe theo chương trình đang hiệu lực (công khai).
app.MapPost("/api/voucher-program/calc", async (VoucherCalcDto dto, IVoucherProgramService svc) =>
{
    var r = await svc.CalcAsync(dto.ModelCode ?? "", dto.DeliveryDate, dto.RegistrationDate);
    return Results.Ok(new { ok = r.ok, msg = r.msg, pointVoucher = r.pointVoucher, pointUseLimit = r.pointUseLimit, modelCode = r.modelCode });
});

// Tính giá trị khuyến mại mua xe mới cho một model theo chương trình đang hiệu lực (công khai).
app.MapPost("/api/car-promotion/calc", async (CarPromoCalcDto dto, ICarPromotionService svc) =>
{
    var r = await svc.CalcAsync(dto.DealerCode ?? "", dto.ModelCode ?? "");
    return Results.Ok(new { ok = r.ok, msg = r.msg, pointVal = r.pointVal, modelCode = r.modelCode });
});

// Tính khuyến mại cho một đơn hàng theo chương trình khuyến mại chung đang hiệu lực (công khai).
app.MapPost("/api/promotion/calc", async (PromotionCalcDto dto, IPromotionProgramService svc) =>
{
    var r = await svc.CalcAsync(dto.OrderAmount, dto.Qty, dto.At);
    return Results.Ok(new { ok = r.ok, msg = r.msg, productDiscount = r.productDiscount, orderDiscount = r.orderDiscount, totalDiscount = r.totalDiscount, programCode = r.programCode });
});

// Tính giá trị thưởng giới thiệu xe cho một model theo chương trình đang hiệu lực (công khai).
app.MapPost("/api/car-recommend/calc", async (CarRecommendCalcDto dto, ICarRecommendService svc) =>
{
    var r = await svc.CalcAsync(dto.DealerCode ?? "", dto.ModelCode ?? "");
    return Results.Ok(new { ok = r.ok, msg = r.msg, pointVal = r.pointVal, modelCode = r.modelCode });
});

// Kiểm tra một giao dịch có được dùng ưu đãi của chương trình khuyến mại theo loại thẻ (công khai).
app.MapPost("/api/card-promotion/check", async (CardPromotionUseDto dto, ICardPromotionProgramService svc) =>
{
    var r = await svc.CheckUseAsync(dto.DealerCode ?? "", dto.CardType ?? "", dto.Qty);
    return Results.Ok(new { ok = r.ok, msg = r.msg, qtyRemain = r.qtyRemain });
});

// Ghi nhận sử dụng ưu đãi của chương trình khuyến mại theo loại thẻ cho một giao dịch (công khai).
app.MapPost("/api/card-promotion/use", async (CardPromotionUseDto dto, ICardPromotionProgramService svc) =>
{
    var r = await svc.UseAsync(dto.DealNo ?? "", dto.DealerCode ?? "", dto.CardNo ?? "", dto.CardType ?? "", dto.Qty);
    return Results.Ok(new { ok = r.ok, msg = r.msg, qtyRemain = r.qtyRemain, qtyUsed = r.qtyUsed });
});

// Kiểm tra một hội viên có đủ điều kiện nhận điểm sinh nhật (công khai).
app.MapPost("/api/birthday-policy/check", async (BirthdayCheckDto dto, IBirthdayPolicyService svc) =>
{
    var r = await svc.CheckEligibilityAsync(dto.MemberNo ?? "", dto.CardType ?? "", dto.DateOfBirth, dto.At);
    return Results.Ok(new { ok = r.ok, msg = r.msg, point = r.point, amount = r.amount, cardType = r.cardType });
});

// Tặng điểm sinh nhật cho một hội viên theo chương trình đang hiệu lực (công khai).
app.MapPost("/api/birthday-policy/grant", async (BirthdayGrantDto dto, IBirthdayPolicyService svc) =>
{
    var r = await svc.GrantAsync(dto.MemberNo ?? "", dto.CardNo ?? "", dto.CardType ?? "", dto.DealerCode ?? "", dto.DateOfBirth, dto.At);
    return Results.Ok(new { ok = r.ok, msg = r.msg, point = r.point, amount = r.amount, cardType = r.cardType });
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
record RedeemDto(string? Code, decimal? Amount, string? MemberNo);
record VoucherCalcDto(string? ModelCode, DateTime? DeliveryDate, DateTime? RegistrationDate);
record CarPromoCalcDto(string? DealerCode, string? ModelCode);
record PromotionCalcDto(decimal OrderAmount, int Qty, DateTime? At);
record CarRecommendCalcDto(string? DealerCode, string? ModelCode);
record CardPromotionUseDto(string? DealNo, string? DealerCode, string? CardNo, string? CardType, int Qty);
record BirthdayCheckDto(string? MemberNo, string? CardType, DateTime? DateOfBirth, DateTime? At);
record BirthdayGrantDto(string? MemberNo, string? CardNo, string? CardType, string? DealerCode, DateTime? DateOfBirth, DateTime? At);
record RegisterOrgDto(string Name);
record ImportCampaignDto(string? Code, string? Name, string? Description, DateTime? FromDate, DateTime? ToDate, int? Status, int LoseWeight, List<ImportPrizeDto>? Prizes, List<ImportEntryDto>? Entries);
record ImportPrizeDto(string? Name, string? Tier, decimal Value, int Quantity, int Weight);
record ImportEntryDto(string? Code, string? CustomerName, string? Phone, bool Win);
