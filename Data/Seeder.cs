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

        if (!await db.Vouchers.AnyAsync())
        {
            db.Vouchers.AddRange(
                new Voucher { Code = "GIAM50K", Name = "Giảm 50.000đ", MemberNo = "HV001", PointTotal = 50_000, PointLimit = 50_000, QtyUseLimit = 1, ExpireDate = DateTime.Today.AddMonths(1) },
                new Voucher { Code = "DIEM200K", Name = "Điểm voucher 200.000đ", MemberNo = "HV002", PointTotal = 200_000, PointLimit = 50_000, QtyUseLimit = 4, ExpireDate = DateTime.Today.AddMonths(2) });
            await db.SaveChangesAsync();
        }

        if (!await db.VoucherPrograms.AnyAsync())
        {
            var p = new VoucherProgram
            {
                Code = "VOUCHER2026", Name = "Voucher mua xe 2026",
                EffDateStart = DateTime.Today.AddDays(-3), EffDateEnd = DateTime.Today.AddMonths(2),
                ValidityPeriod = 30, QtyDayLimitFDlvDate = 30, FlagAllModel = false,
                PointVoucherAllModel = 0, PointUseLimitAllModel = 0,
                Status = VoucherProgramStatus.Finished, Remark = "Áp dụng theo model xe, phát khi mở thẻ trong 30 ngày kể từ ngày giao xe."
            };
            db.VoucherPrograms.Add(p); await db.SaveChangesAsync();
            db.VoucherProgramDtls.AddRange(
                new VoucherProgramDtl { VoucherProgramId = p.Id, ModelCode = "CITY", PointVoucher = 5_000_000, PointUseLimit = 5_000_000 },
                new VoucherProgramDtl { VoucherProgramId = p.Id, ModelCode = "CRV", PointVoucher = 10_000_000, PointUseLimit = 10_000_000 });
            await db.SaveChangesAsync();
        }

        if (!await db.CarPromotions.AnyAsync())
        {
            var cp = new CarPromotion
            {
                Code = "PRMCN2026", Name = "Khuyến mại mua xe mới 2026", DealerCode = "DLCP01",
                EffDateStart = DateTime.Today.AddDays(-3), EffDateEnd = DateTime.Today.AddMonths(2),
                FlagAllModel = false, PointValAllModel = 0,
                Status = CarPromotionStatus.Finished, Remark = "Áp dụng theo model xe, giá trị khuyến mại trừ vào giá bán."
            };
            db.CarPromotions.Add(cp); await db.SaveChangesAsync();
            db.CarPromotionDtls.AddRange(
                new CarPromotionDtl { CarPromotionId = cp.Id, ModelCode = "CITY", PointVal = 20_000_000 },
                new CarPromotionDtl { CarPromotionId = cp.Id, ModelCode = "CRV", PointVal = 40_000_000 });
            await db.SaveChangesAsync();
        }
    }

    private static async Task MigratePostgresAsync(AppDbContext db)
    {
        if (!db.Database.IsNpgsql()) return;
        var def = TenantContext.DefaultOrgId;
        var tables = new[] { "Campaigns", "Prizes", "Entries", "Vouchers", "VoucherRedemptions", "VoucherPrograms", "VoucherProgramDtls", "CarPromotions", "CarPromotionDtls" };
        var sql = new List<string> {
            "CREATE TABLE IF NOT EXISTS minipromo.\"Orgs\" (\"Id\" uuid PRIMARY KEY, \"Name\" text NOT NULL DEFAULT '', \"ApiKey\" text NOT NULL DEFAULT '', \"CreatedAt\" timestamp NOT NULL DEFAULT now())",
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Orgs_ApiKey\" ON minipromo.\"Orgs\" (\"ApiKey\")" };
        foreach (var t in tables) sql.Add($"ALTER TABLE minipromo.\"{t}\" ADD COLUMN IF NOT EXISTS \"OrgId\" uuid NOT NULL DEFAULT '{def}'");
        foreach (var s in sql) try { await db.Database.ExecuteSqlRawAsync(s); } catch { }
    }
}
