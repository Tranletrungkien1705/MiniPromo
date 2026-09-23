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

    // ---- Hoàn tất / Huỷ chương trình (port từ Prm_VoucherNewCar_Finish/Cancel) ----

    private static async Task<VoucherProgram> ApprovedProgram(IVoucherProgramService svc, decimal point = 5_000_000)
    {
        var (_, _, id) = await svc.CreateProgramAsync(new VoucherProgram
        {
            Code = "PRM" + Guid.NewGuid().ToString("N")[..6].ToUpper(), Name = "CT test",
            EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(30),
            ValidityPeriod = 30, QtyDayLimitFDlvDate = 30, FlagAllModel = false
        });
        await svc.AddDetailAsync(new VoucherProgramDtl { VoucherProgramId = id, ModelCode = "CITY", PointVoucher = point, PointUseLimit = point });
        await svc.SetStatusAsync(id, VoucherProgramStatus.Approved);
        return (await svc.GetProgramAsync(id))!;
    }

    [Fact]
    public async Task Finish_FromApproved_Succeeds()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var p = await ApprovedProgram(svc);
            var (ok, _) = await svc.FinishAsync(p.Id, "hoàn tất");
            Assert.True(ok);
            var after = await svc.GetProgramAsync(p.Id);
            Assert.Equal(VoucherProgramStatus.Finished, after!.Status);
            Assert.Equal("hoàn tất", after.Remark);
        }
    }

    [Fact]
    public async Task Finish_FromPending_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateProgramAsync(new VoucherProgram { Name = "X", EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(10) });
            var (ok, _) = await svc.FinishAsync(id, null);   // đang Pending
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task Finish_DuplicateEffDateStart_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            // Dựng trực tiếp 2 chương trình đã duyệt cùng ngày bắt đầu (bỏ qua guard tạo).
            var a = new VoucherProgram { Code = "A", Name = "A", EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(30), FlagAllModel = true, PointVoucherAllModel = 1_000_000, Status = VoucherProgramStatus.Finished };
            var b = new VoucherProgram { Code = "B", Name = "B", EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(30), FlagAllModel = true, PointVoucherAllModel = 1_000_000, Status = VoucherProgramStatus.Approved };
            db.VoucherPrograms.AddRange(a, b); await db.SaveChangesAsync();
            var (ok, _) = await svc.FinishAsync(b.Id, null);   // trùng ngày bắt đầu với A → từ chối
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task Finish_TrimsPreviousProgramEndDate()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            // Chương trình trước đang hiệu lực (bắt đầu hôm qua, kết thúc +30).
            var prev = new VoucherProgram { Code = "PREV", Name = "PREV", EffDateStart = DateTime.Today.AddDays(-1), EffDateEnd = DateTime.Today.AddDays(30), FlagAllModel = true, PointVoucherAllModel = 1_000_000, Status = VoucherProgramStatus.Finished };
            // Chương trình mới bắt đầu hôm nay → khi hoàn tất phải cắt ngày kết thúc của chương trình trước về hôm qua.
            var next = new VoucherProgram { Code = "NEXT", Name = "NEXT", EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(30), FlagAllModel = true, PointVoucherAllModel = 1_000_000, Status = VoucherProgramStatus.Approved };
            db.VoucherPrograms.AddRange(prev, next); await db.SaveChangesAsync();
            var (ok, _) = await svc.FinishAsync(next.Id, null);
            Assert.True(ok);
            var prevAfter = await svc.GetProgramAsync(prev.Id);
            Assert.Equal(DateTime.Today.AddDays(-1), prevAfter!.EffDateEnd.Date);
        }
    }

    [Fact]
    public async Task Cancel_FromApproved_Succeeds()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var p = await ApprovedProgram(svc);
            var (ok, _) = await svc.CancelAsync(p.Id, "huỷ");
            Assert.True(ok);
            var after = await svc.GetProgramAsync(p.Id);
            Assert.Equal(VoucherProgramStatus.Cancelled, after!.Status);
        }
    }

    [Fact]
    public async Task Cancel_FromFinished_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var p = await ApprovedProgram(svc);
            await svc.FinishAsync(p.Id, null);
            var (ok, _) = await svc.CancelAsync(p.Id, null);   // đã hoàn tất → không huỷ được
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task Reconciliation_GroupsByProgramAndModel()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var p = await FinishedProgram(svc, allModel: false, point: 5_000_000);
            await svc.IssueAsync("CITY", DateTime.Today, DateTime.Today, "HV001");
            await svc.IssueAsync("CITY", DateTime.Today, DateTime.Today, "HV002");
            var rows = await svc.ReconciliationAsync(p.Id);
            Assert.Single(rows);
            Assert.Equal("CITY", rows[0].ModelCode);
            Assert.Equal(2, rows[0].Issued);
            Assert.Equal(0, rows[0].Used);
            Assert.Equal(2, rows[0].Unused);
            Assert.Equal(10_000_000, rows[0].PointIssued);
            Assert.Equal(10_000_000, rows[0].PointRemain);
        }
    }

    [Fact]
    public async Task Reconciliation_CountsUsedVouchers()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var p = await FinishedProgram(svc, allModel: false, point: 5_000_000);
            var issue = await svc.IssueAsync("CITY", DateTime.Today, DateTime.Today, "HV001");
            var voucherSvc = new VoucherService(db);
            await voucherSvc.RedeemAsync(issue.voucherCode!, null, null);   // dùng hết 1 lượt
            var rows = await svc.ReconciliationAsync(p.Id);
            Assert.Single(rows);
            Assert.Equal(1, rows[0].Issued);
            Assert.Equal(1, rows[0].Used);
            Assert.Equal(0, rows[0].Unused);
            Assert.Equal(5_000_000, rows[0].PointUsed);
            Assert.Equal(0, rows[0].PointRemain);
        }
    }
}

