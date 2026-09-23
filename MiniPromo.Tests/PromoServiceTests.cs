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

    // ---- Phạm vi sản phẩm áp dụng (port từ Prm_PromotionMainSpec/Prm_PromotionPrmSpec) ----

    [Fact]
    public async Task AddProductScope_EmptyRefCode_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateProgramAsync(new PromotionProgram { Name = "PS", EffDTimeStart = DateTime.Today, EffDTimeEnd = DateTime.Today.AddDays(10) });
            var (ok, _) = await svc.AddProductScopeAsync(new PromotionProductScope { PromotionProgramId = id, RefCode = "" });
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task AddProductScope_Duplicate_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateProgramAsync(new PromotionProgram { Name = "PS", EffDTimeStart = DateTime.Today, EffDTimeEnd = DateTime.Today.AddDays(10) });
            await svc.AddProductScopeAsync(new PromotionProductScope { PromotionProgramId = id, RefType = PromotionRefType.Product, RefCode = "SP001" });
            var (ok, _) = await svc.AddProductScopeAsync(new PromotionProductScope { PromotionProgramId = id, RefType = PromotionRefType.Product, RefCode = "sp001" });
            Assert.False(ok);   // trùng sau khi chuẩn hoá hoa/thường
        }
    }

    [Fact]
    public async Task Calc_WithProductScope_NoLines_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateProgramAsync(new PromotionProgram { Name = "PS", EffDTimeStart = DateTime.Today, EffDTimeEnd = DateTime.Today.AddDays(30) });
            await svc.AddPrmAsync(new PromotionPrm { PromotionProgramId = id, Idx = 1, ValOrdRateDc = 10 });
            await svc.AddProductScopeAsync(new PromotionProductScope { PromotionProgramId = id, RefType = PromotionRefType.Product, RefCode = "SP001" });
            await svc.ApproveAsync(id, null);
            await svc.FinishAsync(id, null);

            var o = await svc.CalcAsync(1_000_000, 1, DateTime.Today);   // không truyền dòng hàng
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Calc_WithProductScope_NoMatchingLine_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateProgramAsync(new PromotionProgram { Name = "PS", EffDTimeStart = DateTime.Today, EffDTimeEnd = DateTime.Today.AddDays(30) });
            await svc.AddPrmAsync(new PromotionPrm { PromotionProgramId = id, Idx = 1, ValOrdRateDc = 10 });
            await svc.AddProductScopeAsync(new PromotionProductScope { PromotionProgramId = id, RefType = PromotionRefType.Product, RefCode = "SP001" });
            await svc.ApproveAsync(id, null);
            await svc.FinishAsync(id, null);

            var lines = new List<PromotionOrderLine> { new("SP999", PromotionRefType.Product, 1, 1_000_000) };
            var o = await svc.CalcAsync(1_000_000, 1, DateTime.Today, lines);
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Calc_WithProductScope_OnlyMatchingLinesCounted()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateProgramAsync(new PromotionProgram { Name = "PS", EffDTimeStart = DateTime.Today, EffDTimeEnd = DateTime.Today.AddDays(30) });
            await svc.AddPrmAsync(new PromotionPrm { PromotionProgramId = id, Idx = 1, ValOrdRateDc = 10, ValOrdDcMax = 0 });
            await svc.AddProductScopeAsync(new PromotionProductScope { PromotionProgramId = id, RefType = PromotionRefType.Product, RefCode = "SP001" });
            await svc.ApproveAsync(id, null);
            await svc.FinishAsync(id, null);

            // Đơn 1.000.000 nhưng chỉ dòng SP001 (400.000) thuộc phạm vi → giảm 10% của 400.000 = 40.000.
            var lines = new List<PromotionOrderLine>
            {
                new("SP001", PromotionRefType.Product, 1, 400_000),
                new("SP999", PromotionRefType.Product, 1, 600_000)
            };
            var o = await svc.CalcAsync(1_000_000, 2, DateTime.Today, lines);
            Assert.True(o.ok);
            Assert.Equal(40_000, o.orderDiscount);
        }
    }

    [Fact]
    public async Task Calc_WithProductGroupScope_MatchesByRefType()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateProgramAsync(new PromotionProgram { Name = "PS", EffDTimeStart = DateTime.Today, EffDTimeEnd = DateTime.Today.AddDays(30) });
            await svc.AddPrmAsync(new PromotionPrm { PromotionProgramId = id, Idx = 1, ValOrdRateDc = 10, ValOrdDcMax = 0 });
            await svc.AddProductScopeAsync(new PromotionProductScope { PromotionProgramId = id, RefType = PromotionRefType.ProductGroup, RefCode = "GRP01" });
            await svc.ApproveAsync(id, null);
            await svc.FinishAsync(id, null);

            // Cùng mã GRP01 nhưng RefType khác (Product) → không khớp; chỉ dòng ProductGroup mới tính.
            var lines = new List<PromotionOrderLine>
            {
                new("GRP01", PromotionRefType.Product, 1, 500_000),
                new("GRP01", PromotionRefType.ProductGroup, 1, 300_000)
            };
            var o = await svc.CalcAsync(800_000, 2, DateTime.Today, lines);
            Assert.True(o.ok);
            Assert.Equal(30_000, o.orderDiscount);   // 10% của 300.000
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

    // ---- Luật "1 ngày + 1 chương trình + 1 hội viên + 1 loại thẻ ≤ 1" (port từ Crd_DealUsePromotion_SaveX) ----

    [Fact]
    public async Task Use_SameMemberSameDay_SecondUse_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await ActiveProgram(svc, qty: 10);
            var day = DateTime.Today;
            var o1 = await svc.UseAsync("DEAL1", "DLCP01", "CARD1", "GOLD", 1, "HV001", day);
            Assert.True(o1.ok);
            // Cùng hội viên + cùng loại thẻ + cùng ngày → lần 2 bị chặn (tối đa 1/ngày).
            var o2 = await svc.UseAsync("DEAL2", "DLCP01", "CARD1", "GOLD", 1, "HV001", day);
            Assert.False(o2.ok);
        }
    }

    [Fact]
    public async Task Use_SameMemberDifferentDay_Allowed()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await ActiveProgram(svc, qty: 10);
            var o1 = await svc.UseAsync("DEAL1", "DLCP01", "CARD1", "GOLD", 1, "HV001", DateTime.Today);
            Assert.True(o1.ok);
            // Khác ngày → được phép.
            var o2 = await svc.UseAsync("DEAL2", "DLCP01", "CARD1", "GOLD", 1, "HV001", DateTime.Today.AddDays(1));
            Assert.True(o2.ok);
        }
    }

    [Fact]
    public async Task Use_DifferentMemberSameDay_Allowed()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await ActiveProgram(svc, qty: 10);
            var day = DateTime.Today;
            Assert.True((await svc.UseAsync("DEAL1", "DLCP01", "CARD1", "GOLD", 1, "HV001", day)).ok);
            // Hội viên khác → không bị chặn.
            Assert.True((await svc.UseAsync("DEAL2", "DLCP01", "CARD2", "GOLD", 1, "HV002", day)).ok);
        }
    }

    [Fact]
    public async Task Use_NoMember_SkipsDailyRule()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await ActiveProgram(svc, qty: 10);
            var day = DateTime.Today;
            // Không truyền hội viên → không áp luật theo ngày, chỉ còn giới hạn tổng Qty.
            Assert.True((await svc.UseAsync("DEAL1", "DLCP01", "CARD1", "GOLD", 1, null, day)).ok);
            Assert.True((await svc.UseAsync("DEAL2", "DLCP01", "CARD1", "GOLD", 1, null, day)).ok);
        }
    }
}
/// <summary>Test chương trình tặng điểm sinh nhật: vòng đời bật/tạm dừng, điều kiện ngày sinh, loại thẻ, 1 lần/năm, quy đổi điểm→tiền, đối soát.</summary>
public class BirthdayPolicyServiceTests
{
    private static (AppDbContext db, IBirthdayPolicyService svc, SqliteConnection conn) NewSvc()
    {
        var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        var opt = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(opt, new TenantContext { OrgId = TenantContext.DefaultOrgId });
        db.Database.EnsureCreated();
        return (db, new BirthdayPolicyService(db), conn);
    }

