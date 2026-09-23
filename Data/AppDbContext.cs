using Microsoft.EntityFrameworkCore;
using MiniPromo.Models;

namespace MiniPromo.Data;

public class AppDbContext : DbContext
{
    private readonly Guid _orgId;
    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant) : base(options) => _orgId = tenant.OrgId;

    public DbSet<Org> Orgs => Set<Org>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<Prize> Prizes => Set<Prize>();
    public DbSet<Entry> Entries => Set<Entry>();
    public DbSet<Voucher> Vouchers => Set<Voucher>();
    public DbSet<VoucherRedemption> VoucherRedemptions => Set<VoucherRedemption>();
    public DbSet<VoucherProgram> VoucherPrograms => Set<VoucherProgram>();
    public DbSet<VoucherProgramDtl> VoucherProgramDtls => Set<VoucherProgramDtl>();
    public DbSet<CarPromotion> CarPromotions => Set<CarPromotion>();
    public DbSet<CarPromotionDtl> CarPromotionDtls => Set<CarPromotionDtl>();
    public DbSet<PromotionProgram> PromotionPrograms => Set<PromotionProgram>();
    public DbSet<PromotionScope> PromotionScopes => Set<PromotionScope>();
    public DbSet<PromotionPrm> PromotionPrms => Set<PromotionPrm>();
    public DbSet<PromotionMain> PromotionMains => Set<PromotionMain>();
    public DbSet<PromotionProductScope> PromotionProductScopes => Set<PromotionProductScope>();
    public DbSet<CarRecommend> CarRecommends => Set<CarRecommend>();
    public DbSet<CarRecommendDtl> CarRecommendDtls => Set<CarRecommendDtl>();
    public DbSet<CardPromotionProgram> CardPromotionPrograms => Set<CardPromotionProgram>();
    public DbSet<CardPromotionProgramDtl> CardPromotionProgramDtls => Set<CardPromotionProgramDtl>();
    public DbSet<CardPromotionProgramSpec> CardPromotionProgramSpecs => Set<CardPromotionProgramSpec>();
    public DbSet<CardPromotionUsage> CardPromotionUsages => Set<CardPromotionUsage>();
    public DbSet<BirthdayPolicy> BirthdayPolicies => Set<BirthdayPolicy>();
    public DbSet<BirthdayPolicyDtl> BirthdayPolicyDtls => Set<BirthdayPolicyDtl>();
    public DbSet<BirthdayGrant> BirthdayGrants => Set<BirthdayGrant>();
    public DbSet<BirthdayVoucher> BirthdayVouchers => Set<BirthdayVoucher>();
    public DbSet<IssueVoucher> IssueVouchers => Set<IssueVoucher>();
    public DbSet<IssueVoucherDtl> IssueVoucherDtls => Set<IssueVoucherDtl>();
    public DbSet<IssueVoucherScope> IssueVoucherScopes => Set<IssueVoucherScope>();
    public DbSet<IssueVoucherProduct> IssueVoucherProducts => Set<IssueVoucherProduct>();
    public DbSet<IssueVoucherPrice> IssueVoucherPrices => Set<IssueVoucherPrice>();
    public DbSet<ParamPromotionType> ParamPromotionTypes => Set<ParamPromotionType>();
    public DbSet<ParamPromotion> ParamPromotions => Set<ParamPromotion>();
    public DbSet<RankPolicy> RankPolicies => Set<RankPolicy>();
    public DbSet<PolicyMoneyToPoint> PolicyMoneyToPoints => Set<PolicyMoneyToPoint>();
    public DbSet<PolicyMoneyToPointDtl> PolicyMoneyToPointDtls => Set<PolicyMoneyToPointDtl>();
    public DbSet<MemberDiscountTransaction> MemberDiscountTransactions => Set<MemberDiscountTransaction>();
    public DbSet<PromotionMainTypeDef> PromotionMainTypeDefs => Set<PromotionMainTypeDef>();
    public DbSet<PromotionPrmTypeDef> PromotionPrmTypeDefs => Set<PromotionPrmTypeDef>();
    public DbSet<PromotionPrmInMain> PromotionPrmInMains => Set<PromotionPrmInMain>();
    public DbSet<DiscountCode> DiscountCodes => Set<DiscountCode>();
    public DbSet<DealerDiscountMap> DealerDiscountMaps => Set<DealerDiscountMap>();
    public DbSet<VoucherIdSequence> VoucherIdSequences => Set<VoucherIdSequence>();
    public DbSet<IntroductionGrant> IntroductionGrants => Set<IntroductionGrant>();
    public DbSet<CarPurchasePointGrant> CarPurchasePointGrants => Set<CarPurchasePointGrant>();
    public DbSet<ExpenseType> ExpenseTypes => Set<ExpenseType>();
    public DbSet<PolicyExpenseType> PolicyExpenseTypes => Set<PolicyExpenseType>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        if (Database.IsNpgsql()) b.HasDefaultSchema("minipromo");
        b.Entity<Org>().HasIndex(x => x.ApiKey).IsUnique();
        b.Entity<Campaign>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();           // Mã chiến dịch công khai — tra cứu xuyên tenant
            e.Ignore(x => x.IsLiveNow);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<Prize>(e =>
        {
            e.Property(x => x.Value).HasPrecision(18, 2);
            e.Ignore(x => x.Remaining);
            e.HasOne(x => x.Campaign).WithMany(x => x.Prizes).HasForeignKey(x => x.CampaignId);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<Entry>(e =>
        {
            e.HasIndex(x => new { x.CampaignId, x.Code }).IsUnique();   // 1 mã chơi 1 lần / chiến dịch
            e.HasOne(x => x.Campaign).WithMany().HasForeignKey(x => x.CampaignId);
            e.HasOne(x => x.Prize).WithMany().HasForeignKey(x => x.PrizeId);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<Voucher>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();           // Mã voucher công khai — tra cứu xuyên tenant
            e.Property(x => x.PointTotal).HasPrecision(18, 2);
            e.Property(x => x.PointRemain).HasPrecision(18, 2);
            e.Property(x => x.PointLimit).HasPrecision(18, 2);
            e.Ignore(x => x.IsExpired);
            e.Ignore(x => x.IsUsable);
            e.Ignore(x => x.Status);
            e.HasOne(x => x.VoucherProgram).WithMany().HasForeignKey(x => x.VoucherProgramId);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<VoucherRedemption>(e =>
        {
            e.Property(x => x.PointUsed).HasPrecision(18, 2);
            e.Property(x => x.PointRemainAfter).HasPrecision(18, 2);
            e.HasOne(x => x.Voucher).WithMany(x => x.Redemptions).HasForeignKey(x => x.VoucherId);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<VoucherProgram>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();           // Mã chương trình voucher — duy nhất
            e.Property(x => x.PointVoucherAllModel).HasPrecision(18, 2);
            e.Property(x => x.PointUseLimitAllModel).HasPrecision(18, 2);
            e.Ignore(x => x.IsLiveNow);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<VoucherProgramDtl>(e =>
        {
            e.Property(x => x.PointVoucher).HasPrecision(18, 2);
            e.Property(x => x.PointUseLimit).HasPrecision(18, 2);
            e.HasOne(x => x.VoucherProgram).WithMany(x => x.Details).HasForeignKey(x => x.VoucherProgramId);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<CarPromotion>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();           // Mã chương trình khuyến mại — duy nhất
            e.Property(x => x.PointValAllModel).HasPrecision(18, 2);
            e.Ignore(x => x.IsLiveNow);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<CarPromotionDtl>(e =>
        {
            e.Property(x => x.PointVal).HasPrecision(18, 2);
            e.HasOne(x => x.CarPromotion).WithMany(x => x.Details).HasForeignKey(x => x.CarPromotionId);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<PromotionProgram>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();           // Mã chương trình khuyến mại — duy nhất
            e.Property(x => x.BudgetVal).HasPrecision(18, 2);
            e.Ignore(x => x.IsLiveNow);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<PromotionScope>(e =>
        {
            e.HasOne(x => x.PromotionProgram).WithMany(x => x.Scopes).HasForeignKey(x => x.PromotionProgramId);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<PromotionPrm>(e =>
        {
            e.Property(x => x.UPDc).HasPrecision(18, 2);
            e.Property(x => x.UPRateDc).HasPrecision(18, 2);
            e.Property(x => x.UPDcMax).HasPrecision(18, 2);
            e.Property(x => x.ValOrdDc).HasPrecision(18, 2);
            e.Property(x => x.ValOrdRateDc).HasPrecision(18, 2);
            e.Property(x => x.ValOrdDcMax).HasPrecision(18, 2);
            e.HasOne(x => x.PromotionProgram).WithMany(x => x.Prms).HasForeignKey(x => x.PromotionProgramId);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<PromotionMain>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.TotalValOrd).HasPrecision(18, 2);
            e.HasOne(x => x.PromotionProgram).WithMany(x => x.Mains).HasForeignKey(x => x.PromotionProgramId);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<PromotionProductScope>(e =>
        {
            e.HasOne(x => x.PromotionProgram).WithMany(x => x.ProductScopes).HasForeignKey(x => x.PromotionProgramId);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<CarRecommend>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();           // Mã chương trình giới thiệu — duy nhất
            e.Property(x => x.PointValAllModel).HasPrecision(18, 2);
            e.Ignore(x => x.IsLiveNow);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<CarRecommendDtl>(e =>
        {
            e.Property(x => x.PointVal).HasPrecision(18, 2);
            e.HasOne(x => x.CarRecommend).WithMany(x => x.Details).HasForeignKey(x => x.CarRecommendId);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<CardPromotionProgram>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();           // Mã chương trình khuyến mại theo loại thẻ — duy nhất
            e.Ignore(x => x.IsLiveNow);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<CardPromotionProgramDtl>(e =>
        {
            e.HasOne(x => x.CardPromotionProgram).WithMany(x => x.Details).HasForeignKey(x => x.CardPromotionProgramId);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<CardPromotionProgramSpec>(e =>
        {
            e.HasOne(x => x.CardPromotionProgram).WithMany(x => x.Dealers).HasForeignKey(x => x.CardPromotionProgramId);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<CardPromotionUsage>(e =>
        {
            e.HasOne(x => x.CardPromotionProgram).WithMany().HasForeignKey(x => x.CardPromotionProgramId);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<BirthdayPolicy>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();           // Mã chương trình sinh nhật — duy nhất
            e.Property(x => x.ParamValue).HasPrecision(18, 2);
            e.Ignore(x => x.IsLiveNow);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<BirthdayPolicyDtl>(e =>
        {
            e.Property(x => x.Point).HasPrecision(18, 2);
            e.Property(x => x.VoucherValue).HasPrecision(18, 2);
            e.HasOne(x => x.BirthdayPolicy).WithMany(x => x.Details).HasForeignKey(x => x.BirthdayPolicyId);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<BirthdayGrant>(e =>
        {
            e.Property(x => x.Point).HasPrecision(18, 2);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasOne(x => x.BirthdayPolicy).WithMany().HasForeignKey(x => x.BirthdayPolicyId);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<BirthdayVoucher>(e =>
        {
            e.HasIndex(x => x.VoucherNo).IsUnique();      // Mã voucher sinh nhật — duy nhất (idempotent theo năm)
            e.Property(x => x.PointVCTotal).HasPrecision(18, 2);
            e.Property(x => x.PointVCRemain).HasPrecision(18, 2);
            e.Property(x => x.PointVCLimit).HasPrecision(18, 2);
            e.Ignore(x => x.IsExpired);
            e.Ignore(x => x.IsUsable);
            e.HasOne(x => x.BirthdayPolicy).WithMany().HasForeignKey(x => x.BirthdayPolicyId);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<IssueVoucher>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();           // Mã đợt phát hành — duy nhất
            e.Ignore(x => x.IsLiveNow);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<IssueVoucherDtl>(e =>
        {
            e.HasIndex(x => x.VoucherNo).IsUnique();      // Mã voucher — duy nhất
            e.HasOne(x => x.IssueVoucher).WithMany(x => x.Details).HasForeignKey(x => x.IssueVoucherId);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<IssueVoucherScope>(e =>
        {
            e.HasOne(x => x.IssueVoucher).WithMany(x => x.Scopes).HasForeignKey(x => x.IssueVoucherId);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<IssueVoucherProduct>(e =>
        {
            e.HasOne(x => x.IssueVoucher).WithMany(x => x.Products).HasForeignKey(x => x.IssueVoucherId);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<IssueVoucherPrice>(e =>
        {
            e.Property(x => x.UPDc).HasPrecision(18, 2);
            e.Property(x => x.UPRateDc).HasPrecision(18, 2);
            e.Property(x => x.UPDcMax).HasPrecision(18, 2);
            e.HasOne(x => x.IssueVoucher).WithMany(x => x.Prices).HasForeignKey(x => x.IssueVoucherId);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<ParamPromotionType>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();           // Mã loại áp dụng — duy nhất
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<ParamPromotion>(e =>
        {
            e.HasIndex(x => new { x.ProgramCode, x.ParamPromotionTypeId }).IsUnique();   // 1 chương trình + 1 loại áp dụng
            e.HasOne(x => x.ParamPromotionType).WithMany(x => x.Params).HasForeignKey(x => x.ParamPromotionTypeId);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<RankPolicy>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();           // Mã chính sách xếp hạng — duy nhất
            e.Property(x => x.PointUpBegin).HasPrecision(18, 2);
            e.Property(x => x.PointUpEnd).HasPrecision(18, 2);
            e.Property(x => x.PointKeepBegin).HasPrecision(18, 2);
            e.Property(x => x.PointKeepEnd).HasPrecision(18, 2);
            e.Ignore(x => x.IsActive);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<PolicyMoneyToPoint>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();           // Mã chính sách quy đổi — duy nhất
            e.Ignore(x => x.IsLiveNow);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<PolicyMoneyToPointDtl>(e =>
        {
            e.Property(x => x.ConvertValue).HasPrecision(18, 2);
            e.Property(x => x.ConvertPoint).HasPrecision(18, 2);
            e.Property(x => x.ValueRankCardType).HasPrecision(18, 2);
            e.Property(x => x.DiscountRate).HasPrecision(18, 2);
            e.Ignore(x => x.Rate);
            e.HasOne(x => x.PolicyMoneyToPoint).WithMany(x => x.Details).HasForeignKey(x => x.PolicyMoneyToPointId);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<MemberDiscountTransaction>(e =>
        {
            e.Property(x => x.PolicyDiscountRate).HasPrecision(18, 2);
            e.Property(x => x.AmountForDC).HasPrecision(18, 2);
            e.Property(x => x.PointChTotal).HasPrecision(18, 2);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<PromotionMainTypeDef>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();           // Mã loại khuyến mại theo — duy nhất
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<PromotionPrmTypeDef>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();           // Mã hình thức khuyến mại — duy nhất
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<PromotionPrmInMain>(e =>
        {
            e.HasIndex(x => new { x.MainTypeId, x.PrmTypeId }).IsUnique();   // 1 hình thức / 1 loại khuyến mại theo
            e.HasOne(x => x.MainType).WithMany(x => x.PrmInMains).HasForeignKey(x => x.MainTypeId);
            e.HasOne(x => x.PrmType).WithMany(x => x.PrmInMains).HasForeignKey(x => x.PrmTypeId);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<DiscountCode>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();           // Mã giảm giá — duy nhất
            e.Property(x => x.DiscountAmount).HasPrecision(18, 2);
            e.Ignore(x => x.IsLiveNow);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<DealerDiscountMap>(e =>
        {
            e.HasIndex(x => new { x.DealerCode, x.DiscountCode }).IsUnique();   // 1 đại lý + 1 mã giảm giá
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<VoucherIdSequence>(e =>
        {
            e.HasIndex(x => x.VoucherNo).IsUnique();      // Mã voucher sinh ra — duy nhất
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<IntroductionGrant>(e =>
        {
            e.HasIndex(x => x.RefNo).IsUnique();          // Số giao dịch tặng điểm giới thiệu — duy nhất
            e.Property(x => x.PointChTotal).HasPrecision(18, 2);
            e.Property(x => x.AmountChTotal).HasPrecision(18, 2);
            e.Property(x => x.ParamValue).HasPrecision(18, 2);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<CarPurchasePointGrant>(e =>
        {
            e.HasIndex(x => x.RefNo).IsUnique();          // Số giao dịch tặng điểm mua xe mới — duy nhất
            e.Property(x => x.PointChTotal).HasPrecision(18, 2);
            e.Property(x => x.AmountChTotal).HasPrecision(18, 2);
            e.Property(x => x.ParamValue).HasPrecision(18, 2);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<ExpenseType>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();           // Mã loại chi phí — duy nhất
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<PolicyExpenseType>(e =>
        {
            e.HasIndex(x => new { x.PolicyExpenseTypeNo, x.ExpenseType }).IsUnique();   // 1 chính sách + 1 loại chi phí
            e.Property(x => x.AmountRate).HasPrecision(18, 2);
            e.Property(x => x.MaxRankReviewPoint).HasPrecision(18, 2);
            e.Property(x => x.MaxAccumulationPoint).HasPrecision(18, 2);
            e.Property(x => x.DiscountRate).HasPrecision(18, 2);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
    }

    public override int SaveChanges() { StampOrg(); return base.SaveChanges(); }
    public override Task<int> SaveChangesAsync(CancellationToken ct = default) { StampOrg(); return base.SaveChangesAsync(ct); }
    private void StampOrg()
    {
        foreach (var e in ChangeTracker.Entries<IOrgOwned>())
            if (e.State == EntityState.Added && e.Entity.OrgId == Guid.Empty) e.Entity.OrgId = _orgId;
    }
}
