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
    }

    public override int SaveChanges() { StampOrg(); return base.SaveChanges(); }
    public override Task<int> SaveChangesAsync(CancellationToken ct = default) { StampOrg(); return base.SaveChangesAsync(ct); }
    private void StampOrg()
    {
        foreach (var e in ChangeTracker.Entries<IOrgOwned>())
            if (e.State == EntityState.Added && e.Entity.OrgId == Guid.Empty) e.Entity.OrgId = _orgId;
    }
}