    // Chương trình đang bật, áp dụng tất cả, loại thẻ GOLD điểm 500, tỷ lệ quy đổi 1.000.
    private static async Task<BirthdayPolicy> ActivePolicy(IBirthdayPolicyService svc,
        decimal point = 500, decimal paramValue = 1_000, string cardType = "GOLD")
    {
        var (_, _, id) = await svc.CreatePolicyAsync(new BirthdayPolicy
        {
            Code = "BIRTH" + Guid.NewGuid().ToString("N")[..6].ToUpper(), Name = "CT test",
            EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(30),
            FlagPoint = true, ParamValue = paramValue
        });
        await svc.AddDetailAsync(new BirthdayPolicyDtl { BirthdayPolicyId = id, CardType = cardType, Point = point });
        await svc.SetStatusAsync(id, BirthdayPolicyStatus.Active);
        return (await svc.GetPolicyAsync(id))!;
    }

    [Fact]
    public async Task Create_MissingName_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (ok, _, _) = await svc.CreatePolicyAsync(new BirthdayPolicy { Name = "" });
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task Create_EndBeforeStart_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (ok, _, _) = await svc.CreatePolicyAsync(new BirthdayPolicy { Name = "X", EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(-1) });
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task AddDetail_NonPositivePoint_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreatePolicyAsync(new BirthdayPolicy { Name = "X" });
            var (ok, _) = await svc.AddDetailAsync(new BirthdayPolicyDtl { BirthdayPolicyId = id, CardType = "GOLD", Point = 0 });
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task AddDetail_DuplicateCardType_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreatePolicyAsync(new BirthdayPolicy { Name = "X" });
            await svc.AddDetailAsync(new BirthdayPolicyDtl { BirthdayPolicyId = id, CardType = "GOLD", Point = 500 });
            var (ok, _) = await svc.AddDetailAsync(new BirthdayPolicyDtl { BirthdayPolicyId = id, CardType = "GOLD", Point = 300 });
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task SetStatus_ActiveWithoutDetail_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreatePolicyAsync(new BirthdayPolicy { Name = "X" });
            var (ok, _) = await svc.SetStatusAsync(id, BirthdayPolicyStatus.Active);
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task Check_NoActivePolicy_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var o = await svc.CheckEligibilityAsync("HV001", "GOLD", DateTime.Today, DateTime.Today);
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Check_NotBirthday_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await ActivePolicy(svc);
            var o = await svc.CheckEligibilityAsync("HV001", "GOLD", DateTime.Today.AddDays(1), DateTime.Today);
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Check_UnknownCardType_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await ActivePolicy(svc);
            var o = await svc.CheckEligibilityAsync("HV001", "SILVER", DateTime.Today, DateTime.Today);
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Check_Birthday_ReturnsPointAndAmount()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await ActivePolicy(svc, point: 500, paramValue: 1_000);
            var o = await svc.CheckEligibilityAsync("HV001", "GOLD", DateTime.Today, DateTime.Today);
            Assert.True(o.ok);
            Assert.Equal(500, o.point);
            Assert.Equal(500_000, o.amount);   // 500 × 1.000
            Assert.Equal("GOLD", o.cardType);
        }
    }

    [Fact]
    public async Task Grant_CreatesGrantRecord()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var p = await ActivePolicy(svc, point: 500, paramValue: 1_000);
            var o = await svc.GrantAsync("HV001", "CARD1", "GOLD", "DLCP01", DateTime.Today, DateTime.Today);
            Assert.True(o.ok);
            Assert.Equal(500, o.point);
            Assert.Equal(500_000, o.amount);
            var g = await db.BirthdayGrants.FirstAsync();
            Assert.Equal("HV001", g.MemberNo);
            Assert.Equal(p.Id, g.BirthdayPolicyId);
            Assert.Equal(500_000, g.Amount);
        }
    }

    [Fact]
    public async Task Grant_TwiceInYear_SecondRejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await ActivePolicy(svc);
            Assert.True((await svc.GrantAsync("HV001", "CARD1", "GOLD", "DLCP01", DateTime.Today, DateTime.Today)).ok);
            var o2 = await svc.GrantAsync("HV001", "CARD1", "GOLD", "DLCP01", DateTime.Today, DateTime.Today);
            Assert.False(o2.ok);   // mỗi hội viên 1 lần/năm
            Assert.Equal(1, await db.BirthdayGrants.CountAsync());
        }
    }

    [Fact]
    public async Task Grant_NotBirthday_Rejected_NoRecord()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await ActivePolicy(svc);
            var o = await svc.GrantAsync("HV001", "CARD1", "GOLD", "DLCP01", DateTime.Today.AddDays(1), DateTime.Today);
            Assert.False(o.ok);
            Assert.Equal(0, await db.BirthdayGrants.CountAsync());
        }
    }

    [Fact]
    public async Task Reconciliation_ReportsGrantedByCardType()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var p = await ActivePolicy(svc, point: 500, paramValue: 1_000);
            await svc.GrantAsync("HV001", "CARD1", "GOLD", "DLCP01", DateTime.Today, DateTime.Today);
            await svc.GrantAsync("HV002", "CARD2", "GOLD", "DLCP01", DateTime.Today, DateTime.Today);
            var rows = await svc.ReconciliationAsync(p.Id);
            var row = Assert.Single(rows);
            Assert.Equal("GOLD", row.CardType);
            Assert.Equal(2, row.Granted);
            Assert.Equal(1_000, row.PointGranted);
            Assert.Equal(1_000_000, row.AmountGranted);
        }
    }
}

/// <summary>Test voucher sinh nhật: chỉ phát khi chương trình bật + có cờ voucher, điều kiện ngày sinh,
/// hạng thẻ có cấu hình voucher, mỗi hội viên 1 voucher/năm (idempotent theo mã BV.YYYY.MemberNo), đối soát.</summary>
public class BirthdayVoucherServiceTests
{
    private static (AppDbContext db, IBirthdayVoucherService svc, SqliteConnection conn) NewSvc()
    {
        var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        var opt = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(opt, new TenantContext { OrgId = TenantContext.DefaultOrgId });
        db.Database.EnsureCreated();
        return (db, new BirthdayVoucherService(db), conn);
    }