/// <summary>Test chương trình khuyến mại mua xe mới: vòng đời duyệt/hoàn tất/huỷ, chỉ 1 chương trình hiệu lực/đại lý, tính giá trị theo model.</summary>
public class CarPromotionServiceTests
{
    private static (AppDbContext db, ICarPromotionService svc, SqliteConnection conn) NewSvc()
    {
        var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        var opt = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(opt, new TenantContext { OrgId = TenantContext.DefaultOrgId });
        db.Database.EnsureCreated();
        return (db, new CarPromotionService(db), conn);
    }

    private static async Task<CarPromotion> FinishedPromotion(ICarPromotionService svc, string dealer = "DLCP01", bool allModel = false, decimal point = 20_000_000)
    {
        var (_, _, id) = await svc.CreatePromotionAsync(new CarPromotion
        {
            Code = "PRMCN" + Guid.NewGuid().ToString("N")[..6].ToUpper(), Name = "CT test", DealerCode = dealer,
            EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(30),
            FlagAllModel = allModel, PointValAllModel = allModel ? point : 0
        });
        if (!allModel)
            await svc.AddDetailAsync(new CarPromotionDtl { CarPromotionId = id, ModelCode = "CITY", PointVal = point });
        await svc.ApproveAsync(id, null);
        await svc.FinishAsync(id, null);
        return (await svc.GetPromotionAsync(id))!;
    }

