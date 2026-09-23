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
    public DbSet<CarRecommend> CarRecommends => Set<CarRecommend>();
    public DbSet<CarRecommendDtl> CarRecommendDtls => Set<CarRecommendDtl>();

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
    }

    public override int SaveChanges() { StampOrg(); return base.SaveChanges(); }
    public override Task<int> SaveChangesAsync(CancellationToken ct = default) { StampOrg(); return base.SaveChangesAsync(ct); }
    private void StampOrg()
    {
        foreach (var e in ChangeTracker.Entries<IOrgOwned>())
            if (e.State == EntityState.Added && e.Entity.OrgId == Guid.Empty) e.Entity.OrgId = _orgId;
    }
}