    // Chương trình đang bật, có phát voucher, hạng GOLD voucher 500 điểm hạn 30 ngày.
    private static async Task<BirthdayPolicy> ActiveVoucherPolicy(IBirthdayPolicyService policySvc,
        decimal voucherValue = 500, int expireDays = 30, string cardType = "GOLD")
    {
        var (_, _, id) = await policySvc.CreatePolicyAsync(new BirthdayPolicy
        {
            Code = "BIRTH" + Guid.NewGuid().ToString("N")[..6].ToUpper(), Name = "CT voucher test",
            EffDateStart = DateTime.Today, EffDateEnd = DateTime.Today.AddDays(30),
            FlagPoint = true, FlagVoucher = true, ParamValue = 1_000
        });
        await policySvc.AddDetailAsync(new BirthdayPolicyDtl { BirthdayPolicyId = id, CardType = cardType, Point = 500, VoucherValue = voucherValue, VoucherExpireDays = expireDays });
        await policySvc.SetStatusAsync(id, BirthdayPolicyStatus.Active);
        return (await policySvc.GetPolicyAsync(id))!;
    }

    [Fact]
    public async Task Check_NoActivePolicy_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var o = await svc.CheckEligibilityAsync("HV001", "GOLD", DateTime.Today, DateTime.Today);
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Check_NotBirthday_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var policySvc = new BirthdayPolicyService(db);
            await ActiveVoucherPolicy(policySvc);
            var o = await svc.CheckEligibilityAsync("HV001", "GOLD", DateTime.Today.AddDays(1), DateTime.Today);
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Check_CardTypeWithoutVoucher_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var policySvc = new BirthdayPolicyService(db);
            await ActiveVoucherPolicy(policySvc, voucherValue: 0);   // hạng không cấu hình voucher
            var o = await svc.CheckEligibilityAsync("HV001", "GOLD", DateTime.Today, DateTime.Today);
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Check_Birthday_ReturnsPointAndExpireDays()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var policySvc = new BirthdayPolicyService(db);
            await ActiveVoucherPolicy(policySvc, voucherValue: 500, expireDays: 30);
            var o = await svc.CheckEligibilityAsync("HV001", "GOLD", DateTime.Today, DateTime.Today);
            Assert.True(o.ok);
            Assert.Equal(500, o.point);
            Assert.Equal(30, o.expireDays);
            Assert.Equal("GOLD", o.cardType);
        }
    }

    [Fact]
    public async Task Issue_CreatesVoucherWithSupportDealer()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var policySvc = new BirthdayPolicyService(db);
            var p = await ActiveVoucherPolicy(policySvc, voucherValue: 500, expireDays: 30);
            var o = await svc.IssueAsync("HV001", "CARD1", "GOLD", DateTime.Today, DateTime.Today);
            Assert.True(o.ok);
            Assert.Equal($"BV.{DateTime.Today.Year}.HV001", o.voucherNo);
            Assert.Equal(500, o.point);
            Assert.Equal(DateTime.Today.AddDays(30), o.expireDate);
            var v = await db.BirthdayVouchers.FirstAsync();
            Assert.Equal("SUPPORT", v.DealerCode);
            Assert.Equal(p.Id, v.BirthdayPolicyId);
            Assert.Equal(500, v.PointVCTotal);
            Assert.Equal(500, v.PointVCRemain);
            Assert.Equal(1, v.QtyUseVCLimit);
        }
    }

    [Fact]
    public async Task Issue_TwiceInYear_SecondRejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var policySvc = new BirthdayPolicyService(db);
            await ActiveVoucherPolicy(policySvc);
            Assert.True((await svc.IssueAsync("HV001", "CARD1", "GOLD", DateTime.Today, DateTime.Today)).ok);
            var o2 = await svc.IssueAsync("HV001", "CARD1", "GOLD", DateTime.Today, DateTime.Today);
            Assert.False(o2.ok);   // mỗi hội viên 1 voucher/năm
            Assert.Equal(1, await db.BirthdayVouchers.CountAsync());
        }
    }

    [Fact]
    public async Task Issue_NotBirthday_Rejected_NoRecord()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var policySvc = new BirthdayPolicyService(db);
            await ActiveVoucherPolicy(policySvc);
            var o = await svc.IssueAsync("HV001", "CARD1", "GOLD", DateTime.Today.AddDays(1), DateTime.Today);
            Assert.False(o.ok);
            Assert.Equal(0, await db.BirthdayVouchers.CountAsync());
        }
    }

    [Fact]
    public async Task Reconciliation_ReportsIssuedByCardType()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var policySvc = new BirthdayPolicyService(db);
            var p = await ActiveVoucherPolicy(policySvc, voucherValue: 500);
            await svc.IssueAsync("HV001", "CARD1", "GOLD", DateTime.Today, DateTime.Today);
            await svc.IssueAsync("HV002", "CARD2", "GOLD", DateTime.Today, DateTime.Today);
            var rows = await svc.ReconciliationAsync(p.Id);
            var row = Assert.Single(rows);
            Assert.Equal("GOLD", row.CardType);
            Assert.Equal(2, row.Issued);
            Assert.Equal(1_000, row.PointIssued);
            Assert.Equal(1_000, row.PointRemain);
        }
    }
}
/// <summary>Test đợt phát hành voucher: chỉ phát khi đợt hiệu lực, chặn vượt số lượng, chặn trùng mã,
/// voucher hết hạn không dùng được, vòng đời Chưa phát → Đã phát → Đã dùng/Thu hồi/Huỷ, đối soát theo trạng thái.</summary>
public class IssueVoucherServiceTests
{
    private static (AppDbContext db, IIssueVoucherService svc, SqliteConnection conn) NewSvc()
    {
        var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        var opt = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(opt, new TenantContext { OrgId = TenantContext.DefaultOrgId });
        db.Database.EnsureCreated();
        return (db, new IssueVoucherService(db), conn);
    }

    // Tạo đợt đang bật, trong hiệu lực, có 1 dòng giá trị ưu đãi.
    private static async Task<IssueVoucher> ActiveBatch(AppDbContext db, IIssueVoucherService svc, int qty = 2, int qtyDateUse = 30)
    {
        var (_, _, id) = await svc.CreateBatchAsync(new IssueVoucher
        {
            Code = "ISSUE1", Name = "Đợt 1", QtyVoucher = qty, QtyDateUse = qtyDateUse,
            EffDateStart = DateTime.Today.AddDays(-1), EffDateEnd = DateTime.Today.AddDays(10)
        });
        await svc.AddPriceAsync(new IssueVoucherPrice { IssueVoucherId = id, IssueType = IssuePriceType.Issue, IssueTypeDtl = "Giảm giá", UPRateDc = 10 });
        await svc.SetActiveAsync(id, true);
        return (await svc.GetBatchAsync(id))!;
    }