    [Fact]
    public async Task Create_StartBeforeToday_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (ok, _, _) = await svc.CreatePromotionAsync(new CarPromotion { Name = "X", DealerCode = "D1", EffDateStart = DateTime.Today.AddDays(-1), EffDateEnd = DateTime.Today.AddDays(10) });
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task Create_MissingDealer_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (ok, _, _) = await svc.CreatePromotionAsync(new CarPromotion { Name = "X", EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(10) });
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task Create_StartNotAfterPrevious_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await FinishedPromotion(svc);
            // Chương trình trước bắt đầu hôm nay → chương trình mới cũng bắt đầu hôm nay là không hợp lệ.
            var (ok, _, _) = await svc.CreatePromotionAsync(new CarPromotion { Name = "Y", DealerCode = "DLCP01", EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(10) });
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task Create_DifferentDealer_Allowed()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await FinishedPromotion(svc, dealer: "DLCP01");
            var (ok, _, _) = await svc.CreatePromotionAsync(new CarPromotion { Name = "Y", DealerCode = "DLCP02", EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(10) });
            Assert.True(ok);   // đại lý khác → không bị chặn
        }
    }

    [Fact]
    public async Task AddDetail_NonPositiveValue_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreatePromotionAsync(new CarPromotion { Name = "Z", DealerCode = "D1", EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(10) });
            var (ok, _) = await svc.AddDetailAsync(new CarPromotionDtl { CarPromotionId = id, ModelCode = "CITY", PointVal = 0 });
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task Approve_FromPending_Succeeds()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreatePromotionAsync(new CarPromotion { Name = "A", DealerCode = "D1", EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(10) });
            var (ok, _) = await svc.ApproveAsync(id, null);
            Assert.True(ok);
            Assert.Equal(CarPromotionStatus.Approved, (await svc.GetPromotionAsync(id))!.Status);
        }
    }

    [Fact]
    public async Task Finish_FromPending_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreatePromotionAsync(new CarPromotion { Name = "A", DealerCode = "D1", EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(10), FlagAllModel = true, PointValAllModel = 1_000_000 });
            var (ok, _) = await svc.FinishAsync(id, null);   // chưa duyệt → không hoàn tất được
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task Finish_AllModel_NoValue_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreatePromotionAsync(new CarPromotion { Name = "A", DealerCode = "D1", EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(10), FlagAllModel = true, PointValAllModel = 0 });
            await svc.ApproveAsync(id, null);
            var (ok, _) = await svc.FinishAsync(id, null);   // tất cả model nhưng giá trị chung = 0
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task Finish_CutsPreviousActiveEndDate()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            // Chương trình trước được tạo từ hôm qua (chèn trực tiếp để bỏ qua kiểm tra ngày bắt đầu).
            var prev = new CarPromotion
            {
                Code = "PRMCNOLD", Name = "Cũ", DealerCode = "DLCP01",
                EffDateStart = DateTime.Today.AddDays(-1), EffDateEnd = DateTime.Today.AddDays(30),
                FlagAllModel = true, PointValAllModel = 3_000_000, Status = CarPromotionStatus.Finished
            };
            db.CarPromotions.Add(prev); await db.SaveChangesAsync();

            // Chương trình mới bắt đầu hôm nay (sau chương trình trước) → hoàn tất sẽ cắt ngày kết thúc chương trình trước.
            var (_, _, id) = await svc.CreatePromotionAsync(new CarPromotion
            {
                Name = "Mới", DealerCode = "DLCP01", EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(40),
                FlagAllModel = true, PointValAllModel = 5_000_000
            });
            await svc.ApproveAsync(id, null);
            var (ok, _) = await svc.FinishAsync(id, null);
            Assert.True(ok);
            var prevAfter = await svc.GetPromotionAsync(prev.Id);
            Assert.Equal(DateTime.Today.AddDays(-1), prevAfter!.EffDateEnd.Date);   // = ngày bắt đầu mới − 1
        }
    }

    [Fact]
    public async Task Cancel_FromApproved_Succeeds()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreatePromotionAsync(new CarPromotion { Name = "A", DealerCode = "D1", EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(10) });
            await svc.ApproveAsync(id, null);
            var (ok, _) = await svc.CancelAsync(id, null);
            Assert.True(ok);
            Assert.Equal(CarPromotionStatus.Cancelled, (await svc.GetPromotionAsync(id))!.Status);
        }
    }

    [Fact]
    public async Task Cancel_FromFinished_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var p = await FinishedPromotion(svc);
            var (ok, _) = await svc.CancelAsync(p.Id, null);   // đã hoàn tất → không huỷ được
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task Calc_AllModel_ReturnsCommonValue()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await FinishedPromotion(svc, allModel: true, point: 7_000_000);
            var o = await svc.CalcAsync("DLCP01", "ANY");
            Assert.True(o.ok);
            Assert.Equal(7_000_000, o.pointVal);
        }
    }

    [Fact]
    public async Task Calc_PerModel_ReturnsModelValue()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await FinishedPromotion(svc, allModel: false, point: 20_000_000);
            var o = await svc.CalcAsync("DLCP01", "CITY");
            Assert.True(o.ok);
            Assert.Equal(20_000_000, o.pointVal);
        }
    }

    [Fact]
    public async Task Calc_PerModel_UnknownModel_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await FinishedPromotion(svc, allModel: false);
            var o = await svc.CalcAsync("DLCP01", "UNKNOWN");
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Calc_NoActivePromotion_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var o = await svc.CalcAsync("DLCP01", "CITY");
            Assert.False(o.ok);
        }
    }
}

/// <summary>Test chương trình khuyến mại chung: vòng đời duyệt/hoàn tất/huỷ, điều kiện áp dụng (thứ/giờ/tháng/ngày), tính giảm giá sản phẩm/đơn hàng.</summary>
public class PromotionProgramServiceTests
{
    private static (AppDbContext db, IPromotionProgramService svc, SqliteConnection conn) NewSvc()
    {
        var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        var opt = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(opt, new TenantContext { OrgId = TenantContext.DefaultOrgId });
        db.Database.EnsureCreated();
        return (db, new PromotionProgramService(db), conn);
    }

