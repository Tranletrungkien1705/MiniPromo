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

        if (!await db.BirthdayPolicies.AnyAsync())
        {
            var bp = new BirthdayPolicy
            {
                Code = "BIRTH2026", Name = "Tặng điểm sinh nhật 2026",
                EffDateStart = DateTime.Today.AddDays(-3), EffDateEnd = DateTime.Today.AddMonths(2),
                FlagPoint = true, ParamValue = 1_000, Status = BirthdayPolicyStatus.Active,
                Remark = "Tặng điểm cho hội viên vào đúng ngày sinh, mỗi hội viên 1 lần/năm; điểm quy đổi ra tiền theo tỷ lệ."
            };
            db.BirthdayPolicies.Add(bp); await db.SaveChangesAsync();
            db.BirthdayPolicyDtls.AddRange(
                new BirthdayPolicyDtl { BirthdayPolicyId = bp.Id, CardType = "GOLD", Point = 500 },
                new BirthdayPolicyDtl { BirthdayPolicyId = bp.Id, CardType = "PLATINUM", Point = 1_000 });
            await db.SaveChangesAsync();
        }

        if (!await db.IssueVouchers.AnyAsync())
        {
            var iv = new IssueVoucher
            {
                Code = "ISSUE2026", Name = "Đợt phát hành voucher Tết 2026",
                EffDateStart = DateTime.Today.AddDays(-3), EffDateEnd = DateTime.Today.AddMonths(2),
                QtyVoucher = 100, QtyDateUse = 30,
                FavorType = IssueFavorType.Discount, IssueForm = IssueFormType.Give,
                FlagConditionUsePrd = false, FlagScopeBranch = false, FlagActive = true,
                Remark = "Phát voucher giảm giá 10% (tối đa 100.000đ) cho khách hàng tại chi nhánh HN01."
            };
            db.IssueVouchers.Add(iv); await db.SaveChangesAsync();
            db.IssueVoucherPrices.Add(new IssueVoucherPrice { IssueVoucherId = iv.Id, IssueType = IssuePriceType.Issue, IssueTypeDtl = "Giảm giá", UPRateDc = 10, UPDcMax = 100_000 });
            db.IssueVoucherScopes.Add(new IssueVoucherScope { IssueVoucherId = iv.Id, ScopeType = IssueScopeType.Branch, Value = "HN01" });
            db.IssueVoucherProducts.Add(new IssueVoucherProduct { IssueVoucherId = iv.Id, RefType = IssueRefType.Product, RefCode = "SP001", RefName = "Sản phẩm A" });
            await db.SaveChangesAsync();
        }

        if (!await db.RankPolicies.AnyAsync())
        {
            db.RankPolicies.AddRange(
                new RankPolicy { Code = "RP-SILVER", CardType = "SILVER", Value = 1, PointUpBegin = 0, PointUpEnd = 4_999, QtyVisitUpBegin = 0, QtyVisitUpEnd = 4, PointKeepBegin = 0, PointKeepEnd = 1_999, QtyVisitKeepBegin = 0, QtyVisitKeepEnd = 2, QtyMonth = 12, Status = RankPolicyStatus.Active, Remark = "Hạng cơ bản — nâng lên GOLD khi đủ 5.000 điểm và 5 lần ghé." },
                new RankPolicy { Code = "RP-GOLD", CardType = "GOLD", Value = 2, PointUpBegin = 5_000, PointUpEnd = 19_999, QtyVisitUpBegin = 5, QtyVisitUpEnd = 19, PointKeepBegin = 2_000, PointKeepEnd = 9_999, QtyVisitKeepBegin = 3, QtyVisitKeepEnd = 9, QtyMonth = 12, Status = RankPolicyStatus.Active, Remark = "Hạng vàng — nâng lên PLATINUM khi đủ 20.000 điểm và 20 lần ghé." },
                new RankPolicy { Code = "RP-PLATINUM", CardType = "PLATINUM", Value = 3, PointUpBegin = 20_000, PointUpEnd = 0, QtyVisitUpBegin = 20, QtyVisitUpEnd = 0, PointKeepBegin = 10_000, PointKeepEnd = 0, QtyVisitKeepBegin = 10, QtyVisitKeepEnd = 0, QtyMonth = 12, Status = RankPolicyStatus.Active, Remark = "Hạng cao nhất — chỉ cần duy trì." });
            await db.SaveChangesAsync();
        }

        if (!await db.PolicyMoneyToPoints.AnyAsync())
        {
            var pmtp = new PolicyMoneyToPoint
            {
                Code = "PMTP2026", Name = "Quy đổi tiền dịch vụ → điểm 2026",
                EffDateStart = DateTime.Today.AddDays(-3), EffDateEnd = DateTime.Today.AddMonths(2),
                Status = PolicyMoneyToPointStatus.Active,
                Remark = "Cứ 1.000đ tiền dịch vụ đổi 1 điểm; hạng cao đổi nhiều điểm hơn và có chiết khấu dịch vụ."
            };
            db.PolicyMoneyToPoints.Add(pmtp); await db.SaveChangesAsync();
            db.PolicyMoneyToPointDtls.AddRange(
                new PolicyMoneyToPointDtl { PolicyMoneyToPointId = pmtp.Id, CardType = "SILVER", ConvertValue = 1_000, ConvertPoint = 1, ValueRankCardType = 500_000, DiscountRate = 0 },
                new PolicyMoneyToPointDtl { PolicyMoneyToPointId = pmtp.Id, CardType = "GOLD", ConvertValue = 1_000, ConvertPoint = 2, ValueRankCardType = 1_000_000, DiscountRate = 5 },
                new PolicyMoneyToPointDtl { PolicyMoneyToPointId = pmtp.Id, CardType = "PLATINUM", ConvertValue = 1_000, ConvertPoint = 3, ValueRankCardType = 2_000_000, DiscountRate = 10 });
            await db.SaveChangesAsync();
        }

        if (!await db.MemberDiscountTransactions.AnyAsync())
        {
            // Giao dịch chiết khấu mẫu: hạng GOLD có tỷ lệ chiết khấu 5% (theo chính sách quy đổi ở trên).
            db.MemberDiscountTransactions.AddRange(
                new MemberDiscountTransaction
                {
                    RefNo = "RO2026001", DealerCode = "DLCP01", MemberNo = "HV001", CardNo = "CARD001",
                    CardTypeUse = "GOLD", CardTypeInit = "GOLD", CardTypeApply = "GOLD",
                    DealPointType = MemberDiscountPointType.DiscountRO, PolicyCode = "PMTP2026",
                    PolicyDiscountRate = 5, AmountForDC = 2_000_000, PointChTotal = 100_000,
                    CreateDate = DateTime.Today, Remark = "Chiết khấu dịch vụ 5% cho hạng GOLD."
                },
                new MemberDiscountTransaction
                {
                    RefNo = "RO2026002", DealerCode = "DLCP01", MemberNo = "HV002", CardNo = "CARD002",
                    CardTypeUse = "PLATINUM", CardTypeInit = "PLATINUM", CardTypeApply = "PLATINUM",
                    DealPointType = MemberDiscountPointType.DiscountRO, PolicyCode = "PMTP2026",
                    PolicyDiscountRate = 10, AmountForDC = 3_000_000, PointChTotal = 300_000,
                    CreateDate = DateTime.Today, Remark = "Chiết khấu dịch vụ 10% cho hạng PLATINUM."
                });
            await db.SaveChangesAsync();
        }

        if (!await db.PromotionMainTypeDefs.AnyAsync())
        {
            // Danh mục "Khuyến mại theo" — theo nguồn Mst_PromotionMainType.
            db.PromotionMainTypeDefs.AddRange(
                new PromotionMainTypeDef { Code = "ORDER", Name = "Đơn hàng", Remark = "Khuyến mại áp dụng cho toàn đơn hàng." },
                new PromotionMainTypeDef { Code = "PRODUCT", Name = "Hàng hóa", Remark = "Khuyến mại áp dụng cho từng mặt hàng." },
                new PromotionMainTypeDef { Code = "PRODUCTANDORDER", Name = "Hàng hóa và đơn hàng", Remark = "Khuyến mại áp dụng cho cả hàng hóa và đơn hàng." });
            await db.SaveChangesAsync();
        }

        if (!await db.PromotionPrmTypeDefs.AnyAsync())
        {
            // Danh mục "Hình thức khuyến mại" — theo nguồn Mst_PromotionPrmType.
            db.PromotionPrmTypeDefs.AddRange(
                new PromotionPrmTypeDef { Code = "ORDER", Name = "Giảm giá đơn hàng" },
                new PromotionPrmTypeDef { Code = "PRODUCT", Name = "Tặng hàng" },
                new PromotionPrmTypeDef { Code = "PRODUCTUPDC", Name = "Giảm giá hàng" },
                new PromotionPrmTypeDef { Code = "PRODUCTUPDCBYQTY", Name = "Giảm giá bán theo SL mua" },
                new PromotionPrmTypeDef { Code = "VOUCHER", Name = "Tặng voucher" });
            await db.SaveChangesAsync();
        }

        if (!await db.PromotionPrmInMains.AnyAsync())
        {
            // Gắn hình thức khuyến mại vào loại khuyến mại theo — theo nguồn Prm_PrmInMain.
            var mains = await db.PromotionMainTypeDefs.ToListAsync();
            var prms = await db.PromotionPrmTypeDefs.ToListAsync();
            int Mid(string c) => mains.First(t => t.Code == c).Id;
            int Pid(string c) => prms.First(t => t.Code == c).Id;
            db.PromotionPrmInMains.AddRange(
                new PromotionPrmInMain { MainTypeId = Mid("ORDER"), PrmTypeId = Pid("ORDER") },
                new PromotionPrmInMain { MainTypeId = Mid("ORDER"), PrmTypeId = Pid("VOUCHER") },
                new PromotionPrmInMain { MainTypeId = Mid("PRODUCT"), PrmTypeId = Pid("PRODUCT") },
                new PromotionPrmInMain { MainTypeId = Mid("PRODUCT"), PrmTypeId = Pid("PRODUCTUPDC") },
                new PromotionPrmInMain { MainTypeId = Mid("PRODUCT"), PrmTypeId = Pid("PRODUCTUPDCBYQTY") },
                new PromotionPrmInMain { MainTypeId = Mid("PRODUCTANDORDER"), PrmTypeId = Pid("PRODUCTUPDC") },
                new PromotionPrmInMain { MainTypeId = Mid("PRODUCTANDORDER"), PrmTypeId = Pid("ORDER") });
            await db.SaveChangesAsync();
        }

        if (!await db.DiscountCodes.AnyAsync())
        {
            // Mã giảm giá mẫu — theo nguồn Inos_DiscountCode.
            db.DiscountCodes.AddRange(
                new DiscountCode { Code = "SALE10", Description = "Giảm 10% giá trị đơn", DiscountType = DiscountCodeType.Percent, DiscountAmount = 10, RemainQty = 100, Enabled = true, EffectDateFrom = DateTime.Today.AddDays(-3), EffectDateTo = DateTime.Today.AddMonths(2) },
                new DiscountCode { Code = "GIAM50K", Description = "Giảm 50.000đ cho đơn hàng", DiscountType = DiscountCodeType.Absolute, DiscountAmount = 50_000, RemainQty = 50, Enabled = true, EffectDateFrom = DateTime.Today.AddDays(-3), EffectDateTo = DateTime.Today.AddMonths(2) });
            await db.SaveChangesAsync();
        }

        if (!await db.DealerDiscountMaps.AnyAsync())
        {
            // Ánh xạ đại lý ↔ mã giảm giá mẫu — theo nguồn Map_DealerDiscount.
            db.DealerDiscountMaps.AddRange(
                new DealerDiscountMap { DealerCode = "DLCP01", DiscountCode = "SALE10", FlagActive = true, Remark = "Đại lý DLCP01 dùng mã SALE10." },
                new DealerDiscountMap { DealerCode = "DLCP01", DiscountCode = "GIAM50K", FlagActive = true, Remark = "Đại lý DLCP01 dùng mã GIAM50K." });
            await db.SaveChangesAsync();
        }

        if (!await db.VoucherIdSequences.AnyAsync())
        {
            // Mã voucher mẫu — theo nguồn Seq_VoucherID (base36 + checksum).
            db.VoucherIdSequences.AddRange(
                new VoucherIdSequence { Seq = 1, VoucherNo = "017YAB00001S", VerGen = "01", Remark = "Mã voucher mẫu 1." },
                new VoucherIdSequence { Seq = 2, VoucherNo = "017YAB00002T", VerGen = "01", Remark = "Mã voucher mẫu 2." });
            await db.SaveChangesAsync();
        }
    }

    private static async Task MigratePostgresAsync(AppDbContext db)
    {
        if (!db.Database.IsNpgsql()) return;
        var def = TenantContext.DefaultOrgId;
        var tables = new[] { "Campaigns", "Prizes", "Entries", "Vouchers", "VoucherRedemptions", "VoucherPrograms", "VoucherProgramDtls", "CarPromotions", "CarPromotionDtls", "PromotionPrograms", "PromotionScopes", "PromotionPrms", "PromotionMains", "PromotionProductScopes", "CarRecommends", "CarRecommendDtls", "CardPromotionPrograms", "CardPromotionProgramDtls", "CardPromotionProgramSpecs", "CardPromotionUsages", "BirthdayPolicies", "BirthdayPolicyDtls", "BirthdayGrants", "IssueVouchers", "IssueVoucherDtls", "IssueVoucherScopes", "IssueVoucherProducts", "IssueVoucherPrices", "RankPolicies", "PolicyMoneyToPoints", "PolicyMoneyToPointDtls", "MemberDiscountTransactions", "PromotionMainTypeDefs", "PromotionPrmTypeDefs", "PromotionPrmInMains", "DiscountCodes", "DealerDiscountMaps", "VoucherIdSequences" };
        var sql = new List<string> {
            "CREATE TABLE IF NOT EXISTS minipromo.\"Orgs\" (\"Id\" uuid PRIMARY KEY, \"Name\" text NOT NULL DEFAULT '', \"ApiKey\" text NOT NULL DEFAULT '', \"CreatedAt\" timestamp NOT NULL DEFAULT now())",
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Orgs_ApiKey\" ON minipromo.\"Orgs\" (\"ApiKey\")" };
        foreach (var t in tables) sql.Add($"ALTER TABLE minipromo.\"{t}\" ADD COLUMN IF NOT EXISTS \"OrgId\" uuid NOT NULL DEFAULT '{def}'");
        foreach (var s in sql) try { await db.Database.ExecuteSqlRawAsync(s); } catch { }
    }
}