    [Fact]
    public async Task Create_RequiresNameAndQty()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            Assert.False((await svc.CreateBatchAsync(new IssueVoucher { Name = "", QtyVoucher = 1, QtyDateUse = 1 })).ok);
            Assert.False((await svc.CreateBatchAsync(new IssueVoucher { Name = "X", QtyVoucher = 0, QtyDateUse = 1 })).ok);
            Assert.False((await svc.CreateBatchAsync(new IssueVoucher { Name = "X", QtyVoucher = 1, QtyDateUse = 0 })).ok);
        }
    }

    [Fact]
    public async Task Create_DuplicateCode_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await svc.CreateBatchAsync(new IssueVoucher { Code = "A", Name = "A", QtyVoucher = 1, QtyDateUse = 1 });
            var o = await svc.CreateBatchAsync(new IssueVoucher { Code = "A", Name = "B", QtyVoucher = 1, QtyDateUse = 1 });
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task SetActive_RequiresPrice()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateBatchAsync(new IssueVoucher { Code = "A", Name = "A", QtyVoucher = 1, QtyDateUse = 1 });
            Assert.False((await svc.SetActiveAsync(id, true)).ok);   // chưa có giá trị ưu đãi
            await svc.AddPriceAsync(new IssueVoucherPrice { IssueVoucherId = id, IssueTypeDtl = "Giảm giá", UPDc = 10_000 });
            Assert.True((await svc.SetActiveAsync(id, true)).ok);
        }
    }

    [Fact]
    public async Task Issue_WhenActive_CreatesVoucherWithExpiry()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var b = await ActiveBatch(db, svc, qtyDateUse: 30);
            var o = await svc.IssueAsync(b.Id, "VC001", "Nguyễn A", DateTime.Today);
            Assert.True(o.ok);
            Assert.Equal("VC001", o.voucherNo);
            Assert.Equal(DateTime.Today.AddDays(30), o.expDate);
            Assert.Equal(1, await db.IssueVoucherDtls.CountAsync());
        }
    }

    [Fact]
    public async Task Issue_WhenInactive_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreateBatchAsync(new IssueVoucher { Code = "A", Name = "A", QtyVoucher = 5, QtyDateUse = 30, EffDateStart = DateTime.Today.AddDays(-1), EffDateEnd = DateTime.Today.AddDays(10) });
            await svc.AddPriceAsync(new IssueVoucherPrice { IssueVoucherId = id, IssueTypeDtl = "Giảm giá", UPDc = 10_000 });
            var o = await svc.IssueAsync(id, "VC001", null, DateTime.Today);   // chưa bật
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Issue_ExceedQty_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var b = await ActiveBatch(db, svc, qty: 2);
            Assert.True((await svc.IssueAsync(b.Id, "VC001", null, DateTime.Today)).ok);
            Assert.True((await svc.IssueAsync(b.Id, "VC002", null, DateTime.Today)).ok);
            var o = await svc.IssueAsync(b.Id, "VC003", null, DateTime.Today);
            Assert.False(o.ok);   // vượt số lượng
        }
    }

    [Fact]
    public async Task Issue_DuplicateVoucherNo_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var b = await ActiveBatch(db, svc, qty: 5);
            Assert.True((await svc.IssueAsync(b.Id, "VC001", null, DateTime.Today)).ok);
            var o = await svc.IssueAsync(b.Id, "VC001", null, DateTime.Today);
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task CheckUse_Issued_Valid()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var b = await ActiveBatch(db, svc);
            await svc.IssueAsync(b.Id, "VC001", null, DateTime.Today);
            var o = await svc.CheckUseAsync("VC001", DateTime.Today);
            Assert.True(o.ok);
            Assert.Equal("Discount", o.favorType);
        }
    }

    [Fact]
    public async Task CheckUse_Expired_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var b = await ActiveBatch(db, svc, qtyDateUse: 5);
            await svc.IssueAsync(b.Id, "VC001", null, DateTime.Today);
            var o = await svc.CheckUseAsync("VC001", DateTime.Today.AddDays(6));
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Use_ThenCheck_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var b = await ActiveBatch(db, svc);
            await svc.IssueAsync(b.Id, "VC001", null, DateTime.Today);
            Assert.True((await svc.UseAsync("VC001", "DH01", DateTime.Today)).ok);
            Assert.False((await svc.CheckUseAsync("VC001", DateTime.Today)).ok);   // đã dùng
            var dtl = await db.IssueVoucherDtls.FirstAsync();
            Assert.Equal(IssueVoucherStatus.Used, dtl.Status);
            Assert.Equal("DH01", dtl.OrderNo);
        }
    }

    [Fact]
    public async Task Evict_OnlyIssued()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var b = await ActiveBatch(db, svc);
            var o = await svc.IssueAsync(b.Id, "VC001", null, DateTime.Today);
            Assert.True((await svc.EvictAsync(o.voucherId)).ok);
            Assert.False((await svc.EvictAsync(o.voucherId)).ok);   // đã thu hồi
            Assert.False((await svc.CheckUseAsync("VC001", DateTime.Today)).ok);
        }
    }

    [Fact]
    public async Task Cancel_UsedVoucher_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var b = await ActiveBatch(db, svc);
            var o = await svc.IssueAsync(b.Id, "VC001", null, DateTime.Today);
            await svc.UseAsync("VC001", null, DateTime.Today);
            Assert.False((await svc.CancelVoucherAsync(o.voucherId)).ok);
        }
    }

    [Fact]
    public async Task Reconciliation_CountsByStatus()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var b = await ActiveBatch(db, svc, qty: 5);
            var v1 = await svc.IssueAsync(b.Id, "VC001", null, DateTime.Today);
            await svc.IssueAsync(b.Id, "VC002", null, DateTime.Today);
            await svc.IssueAsync(b.Id, "VC003", null, DateTime.Today);
            await svc.UseAsync("VC001", null, DateTime.Today);
            await svc.EvictAsync(v1.voucherId == 0 ? 0 : (await db.IssueVoucherDtls.FirstAsync(d => d.VoucherNo == "VC002")).Id);
            var rows = await svc.ReconciliationAsync(b.Id);
            var row = Assert.Single(rows);
            Assert.Equal(5, row.QtyVoucher);
            Assert.Equal(1, row.Issued);    // VC003
            Assert.Equal(1, row.Used);      // VC001
            Assert.Equal(1, row.Evicted);   // VC002
        }
    }
}/// <summary>Test chính sách xếp hạng thẻ: nâng hạng khi đủ ngưỡng, duy trì hạng, giữ nguyên, chặn trùng mã.</summary>
public class RankPolicyServiceTests
{
    private static (AppDbContext db, IRankPolicyService svc, SqliteConnection conn) NewSvc()
    {
        var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        var opt = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(opt, new TenantContext { OrgId = TenantContext.DefaultOrgId });
        db.Database.EnsureCreated();
        return (db, new RankPolicyService(db), conn);
    }

    // Tạo 3 hạng SILVER(1) < GOLD(2) < PLATINUM(3) đang bật.
    private static async Task Seed3(IRankPolicyService svc)
    {
        await svc.CreatePolicyAsync(new RankPolicy { Code = "RP-S", CardType = "SILVER", Value = 1, PointUpBegin = 0, QtyVisitUpBegin = 0, PointKeepBegin = 0, QtyVisitKeepBegin = 0 });
        await svc.CreatePolicyAsync(new RankPolicy { Code = "RP-G", CardType = "GOLD", Value = 2, PointUpBegin = 5_000, QtyVisitUpBegin = 5, PointKeepBegin = 2_000, QtyVisitKeepBegin = 3 });
        await svc.CreatePolicyAsync(new RankPolicy { Code = "RP-P", CardType = "PLATINUM", Value = 3, PointUpBegin = 20_000, QtyVisitUpBegin = 20, PointKeepBegin = 10_000, QtyVisitKeepBegin = 10 });
        foreach (var p in await svc.PoliciesAsync()) await svc.SetStatusAsync(p.Id, RankPolicyStatus.Active);
    }