    // Chương trình đã hoàn tất, áp dụng mọi thời điểm, giảm 10% đơn hàng (tối đa 200.000đ).
    private static async Task<PromotionProgram> FinishedProgram(IPromotionProgramService svc,
        decimal valOrdRateDc = 10, decimal valOrdDcMax = 200_000, decimal valOrdDc = 0)
    {
        var (_, _, id) = await svc.CreateProgramAsync(new PromotionProgram
        {
            Code = "PRM" + Guid.NewGuid().ToString("N")[..6].ToUpper(), Name = "CT test",
            MainType = PromotionMainType.Order, PrmType = PromotionPrmType.Order,
            EffDTimeStart = DateTime.Today, EffDTimeEnd = DateTime.Today.AddDays(30)
        });
        await svc.AddPrmAsync(new PromotionPrm { PromotionProgramId = id, Idx = 1, ValOrdRateDc = valOrdRateDc, ValOrdDcMax = valOrdDcMax, ValOrdDc = valOrdDc });
        await svc.ApproveAsync(id, null);
        await svc.FinishAsync(id, null);
        return (await svc.GetProgramAsync(id))!;
    }

    [Fact]
    public async Task Create_DuplicateCode_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await svc.CreateProgramAsync(new PromotionProgram { Code = "DUP", Name = "A", EffDTimeStart = DateTime.Today, EffDTimeEnd = DateTime.Today.AddDays(10) });
            var (ok, _, _) = await svc.CreateProgramAsync(new PromotionProgram { Code = "DUP", Name = "B", EffDTimeStart = DateTime.Today, EffDTimeEnd = DateTime.Today.AddDays(10) });
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task Create_EndBeforeStart_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (ok, _, _) = await svc.CreateProgramAsync(new PromotionProgram { Name = "X", EffDTimeStart = DateTime.Today, EffDTimeEnd = DateTime.Today.AddDays(-1) });
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task AddPrm_NoDiscount_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateProgramAsync(new PromotionProgram { Name = "Z", EffDTimeStart = DateTime.Today, EffDTimeEnd = DateTime.Today.AddDays(10) });
            var (ok, _) = await svc.AddPrmAsync(new PromotionPrm { PromotionProgramId = id, Idx = 1 });
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task AddPrm_RateOver100_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateProgramAsync(new PromotionProgram { Name = "Z", EffDTimeStart = DateTime.Today, EffDTimeEnd = DateTime.Today.AddDays(10) });
            var (ok, _) = await svc.AddPrmAsync(new PromotionPrm { PromotionProgramId = id, Idx = 1, ValOrdRateDc = 150 });
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task Approve_FromPending_Succeeds()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateProgramAsync(new PromotionProgram { Name = "A", EffDTimeStart = DateTime.Today, EffDTimeEnd = DateTime.Today.AddDays(10) });
            var (ok, _) = await svc.ApproveAsync(id, null);
            Assert.True(ok);
            Assert.Equal(PromotionStatus.Approved, (await svc.GetProgramAsync(id))!.Status);
        }
    }

    [Fact]
    public async Task Finish_FromPending_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateProgramAsync(new PromotionProgram { Name = "A", EffDTimeStart = DateTime.Today, EffDTimeEnd = DateTime.Today.AddDays(10) });
            await svc.AddPrmAsync(new PromotionPrm { PromotionProgramId = id, Idx = 1, ValOrdDc = 10_000 });
            var (ok, _) = await svc.FinishAsync(id, null);   // chưa duyệt
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task Finish_NoPrm_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateProgramAsync(new PromotionProgram { Name = "A", EffDTimeStart = DateTime.Today, EffDTimeEnd = DateTime.Today.AddDays(10) });
            await svc.ApproveAsync(id, null);
            var (ok, _) = await svc.FinishAsync(id, null);   // chưa có hình thức KM
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task Cancel_FromApproved_Succeeds()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateProgramAsync(new PromotionProgram { Name = "A", EffDTimeStart = DateTime.Today, EffDTimeEnd = DateTime.Today.AddDays(10) });
            await svc.ApproveAsync(id, null);
            var (ok, _) = await svc.CancelAsync(id, null);
            Assert.True(ok);
            Assert.Equal(PromotionStatus.Cancelled, (await svc.GetProgramAsync(id))!.Status);
        }
    }

    [Fact]
    public async Task Cancel_FromFinished_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var p = await FinishedProgram(svc);
            var (ok, _) = await svc.CancelAsync(p.Id, null);
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task Calc_OrderRate_CapsAtMax()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await FinishedProgram(svc, valOrdRateDc: 10, valOrdDcMax: 200_000);
            // 10% của 5.000.000 = 500.000 > max 200.000 → chặn ở 200.000.
            var o = await svc.CalcAsync(5_000_000, 1, DateTime.Today);
            Assert.True(o.ok);
            Assert.Equal(200_000, o.orderDiscount);
            Assert.Equal(200_000, o.totalDiscount);
        }
    }

    [Fact]
    public async Task Calc_OrderRate_UnderMax()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await FinishedProgram(svc, valOrdRateDc: 10, valOrdDcMax: 200_000);
            // 10% của 1.000.000 = 100.000 < max → giữ 100.000.
            var o = await svc.CalcAsync(1_000_000, 1, DateTime.Today);
            Assert.True(o.ok);
            Assert.Equal(100_000, o.orderDiscount);
        }
    }

    [Fact]
    public async Task Calc_NoActiveProgram_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var o = await svc.CalcAsync(1_000_000, 1, DateTime.Today);
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Calc_ScopeDayOfWeek_Mismatch_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            // Chỉ áp dụng Thứ Hai (1). Tính vào Chủ Nhật (0) → từ chối.
            var (_, _, id) = await svc.CreateProgramAsync(new PromotionProgram
            {
                Name = "T2", EffDTimeStart = DateTime.Today, EffDTimeEnd = DateTime.Today.AddDays(30),
                FlagAllDayOfWeek = false
            });
            await svc.AddScopeAsync(new PromotionScope { PromotionProgramId = id, ScopeType = PromotionScopeType.DayOfWeek, Value = "1" });
            await svc.AddPrmAsync(new PromotionPrm { PromotionProgramId = id, Idx = 1, ValOrdDc = 50_000 });
            await svc.ApproveAsync(id, null);
            await svc.FinishAsync(id, null);

            // Tìm một ngày Chủ Nhật trong khoảng hiệu lực.
            var sunday = DateTime.Today;
            while (sunday.DayOfWeek != DayOfWeek.Sunday) sunday = sunday.AddDays(1);
            var o = await svc.CalcAsync(1_000_000, 1, sunday);
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Calc_ScopeDayOfWeek_Match_Succeeds()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateProgramAsync(new PromotionProgram
            {
                Name = "T2", EffDTimeStart = DateTime.Today, EffDTimeEnd = DateTime.Today.AddDays(30),
                FlagAllDayOfWeek = false
            });
            await svc.AddScopeAsync(new PromotionScope { PromotionProgramId = id, ScopeType = PromotionScopeType.DayOfWeek, Value = "1" });
            await svc.AddPrmAsync(new PromotionPrm { PromotionProgramId = id, Idx = 1, ValOrdDc = 50_000 });
            await svc.ApproveAsync(id, null);
            await svc.FinishAsync(id, null);

            var monday = DateTime.Today;
            while (monday.DayOfWeek != DayOfWeek.Monday) monday = monday.AddDays(1);
            var o = await svc.CalcAsync(1_000_000, 1, monday);
            Assert.True(o.ok);
            Assert.Equal(50_000, o.orderDiscount);
        }
    }

    [Fact]
    public async Task Calc_MainCondition_NotMet_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateProgramAsync(new PromotionProgram
            {
                Name = "MIN", EffDTimeStart = DateTime.Today, EffDTimeEnd = DateTime.Today.AddDays(30)
            });
            await svc.AddPrmAsync(new PromotionPrm { PromotionProgramId = id, Idx = 1, ValOrdDc = 50_000 });
            await svc.AddMainAsync(new PromotionMain { PromotionProgramId = id, Idx = 1, TotalValOrd = 500_000 });
            await svc.ApproveAsync(id, null);
            await svc.FinishAsync(id, null);

            var o = await svc.CalcAsync(300_000, 1, DateTime.Today);   // < 500.000
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Calc_ProductDiscount_WithMulti()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateProgramAsync(new PromotionProgram
            {
                Name = "MULTI", EffDTimeStart = DateTime.Today, EffDTimeEnd = DateTime.Today.AddDays(30),
                FlagMulti = true
            });
            await svc.AddPrmAsync(new PromotionPrm { PromotionProgramId = id, Idx = 1, UPDc = 20_000 });
            await svc.ApproveAsync(id, null);
            await svc.FinishAsync(id, null);

            var o = await svc.CalcAsync(1_000_000, 3, DateTime.Today);
            Assert.True(o.ok);
            Assert.Equal(60_000, o.productDiscount);   // 20.000 × 3
        }
    }

    [Fact]
    public async Task Calc_TotalDiscount_CappedAtOrderAmount()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateProgramAsync(new PromotionProgram
            {
                Name = "CAP", EffDTimeStart = DateTime.Today, EffDTimeEnd = DateTime.Today.AddDays(30)
            });
            await svc.AddPrmAsync(new PromotionPrm { PromotionProgramId = id, Idx = 1, ValOrdDc = 5_000_000 });
            await svc.ApproveAsync(id, null);
            await svc.FinishAsync(id, null);

            var o = await svc.CalcAsync(1_000_000, 1, DateTime.Today);
            Assert.True(o.ok);
            Assert.Equal(1_000_000, o.totalDiscount);   // không giảm quá giá trị đơn hàng
        }
    }
}

