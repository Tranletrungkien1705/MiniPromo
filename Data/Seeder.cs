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

        if (!await db.PromotionPrograms.AnyAsync())
        {
            var pp = new PromotionProgram
            {
                Code = "PRMKM2026", Name = "Khuyến mại cuối tuần 2026",
                MainType = PromotionMainType.Order, PrmType = PromotionPrmType.Order,
                BudgetVal = 500_000_000,
                EffDTimeStart = DateTime.Today.AddDays(-3), EffDTimeEnd = DateTime.Today.AddMonths(2),
                FlagParallel = false, FlagMulti = false,
                FlagAllMonth = true, FlagAllDay = true, FlagAllDayOfWeek = false, FlagAllTime = true,
                Status = PromotionStatus.Finished, Remark = "Giảm 10% đơn hàng (tối đa 200.000đ) vào Thứ Bảy & Chủ Nhật."
            };
            db.PromotionPrograms.Add(pp); await db.SaveChangesAsync();
            db.PromotionScopes.AddRange(
                new PromotionScope { PromotionProgramId = pp.Id, ScopeType = PromotionScopeType.DayOfWeek, Value = "6" },
                new PromotionScope { PromotionProgramId = pp.Id, ScopeType = PromotionScopeType.DayOfWeek, Value = "0" });
            db.PromotionPrms.Add(new PromotionPrm { PromotionProgramId = pp.Id, Idx = 1, ValOrdRateDc = 10, ValOrdDcMax = 200_000 });
            db.PromotionMains.Add(new PromotionMain { PromotionProgramId = pp.Id, Idx = 1, TotalValOrd = 500_000 });
            db.PromotionProductScopes.AddRange(
                new PromotionProductScope { PromotionProgramId = pp.Id, Kind = PromotionProductScopeKind.Main, Idx = 1, RefType = PromotionRefType.Product, RefCode = "SP001", RefName = "Sản phẩm A" },
                new PromotionProductScope { PromotionProgramId = pp.Id, Kind = PromotionProductScopeKind.Main, Idx = 1, RefType = PromotionRefType.ProductGroup, RefCode = "GRP01", RefName = "Nhóm hàng B" });
            await db.SaveChangesAsync();
        }

        if (!await db.CarRecommends.AnyAsync())
        {
            var cr = new CarRecommend
            {
                Code = "PRMCR2026", Name = "Giới thiệu xe 2026", DealerCode = "DLCP01",
                EffDateStart = DateTime.Today.AddDays(-3), EffDateEnd = DateTime.Today.AddMonths(2),
                FlagAllModel = false, PointValAllModel = 0,
                Status = CarRecommendStatus.Finished, Remark = "Thưởng cho người giới thiệu khách mua xe, áp dụng theo model."
            };
            db.CarRecommends.Add(cr); await db.SaveChangesAsync();
            db.CarRecommendDtls.AddRange(
                new CarRecommendDtl { CarRecommendId = cr.Id, ModelCode = "CITY", PointVal = 3_000_000 },
                new CarRecommendDtl { CarRecommendId = cr.Id, ModelCode = "CRV", PointVal = 5_000_000 });
            await db.SaveChangesAsync();
        }

        if (!await db.CardPromotionPrograms.AnyAsync())
        {
            var cp = new CardPromotionProgram
            {
                Code = "PRMPR2026", Name = "Ưu đãi theo loại thẻ 2026",
                EffDateStart = DateTime.Today.AddDays(-3), EffDateEnd = DateTime.Today.AddMonths(2),
                FlagAllDL = false, Status = CardPromotionProgramStatus.Active,
                Remark = "Cấp số lượng ưu đãi theo loại thẻ, áp dụng cho đại lý chỉ định."
            };
            db.CardPromotionPrograms.Add(cp); await db.SaveChangesAsync();
            db.CardPromotionProgramDtls.AddRange(
                new CardPromotionProgramDtl { CardPromotionProgramId = cp.Id, CardType = "GOLD", Qty = 10, Unit = "Lần" },
                new CardPromotionProgramDtl { CardPromotionProgramId = cp.Id, CardType = "PLATINUM", Qty = 20, Unit = "Lần" });
            db.CardPromotionProgramSpecs.Add(new CardPromotionProgramSpec { CardPromotionProgramId = cp.Id, DealerCode = "DLCP01" });
            await db.SaveChangesAsync();
        }
    }

    private static async Task MigratePostgresAsync(AppDbContext db)
    {
        if (!db.Database.IsNpgsql()) return;
        var def = TenantContext.DefaultOrgId;
        var tables = new[] { "Campaigns", "Prizes", "Entries", "Vouchers", "VoucherRedemptions", "VoucherPrograms", "VoucherProgramDtls", "CarPromotions", "CarPromotionDtls", "PromotionPrograms", "PromotionScopes", "PromotionPrms", "PromotionMains", "PromotionProductScopes", "CarRecommends", "CarRecommendDtls", "CardPromotionPrograms", "CardPromotionProgramDtls", "CardPromotionProgramSpecs", "CardPromotionUsages" };
        var sql = new List<string> {
            "CREATE TABLE IF NOT EXISTS minipromo.\"Orgs\" (\"Id\" uuid PRIMARY KEY, \"Name\" text NOT NULL DEFAULT '', \"ApiKey\" text NOT NULL DEFAULT '', \"CreatedAt\" timestamp NOT NULL DEFAULT now())",
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Orgs_ApiKey\" ON minipromo.\"Orgs\" (\"ApiKey\")" };
        foreach (var t in tables) sql.Add($"ALTER TABLE minipromo.\"{t}\" ADD COLUMN IF NOT EXISTS \"OrgId\" uuid NOT NULL DEFAULT '{def}'");
        foreach (var s in sql) try { await db.Database.ExecuteSqlRawAsync(s); } catch { }
    }
}