    [Fact]
    public async Task Create_RequiresCardType()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            Assert.False((await svc.CreatePolicyAsync(new RankPolicy { CardType = "" })).ok);
        }
    }

    [Fact]
    public async Task Create_DuplicateCode_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await svc.CreatePolicyAsync(new RankPolicy { Code = "A", CardType = "SILVER" });
            var o = await svc.CreatePolicyAsync(new RankPolicy { Code = "A", CardType = "GOLD" });
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Evaluate_NoActivePolicy_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var o = await svc.EvaluateAsync("SILVER", 10_000, 10);
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Evaluate_Up_WhenThresholdsMet()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await Seed3(svc);
            // SILVER đủ ngưỡng nâng (điểm ≥ 0, lượt ≥ 0) → nâng lên hạng kế tiếp GOLD.
            var o = await svc.EvaluateAsync("SILVER", 100, 1);
            Assert.True(o.ok);
            Assert.Equal(RankActionType.Up, o.action);
            Assert.Equal("GOLD", o.cardType);
            Assert.Equal(2, o.value);
        }
    }

    [Fact]
    public async Task Evaluate_Up_ToNextHigherOnly()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await Seed3(svc);
            // GOLD đủ ngưỡng nâng → chỉ lên PLATINUM (hạng kế tiếp), không nhảy bậc.
            var o = await svc.EvaluateAsync("GOLD", 25_000, 25);
            Assert.Equal(RankActionType.Up, o.action);
            Assert.Equal("PLATINUM", o.cardType);
        }
    }

    [Fact]
    public async Task Evaluate_Keep_WhenOnlyKeepThresholdMet()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await Seed3(svc);
            // GOLD: điểm 3.000 < 5.000 (không nâng) nhưng ≥ 2.000 và lượt 3 ≥ 3 → duy trì.
            var o = await svc.EvaluateAsync("GOLD", 3_000, 3);
            Assert.True(o.ok);
            Assert.Equal(RankActionType.Keep, o.action);
            Assert.Equal("GOLD", o.cardType);
        }
    }

    [Fact]
    public async Task Evaluate_Keep_WhenBelowBoth()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await Seed3(svc);
            // GOLD: điểm 100 < 2.000 và lượt 1 < 3 → giữ nguyên (Keep, không nâng).
            var o = await svc.EvaluateAsync("GOLD", 100, 1);
            Assert.True(o.ok);
            Assert.Equal(RankActionType.Keep, o.action);
            Assert.Equal("GOLD", o.cardType);
        }
    }

    [Fact]
    public async Task Evaluate_TopRank_NoUp()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await Seed3(svc);
            // PLATINUM là hạng cao nhất → không có hạng cao hơn để nâng, chỉ duy trì.
            var o = await svc.EvaluateAsync("PLATINUM", 100_000, 100);
            Assert.True(o.ok);
            Assert.Equal(RankActionType.Keep, o.action);
            Assert.Equal("PLATINUM", o.cardType);
        }
    }

    [Fact]
    public async Task Evaluate_InactivePolicy_NotConsidered()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await Seed3(svc);
            var silver = (await svc.PoliciesAsync()).First(p => p.CardType == "SILVER");
            await svc.SetStatusAsync(silver.Id, RankPolicyStatus.Inactive);
            var o = await svc.EvaluateAsync("SILVER", 100, 1);
            Assert.False(o.ok);   // hạng hiện tại không còn chính sách đang bật
        }
    }
}
/// <summary>Test chính sách quy đổi tiền dịch vụ → điểm: chỉ chính sách đang bật + trong hiệu lực,
/// tỷ lệ theo hạng thẻ, mốc lượt xét hạng, chiết khấu, chặn trùng hạng thẻ.</summary>
public class PolicyMoneyToPointServiceTests
{
    private static (AppDbContext db, IPolicyMoneyToPointService svc, SqliteConnection conn) NewSvc()
    {
        var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        var opt = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(opt, new TenantContext { OrgId = TenantContext.DefaultOrgId });
        db.Database.EnsureCreated();
        return (db, new PolicyMoneyToPointService(db), conn);
    }

    // Tạo chính sách đang bật với 2 hạng: GOLD (1000đ→2 điểm, mốc 1.000.000, ck 5%), SILVER (1000đ→1 điểm).
    private static async Task<PolicyMoneyToPoint> ActivePolicy(IPolicyMoneyToPointService svc)
    {
        var (_, _, id) = await svc.CreatePolicyAsync(new PolicyMoneyToPoint
        {
            Code = "PMTP", Name = "Quy đổi", EffDateStart = DateTime.Today.AddDays(-1), EffDateEnd = DateTime.Today.AddDays(10)
        });
        await svc.AddDetailAsync(new PolicyMoneyToPointDtl { PolicyMoneyToPointId = id, CardType = "GOLD", ConvertValue = 1_000, ConvertPoint = 2, ValueRankCardType = 1_000_000, DiscountRate = 5 });
        await svc.AddDetailAsync(new PolicyMoneyToPointDtl { PolicyMoneyToPointId = id, CardType = "SILVER", ConvertValue = 1_000, ConvertPoint = 1, ValueRankCardType = 500_000, DiscountRate = 0 });
        await svc.SetStatusAsync(id, PolicyMoneyToPointStatus.Active);
        return (await svc.GetPolicyAsync(id))!;
    }

    [Fact]
    public async Task Create_RequiresCode_And_DuplicateRejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var a = await svc.CreatePolicyAsync(new PolicyMoneyToPoint { Code = "A" });
            Assert.True(a.ok);
            var b = await svc.CreatePolicyAsync(new PolicyMoneyToPoint { Code = "A" });
            Assert.False(b.ok);
        }
    }

    [Fact]
    public async Task Activate_WithoutDetails_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreatePolicyAsync(new PolicyMoneyToPoint { Code = "P" });
            var o = await svc.SetStatusAsync(id, PolicyMoneyToPointStatus.Active);
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task AddDetail_RequiresPositiveConvertValue()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreatePolicyAsync(new PolicyMoneyToPoint { Code = "P" });
            var o = await svc.AddDetailAsync(new PolicyMoneyToPointDtl { PolicyMoneyToPointId = id, CardType = "GOLD", ConvertValue = 0, ConvertPoint = 1 });
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task AddDetail_DuplicateCardType_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, id) = await svc.CreatePolicyAsync(new PolicyMoneyToPoint { Code = "P" });
            await svc.AddDetailAsync(new PolicyMoneyToPointDtl { PolicyMoneyToPointId = id, CardType = "GOLD", ConvertValue = 1_000, ConvertPoint = 1 });
            var o = await svc.AddDetailAsync(new PolicyMoneyToPointDtl { PolicyMoneyToPointId = id, CardType = "GOLD", ConvertValue = 2_000, ConvertPoint = 2 });
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Calc_NoActivePolicy_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var o = await svc.CalcAsync("GOLD", 1_000_000, null);
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Calc_ConvertsByRate()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await ActivePolicy(svc);
            // GOLD: 1.000đ → 2 điểm ⇒ 500.000đ → 1.000 điểm.
            var o = await svc.CalcAsync("GOLD", 500_000, null);
            Assert.True(o.ok);
            Assert.Equal(1_000m, o.point);
            Assert.Equal(5m, o.discountRate);
            Assert.Equal(0, o.qtyVisit);   // 500.000 < mốc 1.000.000
        }
    }

    [Fact]
    public async Task Calc_QtyVisit_WhenAmountReachesRankThreshold()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await ActivePolicy(svc);
            var o = await svc.CalcAsync("GOLD", 1_000_000, null);
            Assert.True(o.ok);
            Assert.Equal(1, o.qtyVisit);   // đạt mốc 1.000.000 → 1 lượt xét hạng
        }
    }

    [Fact]
    public async Task Calc_UnknownCardType_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await ActivePolicy(svc);
            var o = await svc.CalcAsync("BRONZE", 1_000_000, null);
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Calc_OutsideEffectiveWindow_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await ActivePolicy(svc);
            var o = await svc.CalcAsync("GOLD", 1_000_000, DateTime.Today.AddDays(30));
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Calc_InactivePolicy_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var p = await ActivePolicy(svc);
            await svc.SetStatusAsync(p.Id, PolicyMoneyToPointStatus.Inactive);
            var o = await svc.CalcAsync("GOLD", 1_000_000, null);
            Assert.False(o.ok);
        }
    }
}
/// <summary>Test chiết khấu hội viên (Crd_MemberDiscountTransaction): công thức chiết khấu, cờ FlagDiscount,
/// tỷ lệ hạng thẻ theo chính sách, chỉ ghi nhận khi chiết khấu > 0, đối soát theo hạng thẻ.</summary>
public class MemberDiscountServiceTests
{
    private static (AppDbContext db, IMemberDiscountService svc, SqliteConnection conn) NewSvc()
    {
        var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        var opt = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(opt, new TenantContext { OrgId = TenantContext.DefaultOrgId });
        db.Database.EnsureCreated();
        return (db, new MemberDiscountService(db), conn);
    }