/// <summary>Test chương trình giới thiệu xe: vòng đời duyệt/hoàn tất/huỷ, chỉ 1 chương trình hiệu lực/đại lý, tính giá trị thưởng theo model.</summary>
public class CarRecommendServiceTests
{
    private static (AppDbContext db, ICarRecommendService svc, SqliteConnection conn) NewSvc()
    {
        var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        var opt = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(opt, new TenantContext { OrgId = TenantContext.DefaultOrgId });
        db.Database.EnsureCreated();
        return (db, new CarRecommendService(db), conn);
    }

    private static async Task<CarRecommend> FinishedRecommend(ICarRecommendService svc, string dealer = "DLCP01", bool allModel = false, decimal point = 3_000_000)
    {
        var (_, _, id) = await svc.CreateRecommendAsync(new CarRecommend
        {
            Code = "PRMCR" + Guid.NewGuid().ToString("N")[..6].ToUpper(), Name = "CT test", DealerCode = dealer,
            EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(30),
            FlagAllModel = allModel, PointValAllModel = allModel ? point : 0
        });
        if (!allModel)
            await svc.AddDetailAsync(new CarRecommendDtl { CarRecommendId = id, ModelCode = "CITY", PointVal = point });
        await svc.ApproveAsync(id, null);
        await svc.FinishAsync(id, null);
        return (await svc.GetRecommendAsync(id))!;
    }

