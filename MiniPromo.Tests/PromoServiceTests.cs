using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniPromo.Data;
using MiniPromo.Models;
using MiniPromo.Services;
using Xunit;

namespace MiniPromo.Tests;

/// <summary>Test quay thưởng: chỉ chơi khi Running, 1 mã tem chơi 1 lần, giải 100% trúng, hết suất, guard chạy cần giải.</summary>
public class PromoServiceTests
{
    private static (AppDbContext db, IPromoService svc, SqliteConnection conn) NewSvc()
    {
        var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        var opt = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(opt, new TenantContext { OrgId = TenantContext.DefaultOrgId });
        db.Database.EnsureCreated();
        return (db, new PromoService(db), conn);
    }

    // Tạo chiến dịch chắc-chắn-trúng: loseWeight=0, 1 giải nhiều suất.
    private static async Task<Campaign> RunningCampaign(AppDbContext db, IPromoService svc, int prizeQty = 10, int loseWeight = 0)
    {
        var (_, _, id) = await svc.CreateCampaignAsync(new Campaign { Code = "TET", Name = "Tết", LoseWeight = loseWeight, FromDate = DateTime.Today.AddDays(-1), ToDate = DateTime.Today.AddDays(10) });
        await svc.AddPrizeAsync(new Prize { CampaignId = id, Name = "Voucher", Tier = "KK", Value = 50000, Quantity = prizeQty, Weight = 100 });
        await svc.SetStatusAsync(id, CampaignStatus.Running);
        return (await svc.GetCampaignAsync(id))!;
    }

    [Fact]
    public async Task Play_Draft_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateCampaignAsync(new Campaign { Code = "X", Name = "X" });
            var o = await svc.PlayAsync("X", "TEM1", null, null);
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Play_GuaranteedWin_WhenNoLoseWeight()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var c = await RunningCampaign(db, svc);
            var o = await svc.PlayAsync(c.Code, "TEM1", "A", "090");
            Assert.True(o.ok);
            Assert.True(o.win);
            Assert.Equal("Voucher", o.prizeName);
        }
    }

    [Fact]
    public async Task Play_SameCode_Twice_Blocked()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var c = await RunningCampaign(db, svc);
            await svc.PlayAsync(c.Code, "TEM1", null, null);
            var o2 = await svc.PlayAsync(c.Code, "TEM1", null, null);
            Assert.False(o2.ok);  // 1 mã chơi 1 lần
        }
    }

    [Fact]
    public async Task Play_DecrementsPrizeStock()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var c = await RunningCampaign(db, svc, prizeQty: 2);
            await svc.PlayAsync(c.Code, "T1", null, null);
            await svc.PlayAsync(c.Code, "T2", null, null);
            var stat = await svc.StatAsync(c.Id);
            Assert.Equal(2, stat.Wins);
            Assert.Equal(0, stat.PrizesLeft);
            // Hết suất → lượt sau trượt (không có giải còn suất, loseWeight=0 nhưng không giải nào còn)
            var o3 = await svc.PlayAsync(c.Code, "T3", null, null);
            Assert.True(o3.ok);
            Assert.False(o3.win);
        }
    }

    [Fact]
    public async Task SetStatus_RunningNeedsPrize()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateCampaignAsync(new Campaign { Code = "Y", Name = "Y" });
            var (ok, _) = await svc.SetStatusAsync(id, CampaignStatus.Running);  // chưa có giải
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task Claim_WinningEntry()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var c = await RunningCampaign(db, svc);
            await svc.PlayAsync(c.Code, "T1", null, null);
            var entry = (await svc.EntriesAsync(c.Id, PlayResult.Win)).First();
            var (ok, _) = await svc.SetClaimAsync(entry.Id, ClaimStatus.Claimed);
            Assert.True(ok);
        }
    }
}

/// <summary>Test mã giảm giá / điểm voucher: hạn mức mỗi lần, số lượt, hết hạn, tạm dừng, hết điểm.</summary>
public class VoucherServiceTests
{
    private static (AppDbContext db, IVoucherService svc, SqliteConnection conn) NewSvc()
    {
        var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        var opt = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(opt, new TenantContext { OrgId = TenantContext.DefaultOrgId });
        db.Database.EnsureCreated();
        return (db, new VoucherService(db), conn);
    }

    private static async Task<Voucher> MakeVoucher(IVoucherService svc, decimal total = 200_000, decimal limit = 50_000, int uses = 4, DateTime? expire = null)
    {
        var (_, _, id) = await svc.CreateVoucherAsync(new Voucher
        {
            Code = "VC" + Guid.NewGuid().ToString("N")[..6].ToUpper(), Name = "Voucher test",
            PointTotal = total, PointLimit = limit, QtyUseLimit = uses,
            ExpireDate = expire ?? DateTime.Today.AddMonths(1)
        });
        return (await svc.GetVoucherAsync(id))!;
    }

    [Fact]
    public async Task Redeem_DeductsPerUseLimit_AndDecrementsUses()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var v = await MakeVoucher(svc, total: 200_000, limit: 50_000, uses: 4);
            var o = await svc.RedeemAsync(v.Code, null, null);
            Assert.True(o.ok);
            Assert.Equal(50_000, o.pointUsed);
            Assert.Equal(150_000, o.pointRemain);
            Assert.Equal(3, o.qtyUseRemain);
        }
    }

    [Fact]
    public async Task Redeem_CapsAtAmount_WhenAmountSmaller()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var v = await MakeVoucher(svc, total: 200_000, limit: 50_000, uses: 4);
            var o = await svc.RedeemAsync(v.Code, 30_000, null);
            Assert.True(o.ok);
            Assert.Equal(30_000, o.pointUsed);
            Assert.Equal(170_000, o.pointRemain);
        }
    }

    [Fact]
    public async Task Redeem_ExhaustsUses_ThenRejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var v = await MakeVoucher(svc, total: 200_000, limit: 50_000, uses: 2);
            Assert.True((await svc.RedeemAsync(v.Code, null, null)).ok);
            Assert.True((await svc.RedeemAsync(v.Code, null, null)).ok);
            var o3 = await svc.RedeemAsync(v.Code, null, null);
            Assert.False(o3.ok);  // hết lượt
        }
    }

    [Fact]
    public async Task Redeem_Expired_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var v = await MakeVoucher(svc, expire: DateTime.Today.AddDays(-1));
            var o = await svc.RedeemAsync(v.Code, null, null);
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Redeem_Inactive_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var v = await MakeVoucher(svc);
            await svc.SetActiveAsync(v.Id, false);
            var o = await svc.RedeemAsync(v.Code, null, null);
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Redeem_UnknownCode_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var o = await svc.RedeemAsync("KHONGCO", null, null);
            Assert.False(o.ok);
        }
    }
}