    // Chính sách quy đổi đang bật: GOLD chiết khấu 5%, PLATINUM chiết khấu 10%.
    private static async Task ActivePolicy(AppDbContext db)
    {
        var p = new PolicyMoneyToPoint
        {
            Code = "PMTP", Name = "Quy đổi", EffDateStart = DateTime.Today.AddDays(-1), EffDateEnd = DateTime.Today.AddDays(10),
            Status = PolicyMoneyToPointStatus.Active
        };
        db.PolicyMoneyToPoints.Add(p); await db.SaveChangesAsync();
        db.PolicyMoneyToPointDtls.AddRange(
            new PolicyMoneyToPointDtl { PolicyMoneyToPointId = p.Id, CardType = "GOLD", ConvertValue = 1_000, ConvertPoint = 2, DiscountRate = 5 },
            new PolicyMoneyToPointDtl { PolicyMoneyToPointId = p.Id, CardType = "PLATINUM", ConvertValue = 1_000, ConvertPoint = 3, DiscountRate = 10 });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Calc_AppliesCardTypeAndPaymentRates()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await ActivePolicy(db);
            // 2.000.000 × 100% (ĐT thanh toán) × 5% (hạng GOLD) = 100.000
            var o = await svc.CalcAsync("RO1", "GOLD", new[] { new MemberDiscountLine(2_000_000, 100) }, null);
            Assert.True(o.ok);
            Assert.Equal(2_000_000, o.amountForDC);
            Assert.Equal(100_000, o.discount);
            Assert.Equal(5, o.policyDiscountRate);
        }
    }

    [Fact]
    public async Task Calc_SkipsLinesWithoutFlagDiscount()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await ActivePolicy(db);
            var o = await svc.CalcAsync("RO1", "GOLD", new[]
            {
                new MemberDiscountLine(1_000_000, 100, true),
                new MemberDiscountLine(1_000_000, 100, false)   // không tính
            }, null);
            Assert.True(o.ok);
            Assert.Equal(1_000_000, o.amountForDC);
            Assert.Equal(50_000, o.discount);
        }
    }

    [Fact]
    public async Task Calc_UnknownCardType_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await ActivePolicy(db);
            var o = await svc.CalcAsync("RO1", "BRONZE", new[] { new MemberDiscountLine(1_000_000, 100) }, null);
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Calc_NoLines_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await ActivePolicy(db);
            var o = await svc.CalcAsync("RO1", "GOLD", Array.Empty<MemberDiscountLine>(), null);
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Record_PersistsTransaction()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await ActivePolicy(db);
            var o = await svc.RecordAsync("RO1", "DLCP01", "HV001", "CARD001", "GOLD", "GOLD", "GOLD",
                new[] { new MemberDiscountLine(2_000_000, 100) }, null);
            Assert.True(o.ok);
            Assert.Equal(100_000, o.discount);
            var list = await svc.TransactionsAsync("RO1");
            Assert.Single(list);
            Assert.Equal("GOLD", list[0].CardTypeApply);
            Assert.Equal("PMTP", list[0].PolicyCode);
        }
    }

    [Fact]
    public async Task Record_ZeroDiscount_NotPersisted()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await ActivePolicy(db);
            // Tỷ lệ ĐT thanh toán 0% → chiết khấu 0 → không ghi nhận.
            var o = await svc.RecordAsync("RO1", "DLCP01", "HV001", "CARD001", "GOLD", "GOLD", "GOLD",
                new[] { new MemberDiscountLine(2_000_000, 0) }, null);
            Assert.False(o.ok);
            Assert.Empty(await svc.TransactionsAsync(null));
        }
    }

    [Fact]
    public async Task Reconciliation_GroupsByCardTypeApply()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await ActivePolicy(db);
            await svc.RecordAsync("RO1", "DLCP01", "HV001", "C1", "GOLD", "GOLD", "GOLD", new[] { new MemberDiscountLine(2_000_000, 100) }, null);
            await svc.RecordAsync("RO2", "DLCP01", "HV002", "C2", "GOLD", "GOLD", "GOLD", new[] { new MemberDiscountLine(1_000_000, 100) }, null);
            await svc.RecordAsync("RO3", "DLCP01", "HV003", "C3", "PLATINUM", "PLATINUM", "PLATINUM", new[] { new MemberDiscountLine(3_000_000, 100) }, null);

            var recon = await svc.ReconciliationAsync(null);
            Assert.Equal(2, recon.Count);
            var gold = recon.First(r => r.CardTypeApply == "GOLD");
            Assert.Equal(2, gold.Deals);
            Assert.Equal(3_000_000, gold.AmountForDC);
            Assert.Equal(150_000, gold.Discount);
            var plat = recon.First(r => r.CardTypeApply == "PLATINUM");
            Assert.Equal(300_000, plat.Discount);
        }
    }
}/// <summary>Test danh mục loại khuyến mại: chặn trùng mã, bật/tạm dừng, gắn hình thức vào loại khuyến mại theo,
/// kiểm tra hình thức hợp lệ (loại/hình thức/dòng gắn đều phải đang bật).</summary>
public class PromotionTypeServiceTests
{
    private static (AppDbContext db, IPromotionTypeService svc, SqliteConnection conn) NewSvc()
    {
        var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        var opt = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(opt, new TenantContext { OrgId = TenantContext.DefaultOrgId });
        db.Database.EnsureCreated();
        return (db, new PromotionTypeService(db), conn);
    }

    // Tạo 1 loại khuyến mại theo + 1 hình thức khuyến mại + gắn chúng lại với nhau.
    private static async Task<(int mainId, int prmId, int mapId)> SeedOne(IPromotionTypeService svc)
    {
        var (_, _, mainId) = await svc.CreateMainTypeAsync(new PromotionMainTypeDef { Code = "PRODUCT", Name = "Hàng hóa" });
        var (_, _, prmId) = await svc.CreatePrmTypeAsync(new PromotionPrmTypeDef { Code = "PRODUCTUPDC", Name = "Giảm giá hàng" });
        var (_, _, mapId) = await svc.AddMappingAsync(mainId, prmId, null);
        return (mainId, prmId, mapId);
    }