    [Fact]
    public async Task Create_StartBeforeToday_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (ok, _, _) = await svc.CreateRecommendAsync(new CarRecommend { Name = "X", DealerCode = "D1", EffDateStart = DateTime.Today.AddDays(-1), EffDateEnd = DateTime.Today.AddDays(10) });
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task Create_MissingDealer_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (ok, _, _) = await svc.CreateRecommendAsync(new CarRecommend { Name = "X", EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(10) });
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task Create_StartNotAfterPrevious_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await FinishedRecommend(svc);
            // Chương trình trước bắt đầu hôm nay → chương trình mới cũng bắt đầu hôm nay là không hợp lệ.
            var (ok, _, _) = await svc.CreateRecommendAsync(new CarRecommend { Name = "Y", DealerCode = "DLCP01", EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(10) });
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task Create_DifferentDealer_Allowed()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await FinishedRecommend(svc, dealer: "DLCP01");
            var (ok, _, _) = await svc.CreateRecommendAsync(new CarRecommend { Name = "Y", DealerCode = "DLCP02", EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(10) });
            Assert.True(ok);   // đại lý khác → không bị chặn
        }
    }

    [Fact]
    public async Task AddDetail_NonPositiveValue_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateRecommendAsync(new CarRecommend { Name = "Z", DealerCode = "D1", EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(10) });
            var (ok, _) = await svc.AddDetailAsync(new CarRecommendDtl { CarRecommendId = id, ModelCode = "CITY", PointVal = 0 });
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task Approve_FromPending_Succeeds()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateRecommendAsync(new CarRecommend { Name = "A", DealerCode = "D1", EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(10) });
            var (ok, _) = await svc.ApproveAsync(id, null);
            Assert.True(ok);
            Assert.Equal(CarRecommendStatus.Approved, (await svc.GetRecommendAsync(id))!.Status);
        }
    }

    [Fact]
    public async Task Finish_FromPending_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateRecommendAsync(new CarRecommend { Name = "A", DealerCode = "D1", EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(10), FlagAllModel = true, PointValAllModel = 1_000_000 });
            var (ok, _) = await svc.FinishAsync(id, null);   // chưa duyệt → không hoàn tất được
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task Finish_AllModel_NoValue_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateRecommendAsync(new CarRecommend { Name = "A", DealerCode = "D1", EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(10), FlagAllModel = true, PointValAllModel = 0 });
            await svc.ApproveAsync(id, null);
            var (ok, _) = await svc.FinishAsync(id, null);   // tất cả model nhưng giá trị chung = 0
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task Finish_CutsPreviousActiveEndDate()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            // Chương trình trước được tạo từ hôm qua (chèn trực tiếp để bỏ qua kiểm tra ngày bắt đầu).
            var prev = new CarRecommend
            {
                Code = "PRMCROLD", Name = "Cũ", DealerCode = "DLCP01",
                EffDateStart = DateTime.Today.AddDays(-1), EffDateEnd = DateTime.Today.AddDays(30),
                FlagAllModel = true, PointValAllModel = 3_000_000, Status = CarRecommendStatus.Finished
            };
            db.CarRecommends.Add(prev); await db.SaveChangesAsync();

            // Chương trình mới bắt đầu hôm nay (sau chương trình trước) → hoàn tất sẽ cắt ngày kết thúc chương trình trước.
            var (_, _, id) = await svc.CreateRecommendAsync(new CarRecommend
            {
                Name = "Mới", DealerCode = "DLCP01", EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(40),
                FlagAllModel = true, PointValAllModel = 5_000_000
            });
            await svc.ApproveAsync(id, null);
            var (ok, _) = await svc.FinishAsync(id, null);
            Assert.True(ok);
            var prevAfter = await svc.GetRecommendAsync(prev.Id);
            Assert.Equal(DateTime.Today.AddDays(-1), prevAfter!.EffDateEnd.Date);   // = ngày bắt đầu mới − 1
        }
    }

    [Fact]
    public async Task Cancel_FromApproved_Succeeds()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateRecommendAsync(new CarRecommend { Name = "A", DealerCode = "D1", EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(10) });
            await svc.ApproveAsync(id, null);
            var (ok, _) = await svc.CancelAsync(id, null);
            Assert.True(ok);
            Assert.Equal(CarRecommendStatus.Cancelled, (await svc.GetRecommendAsync(id))!.Status);
        }
    }

    [Fact]
    public async Task Cancel_FromFinished_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var p = await FinishedRecommend(svc);
            var (ok, _) = await svc.CancelAsync(p.Id, null);   // đã hoàn tất → không huỷ được
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task Calc_AllModel_ReturnsCommonValue()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await FinishedRecommend(svc, allModel: true, point: 4_000_000);
            var o = await svc.CalcAsync("DLCP01", "ANY");
            Assert.True(o.ok);
            Assert.Equal(4_000_000, o.pointVal);
        }
    }

    [Fact]
    public async Task Calc_PerModel_ReturnsModelValue()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await FinishedRecommend(svc, allModel: false, point: 3_000_000);
            var o = await svc.CalcAsync("DLCP01", "CITY");
            Assert.True(o.ok);
            Assert.Equal(3_000_000, o.pointVal);
        }
    }

    [Fact]
    public async Task Calc_PerModel_UnknownModel_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await FinishedRecommend(svc, allModel: false);
            var o = await svc.CalcAsync("DLCP01", "UNKNOWN");
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Calc_NoActiveRecommend_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var o = await svc.CalcAsync("DLCP01", "CITY");
            Assert.False(o.ok);
        }
    }
}
/// <summary>Test chương trình khuyến mại theo loại thẻ: hạn mức theo loại thẻ, phạm vi đại lý, ghi nhận sử dụng, đối soát.</summary>
public class CardPromotionProgramServiceTests
{
    private static (AppDbContext db, ICardPromotionProgramService svc, SqliteConnection conn) NewSvc()
    {
        var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        var opt = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(opt, new TenantContext { OrgId = TenantContext.DefaultOrgId });
        db.Database.EnsureCreated();
        return (db, new CardPromotionProgramService(db), conn);
    }

