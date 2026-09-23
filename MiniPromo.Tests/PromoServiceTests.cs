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

/// <summary>Test chương trình voucher theo model: chỉ 1 chương trình hiệu lực, điều kiện ngày, tính giá trị theo model.</summary>
public class VoucherProgramServiceTests
{
    private static (AppDbContext db, IVoucherProgramService svc, SqliteConnection conn) NewSvc()
    {
        var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        var opt = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(opt, new TenantContext { OrgId = TenantContext.DefaultOrgId });
        db.Database.EnsureCreated();
        return (db, new VoucherProgramService(db), conn);
    }

    private static async Task<VoucherProgram> FinishedProgram(IVoucherProgramService svc, bool allModel = false, decimal point = 5_000_000)
    {
        var (_, _, id) = await svc.CreateProgramAsync(new VoucherProgram
        {
            Code = "PRM" + Guid.NewGuid().ToString("N")[..6].ToUpper(), Name = "CT test",
            EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(30),
            ValidityPeriod = 30, QtyDayLimitFDlvDate = 30, FlagAllModel = allModel,
            PointVoucherAllModel = allModel ? point : 0, PointUseLimitAllModel = allModel ? point : 0
        });
        if (!allModel)
            await svc.AddDetailAsync(new VoucherProgramDtl { VoucherProgramId = id, ModelCode = "CITY", PointVoucher = point, PointUseLimit = point });
        await svc.SetStatusAsync(id, VoucherProgramStatus.Finished);
        return (await svc.GetProgramAsync(id))!;
    }

    [Fact]
    public async Task Create_StartBeforeToday_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (ok, _, _) = await svc.CreateProgramAsync(new VoucherProgram { Name = "X", EffDateStart = DateTime.Today.AddDays(-1), EffDateEnd = DateTime.Today.AddDays(10) });
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task Create_StartNotAfterPrevious_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await FinishedProgram(svc);
            // Chương trình trước bắt đầu hôm nay → chương trình mới cũng bắt đầu hôm nay là không hợp lệ.
            var (ok, _, _) = await svc.CreateProgramAsync(new VoucherProgram { Name = "Y", EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(10) });
            Assert.False(ok);  // phải sau chương trình trước
        }
    }

    [Fact]
    public async Task AddDetail_NonPositiveValue_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateProgramAsync(new VoucherProgram { Name = "Z", EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(10) });
            var (ok, _) = await svc.AddDetailAsync(new VoucherProgramDtl { VoucherProgramId = id, ModelCode = "CITY", PointVoucher = 0 });
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task Calc_AllModel_ReturnsCommonValue()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await FinishedProgram(svc, allModel: true, point: 7_000_000);
            var o = await svc.CalcAsync("ANY", DateTime.Today, DateTime.Today);
            Assert.True(o.ok);
            Assert.Equal(7_000_000, o.pointVoucher);
        }
    }

    [Fact]
    public async Task Calc_PerModel_UnknownModel_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await FinishedProgram(svc, allModel: false);
            var o = await svc.CalcAsync("UNKNOWN", DateTime.Today, DateTime.Today);
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Calc_RegistrationBeyondDayLimit_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await FinishedProgram(svc, allModel: false);
            var o = await svc.CalcAsync("CITY", DateTime.Today, DateTime.Today.AddDays(60));  // > 30 ngày
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Calc_ValidModel_ReturnsValue()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await FinishedProgram(svc, allModel: false, point: 5_000_000);
            var o = await svc.CalcAsync("city", DateTime.Today, DateTime.Today.AddDays(5));
            Assert.True(o.ok);
            Assert.Equal(5_000_000, o.pointVoucher);
        }
    }

    [Fact]
    public async Task Issue_ValidModel_CreatesLinkedVoucher()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var p = await FinishedProgram(svc, allModel: false, point: 5_000_000);
            var o = await svc.IssueAsync("CITY", DateTime.Today, DateTime.Today.AddDays(5), "HV001");
            Assert.True(o.ok);
            Assert.False(string.IsNullOrWhiteSpace(o.voucherCode));
            var v = await db.Vouchers.FirstAsync(x => x.Id == o.voucherId);
            Assert.Equal(5_000_000, v.PointTotal);
            Assert.Equal(5_000_000, v.PointRemain);
            Assert.Equal(p.Id, v.VoucherProgramId);
            Assert.Equal("CITY", v.ModelCode);
            Assert.Equal("HV001", v.MemberNo);
            Assert.Equal(DateTime.Today.AddDays(30), v.ExpireDate);   // hôm nay + ValidityPeriod
            Assert.True(v.IsUsable);
        }
    }

    [Fact]
    public async Task Issue_UnknownModel_Rejected_NoVoucher()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await FinishedProgram(svc, allModel: false);
            var o = await svc.IssueAsync("UNKNOWN", DateTime.Today, DateTime.Today, null);
            Assert.False(o.ok);
            Assert.Equal(0, await db.Vouchers.CountAsync());
        }
    }

    [Fact]
    public async Task Issue_RegistrationBeyondDayLimit_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await FinishedProgram(svc, allModel: false);
            var o = await svc.IssueAsync("CITY", DateTime.Today, DateTime.Today.AddDays(60), null);  // > 30 ngày
            Assert.False(o.ok);
            Assert.Equal(0, await db.Vouchers.CountAsync());
        }
    }
}