    [Fact]
    public async Task CreateMainType_RequiresName()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            Assert.False((await svc.CreateMainTypeAsync(new PromotionMainTypeDef { Name = "" })).ok);
        }
    }

    [Fact]
    public async Task CreateMainType_DuplicateCode_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await svc.CreateMainTypeAsync(new PromotionMainTypeDef { Code = "ORDER", Name = "Đơn hàng" });
            var o = await svc.CreateMainTypeAsync(new PromotionMainTypeDef { Code = "ORDER", Name = "Đơn hàng 2" });
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task CreatePrmType_DuplicateCode_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await svc.CreatePrmTypeAsync(new PromotionPrmTypeDef { Code = "VOUCHER", Name = "Tặng voucher" });
            var o = await svc.CreatePrmTypeAsync(new PromotionPrmTypeDef { Code = "VOUCHER", Name = "Tặng voucher 2" });
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task AddMapping_Duplicate_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (mainId, prmId, _) = await SeedOne(svc);
            var o = await svc.AddMappingAsync(mainId, prmId, null);
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task AddMapping_UnknownMainType_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, prmId) = await svc.CreatePrmTypeAsync(new PromotionPrmTypeDef { Code = "ORDER", Name = "Giảm giá đơn hàng" });
            var o = await svc.AddMappingAsync(999, prmId, null);
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Check_Allowed_WhenAllActive()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await SeedOne(svc);
            var o = await svc.CheckPrmInMainAsync("PRODUCT", "PRODUCTUPDC");
            Assert.True(o.ok);
        }
    }

    [Fact]
    public async Task Check_Rejected_WhenMainTypeInactive()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (mainId, _, _) = await SeedOne(svc);
            await svc.SetMainTypeActiveAsync(mainId, false);
            var o = await svc.CheckPrmInMainAsync("PRODUCT", "PRODUCTUPDC");
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Check_Rejected_WhenPrmTypeInactive()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, prmId, _) = await SeedOne(svc);
            await svc.SetPrmTypeActiveAsync(prmId, false);
            var o = await svc.CheckPrmInMainAsync("PRODUCT", "PRODUCTUPDC");
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Check_Rejected_WhenMappingInactive()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var (_, _, mapId) = await SeedOne(svc);
            await svc.SetMappingActiveAsync(mapId, false);
            var o = await svc.CheckPrmInMainAsync("PRODUCT", "PRODUCTUPDC");
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Check_Rejected_WhenNotMapped()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await svc.CreateMainTypeAsync(new PromotionMainTypeDef { Code = "ORDER", Name = "Đơn hàng" });
            await svc.CreatePrmTypeAsync(new PromotionPrmTypeDef { Code = "PRODUCTUPDC", Name = "Giảm giá hàng" });
            // Chưa gắn → không được phép dùng.
            var o = await svc.CheckPrmInMainAsync("ORDER", "PRODUCTUPDC");
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Check_Rejected_WhenUnknownCode()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await SeedOne(svc);
            Assert.False((await svc.CheckPrmInMainAsync("NOPE", "PRODUCTUPDC")).ok);
            Assert.False((await svc.CheckPrmInMainAsync("PRODUCT", "NOPE")).ok);
        }
    }
}/// <summary>Test mã giảm giá: chặn trùng mã, validate giá trị, bật/tắt, kiểm tra hợp lệ (tồn tại + đang bật +
/// còn lượt + trong hiệu lực), áp dụng trừ lượt, tính giảm theo %/số tiền, ánh xạ đại lý.</summary>
public class DiscountCodeServiceTests
{
    private static (AppDbContext db, IDiscountCodeService svc, SqliteConnection conn) NewSvc()
    {
        var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        var opt = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(opt, new TenantContext { OrgId = TenantContext.DefaultOrgId });
        db.Database.EnsureCreated();
        return (db, new DiscountCodeService(db), conn);
    }

    private static async Task<int> SeedPercent(IDiscountCodeService svc, decimal pct = 10, int qty = 5)
    {
        var (_, _, id) = await svc.CreateCodeAsync(new DiscountCode
        {
            Code = "SALE10", DiscountType = DiscountCodeType.Percent, DiscountAmount = pct, RemainQty = qty,
            EffectDateFrom = DateTime.Today.AddDays(-1), EffectDateTo = DateTime.Today.AddDays(10)
        });
        return id;
    }

    [Fact]
    public async Task Create_RequiresCode()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            Assert.False((await svc.CreateCodeAsync(new DiscountCode { Code = "", DiscountAmount = 10 })).ok);
        }
    }

    [Fact]
    public async Task Create_DuplicateCode_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await SeedPercent(svc);
            var o = await svc.CreateCodeAsync(new DiscountCode { Code = "SALE10", DiscountType = DiscountCodeType.Percent, DiscountAmount = 5, RemainQty = 1 });
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Create_PercentOver100_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var o = await svc.CreateCodeAsync(new DiscountCode { Code = "X", DiscountType = DiscountCodeType.Percent, DiscountAmount = 150, RemainQty = 1 });
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Check_Valid_WhenEnabledInRange()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await SeedPercent(svc);
            var o = await svc.CheckAsync("SALE10", 1_000_000, null);
            Assert.True(o.ok);
            Assert.Equal(100_000, o.discountAmount);   // 10% của 1.000.000
        }
    }

    [Fact]
    public async Task Check_UnknownCode_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            Assert.False((await svc.CheckAsync("NOPE", 1_000_000, null)).ok);
        }
    }

    [Fact]
    public async Task Check_Disabled_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var id = await SeedPercent(svc);
            await svc.SetEnabledAsync(id, false);
            Assert.False((await svc.CheckAsync("SALE10", 1_000_000, null)).ok);
        }
    }

    [Fact]
    public async Task Check_OutOfRange_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await SeedPercent(svc);
            // Mốc ngày sau khi hết hiệu lực.
            Assert.False((await svc.CheckAsync("SALE10", 1_000_000, DateTime.Today.AddDays(30))).ok);
        }
    }

    [Fact]
    public async Task Check_NoRemainQty_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await SeedPercent(svc, qty: 0);
            Assert.False((await svc.CheckAsync("SALE10", 1_000_000, null)).ok);
        }
    }

    [Fact]
    public async Task Apply_Absolute_CapsAtOrderAmount()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await svc.CreateCodeAsync(new DiscountCode
            {
                Code = "GIAM50K", DiscountType = DiscountCodeType.Absolute, DiscountAmount = 50_000, RemainQty = 3,
                EffectDateFrom = DateTime.Today.AddDays(-1), EffectDateTo = DateTime.Today.AddDays(10)
            });
            var o = await svc.ApplyAsync("GIAM50K", 30_000, null);
            Assert.True(o.ok);
            Assert.Equal(30_000, o.discount);      // không vượt giá trị đơn
            Assert.Equal(0, o.payable);
        }
    }

    [Fact]
    public async Task Apply_DecrementsRemainQty()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await SeedPercent(svc, qty: 2);
            var o1 = await svc.ApplyAsync("SALE10", 1_000_000, null);
            Assert.True(o1.ok);
            Assert.Equal(1, o1.remainQty);
            var o2 = await svc.ApplyAsync("SALE10", 1_000_000, null);
            Assert.Equal(0, o2.remainQty);
            // Hết lượt → lần sau bị từ chối.
            Assert.False((await svc.ApplyAsync("SALE10", 1_000_000, null)).ok);
        }
    }

    [Fact]
    public async Task AddMap_Duplicate_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await svc.AddMapAsync(new DealerDiscountMap { DealerCode = "DLCP01", DiscountCode = "SALE10" });
            var o = await svc.AddMapAsync(new DealerDiscountMap { DealerCode = "DLCP01", DiscountCode = "SALE10" });
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Maps_FilterByDealer()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await svc.AddMapAsync(new DealerDiscountMap { DealerCode = "DLCP01", DiscountCode = "SALE10" });
            await svc.AddMapAsync(new DealerDiscountMap { DealerCode = "DLCP02", DiscountCode = "SALE10" });
            Assert.Single(await svc.MapsAsync("DLCP01"));
            Assert.Equal(2, (await svc.MapsAsync(null)).Count);
        }
    }
}/// <summary>Test sinh mã voucher: định dạng base36 + checksum, số thứ tự tăng dần, validate mã hợp lệ/sai checksum.</summary>
public class VoucherIdServiceTests
{
    private static (AppDbContext db, IVoucherIdService svc, SqliteConnection conn) NewSvc()
    {
        var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        var opt = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(opt, new TenantContext { OrgId = TenantContext.DefaultOrgId });
        db.Database.EnsureCreated();
        return (db, new VoucherIdService(db), conn);
    }