    // Chương trình đang bật, áp dụng tất cả đại lý, loại thẻ GOLD hạn mức 10.
    private static async Task<CardPromotionProgram> ActiveProgram(ICardPromotionProgramService svc,
        bool allDL = true, int qty = 10, string cardType = "GOLD")
    {
        var (_, _, id) = await svc.CreateProgramAsync(new CardPromotionProgram
        {
            Code = "PRMPR" + Guid.NewGuid().ToString("N")[..6].ToUpper(), Name = "CT test",
            EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(30), FlagAllDL = allDL
        });
        await svc.AddDetailAsync(new CardPromotionProgramDtl { CardPromotionProgramId = id, CardType = cardType, Qty = qty });
        if (!allDL) await svc.AddDealerAsync(new CardPromotionProgramSpec { CardPromotionProgramId = id, DealerCode = "DLCP01" });
        await svc.SetStatusAsync(id, CardPromotionProgramStatus.Active);
        return (await svc.GetProgramAsync(id))!;
    }

    [Fact]
    public async Task Create_MissingName_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (ok, _, _) = await svc.CreateProgramAsync(new CardPromotionProgram { Name = "" });
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task Create_EndBeforeStart_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (ok, _, _) = await svc.CreateProgramAsync(new CardPromotionProgram { Name = "X", EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(-1) });
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task AddDetail_NonPositiveQty_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateProgramAsync(new CardPromotionProgram { Name = "X" });
            var (ok, _) = await svc.AddDetailAsync(new CardPromotionProgramDtl { CardPromotionProgramId = id, CardType = "GOLD", Qty = 0 });
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task AddDetail_DuplicateCardType_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateProgramAsync(new CardPromotionProgram { Name = "X" });
            await svc.AddDetailAsync(new CardPromotionProgramDtl { CardPromotionProgramId = id, CardType = "GOLD", Qty = 5 });
            var (ok, _) = await svc.AddDetailAsync(new CardPromotionProgramDtl { CardPromotionProgramId = id, CardType = "GOLD", Qty = 3 });
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task SetStatus_ActiveWithoutDetail_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateProgramAsync(new CardPromotionProgram { Name = "X" });
            var (ok, _) = await svc.SetStatusAsync(id, CardPromotionProgramStatus.Active);
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task SetStatus_ActiveWithoutDealer_WhenNotAllDL_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateProgramAsync(new CardPromotionProgram { Name = "X", FlagAllDL = false });
            await svc.AddDetailAsync(new CardPromotionProgramDtl { CardPromotionProgramId = id, CardType = "GOLD", Qty = 5 });
            var (ok, _) = await svc.SetStatusAsync(id, CardPromotionProgramStatus.Active);
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task CheckUse_NoActiveProgram_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var o = await svc.CheckUseAsync("DLCP01", "GOLD", 1);
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task CheckUse_UnknownCardType_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await ActiveProgram(svc);
            var o = await svc.CheckUseAsync("DLCP01", "SILVER", 1);
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task CheckUse_DealerOutOfScope_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await ActiveProgram(svc, allDL: false);
            var o = await svc.CheckUseAsync("DLCP99", "GOLD", 1);
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task CheckUse_DealerInScope_Ok()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await ActiveProgram(svc, allDL: false);
            var o = await svc.CheckUseAsync("DLCP01", "GOLD", 1);
            Assert.True(o.ok);
            Assert.Equal(10, o.qtyRemain);
        }
    }

    [Fact]
    public async Task CheckUse_QtyExceedsRemain_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await ActiveProgram(svc, qty: 3);
            var o = await svc.CheckUseAsync("DLCP01", "GOLD", 5);
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Use_DecrementsRemain()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await ActiveProgram(svc, qty: 10);
            var o = await svc.UseAsync("DEAL1", "DLCP01", "CARD1", "GOLD", 4);
            Assert.True(o.ok);
            Assert.Equal(6, o.qtyRemain);
            Assert.Equal(4, o.qtyUsed);
            Assert.Equal(6, await svc.QtyRemainAsync((await svc.ActiveProgramAsync())!.Id, "GOLD"));
        }
    }

    [Fact]
    public async Task Use_ExhaustsRemain_ThenRejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await ActiveProgram(svc, qty: 5);
            await svc.UseAsync("DEAL1", "DLCP01", "CARD1", "GOLD", 5);
            var o = await svc.UseAsync("DEAL2", "DLCP01", "CARD2", "GOLD", 1);
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Use_MissingDealNo_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await ActiveProgram(svc);
            var o = await svc.UseAsync("", "DLCP01", "CARD1", "GOLD", 1);
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Reconciliation_ReportsUsedAndRemain()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var p = await ActiveProgram(svc, qty: 10);
            await svc.UseAsync("DEAL1", "DLCP01", "CARD1", "GOLD", 3);
            var rows = await svc.ReconciliationAsync(p.Id);
            var row = Assert.Single(rows);
            Assert.Equal("GOLD", row.CardType);
            Assert.Equal(10, row.Qty);
            Assert.Equal(3, row.QtyUsed);
            Assert.Equal(7, row.QtyRemain);
        }
    }
}