using Microsoft.EntityFrameworkCore;
using MiniPromo.Models;
namespace MiniPromo.Data;

public static class Seeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        await db.Database.EnsureCreatedAsync();
        await MigratePostgresAsync(db);
        if (!await db.Orgs.AnyAsync(o => o.Id == TenantContext.DefaultOrgId))
        { db.Orgs.Add(new Org { Id = TenantContext.DefaultOrgId, Name = "Demo Khuyến mãi", ApiKey = TenantContext.DefaultApiKey }); await db.SaveChangesAsync(); }

        if (!await db.Campaigns.AnyAsync())
        {
            var c = new Campaign
            {
                Code = "TET2026", Name = "Lì xì Tết 2026", Description = "Quét mã trên tem sản phẩm để quay số trúng thưởng.",
                FromDate = DateTime.Today.AddDays(-3), ToDate = DateTime.Today.AddMonths(1),
                Status = CampaignStatus.Running, LoseWeight = 60
            };
            db.Campaigns.Add(c); await db.SaveChangesAsync();
            db.Prizes.AddRange(
                new Prize { CampaignId = c.Id, Tier = "Giải Nhất", Name = "Xe máy Honda", Value = 30_000_000, Quantity = 1, Weight = 1 },
                new Prize { CampaignId = c.Id, Tier = "Giải Nhì", Name = "Điện thoại", Value = 8_000_000, Quantity = 5, Weight = 5 },
                new Prize { CampaignId = c.Id, Tier = "Giải Ba", Name = "Voucher 200K", Value = 200_000, Quantity = 100, Weight = 40 });
            await db.SaveChangesAsync();
        }
    }

    private static async Task MigratePostgresAsync(AppDbContext db)
    {
        if (!db.Database.IsNpgsql()) return;
        var def = TenantContext.DefaultOrgId;
        var tables = new[] { "Campaigns", "Prizes", "Entries" };
        var sql = new List<string> {
            "CREATE TABLE IF NOT EXISTS minipromo.\"Orgs\" (\"Id\" uuid PRIMARY KEY, \"Name\" text NOT NULL DEFAULT '', \"ApiKey\" text NOT NULL DEFAULT '', \"CreatedAt\" timestamp NOT NULL DEFAULT now())",
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Orgs_ApiKey\" ON minipromo.\"Orgs\" (\"ApiKey\")" };
        foreach (var t in tables) sql.Add($"ALTER TABLE minipromo.\"{t}\" ADD COLUMN IF NOT EXISTS \"OrgId\" uuid NOT NULL DEFAULT '{def}'");
        foreach (var s in sql) try { await db.Database.ExecuteSqlRawAsync(s); } catch { }
    }
}