    [Fact]
    public async Task Generate_ReturnsRequestedAmount()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var o = await svc.GenerateAsync(3, new DateTime(2026, 9, 24));
            Assert.True(o.ok);
            Assert.Equal(3, o.codes.Count);
            Assert.Equal(3, o.lastSeq);
        }
    }

    [Fact]
    public async Task Generate_CodeIs10Chars_AndValid()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var o = await svc.GenerateAsync(1, new DateTime(2026, 9, 24));
            var code = o.codes[0];
            Assert.Equal(12, code.Length);
            Assert.True(svc.Validate(code).ok);
        }
    }

    [Fact]
    public async Task Generate_SeqIncrements_AcrossCalls()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await svc.GenerateAsync(2, new DateTime(2026, 9, 24));
            var o2 = await svc.GenerateAsync(1, new DateTime(2026, 9, 24));
            Assert.Equal(3, o2.lastSeq);
            Assert.Equal(3, (await svc.SequencesAsync()).Count);
        }
    }

    [Fact]
    public async Task Generate_ZeroAmount_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            Assert.False((await svc.GenerateAsync(0, null)).ok);
        }
    }

    [Fact]
    public async Task Generate_TooMany_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            Assert.False((await svc.GenerateAsync(1001, null)).ok);
        }
    }

    [Fact]
    public async Task Validate_WrongChecksum_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var o = await svc.GenerateAsync(1, new DateTime(2026, 9, 24));
            var code = o.codes[0];
            // Đổi ký tự checksum cuối → sai.
            var bad = code[..11] + (code[11] == 'Z' ? 'Y' : 'Z');
            Assert.False(svc.Validate(bad).ok);
        }
    }

    [Fact]
    public void Validate_WrongLength_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            Assert.False(svc.Validate("ABC").ok);
            Assert.False(svc.Validate("").ok);
        }
    }

    [Fact]
    public async Task Generate_SameDay_SameSeq_ProducesSameCode()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var day = new DateTime(2026, 9, 24);
            var a = await svc.GenerateAsync(1, day);
            // Xoá để seq quay lại 0 rồi sinh lại cùng mốc ngày → mã giống nhau (hàm thuần theo seq+ngày).
            db.VoucherIdSequences.RemoveRange(db.VoucherIdSequences);
            await db.SaveChangesAsync();
            var b = await svc.GenerateAsync(1, day);
            Assert.Equal(a.codes[0], b.codes[0]);
        }
    }
}/// <summary>Test tặng điểm giới thiệu: cộng điểm cho người giới thiệu, chống trùng theo hội viên mới, quy đổi tiền, hạn dùng, đối soát.</summary>
public class IntroductionGrantServiceTests
{
    private static (AppDbContext db, IIntroductionGrantService svc, SqliteConnection conn) NewSvc()
    {
        var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        var opt = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(opt, new TenantContext { OrgId = TenantContext.DefaultOrgId });
        db.Database.EnsureCreated();
        return (db, new IntroductionGrantService(db), conn);
    }

    [Fact]
    public async Task Grant_AwardsReferrer_NotNewMember()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var o = await svc.GrantAsync("HV100", "HV001", "CARD001", "GOLD", "GOLD", "DLCP01", 1_000, 1_000, null);
            Assert.True(o.ok);
            Assert.Equal("HV001", o.memberNo);        // người ĐƯỢC thưởng = người giới thiệu
            Assert.Equal("HV100", o.newMemberNo);
            Assert.Equal(1_000, o.point);
            Assert.Equal(1_000_000, o.amount);        // 1.000 điểm × 1.000
        }
    }

    [Fact]
    public async Task Grant_SameNewMember_Twice_Blocked()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            Assert.True((await svc.GrantAsync("HV100", "HV001", "", "", "", "", 500, 1_000, null)).ok);
            var o2 = await svc.GrantAsync("HV100", "HV002", "", "", "", "", 500, 1_000, null);
            Assert.False(o2.ok);  // mỗi hội viên mới chỉ thưởng 1 lần
        }
    }

    [Fact]
    public async Task Grant_SelfReferral_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var o = await svc.GrantAsync("HV001", "HV001", "", "", "", "", 500, 1_000, null);
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Grant_NoReferrer_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var o = await svc.GrantAsync("HV100", "", "", "", "", "", 500, 1_000, null);
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Grant_ZeroPoint_Rejected()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var o = await svc.GrantAsync("HV100", "HV001", "", "", "", "", 0, 1_000, null);
            Assert.False(o.ok);
        }
    }

    [Fact]
    public async Task Grant_Expiry_IsEndOfNextYearDecember()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var at = new DateTime(2026, 9, 24);
            var o = await svc.GrantAsync("HV100", "HV001", "", "", "", "", 500, 1_000, at);
            Assert.True(o.ok);
            Assert.Equal(new DateTime(2027, 12, 31, 23, 59, 59), o.pointExpiryDTime);
        }
    }

    [Fact]
    public async Task Grant_DefaultParamValue_WhenZero()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var o = await svc.GrantAsync("HV100", "HV001", "", "", "", "", 500, 0, null);
            Assert.True(o.ok);
            Assert.Equal(500, o.amount);   // paramValue <= 0 → mặc định 1
        }
    }

    [Fact]
    public async Task Reconciliation_GroupsByReferrer()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await svc.GrantAsync("HV100", "HV001", "", "", "", "", 1_000, 1_000, null);
            await svc.GrantAsync("HV101", "HV001", "", "", "", "", 500, 1_000, null);
            await svc.GrantAsync("HV102", "HV002", "", "", "", "", 300, 1_000, null);
            var rows = await svc.ReconciliationAsync(null);
            var hv001 = rows.First(r => r.MemberNo == "HV001");
            Assert.Equal(2, hv001.Granted);
            Assert.Equal(1_500, hv001.PointGranted);
            Assert.Equal(1_500_000, hv001.AmountGranted);
        }
    }

    [Fact]
    public async Task Grants_FilterByMember()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            await svc.GrantAsync("HV100", "HV001", "", "", "", "", 1_000, 1_000, null);
            await svc.GrantAsync("HV102", "HV002", "", "", "", "", 300, 1_000, null);
            var rows = await svc.GrantsAsync("HV002");
            Assert.Single(rows);
            Assert.Equal("HV002", rows[0].MemberNo);
        }
    }
}