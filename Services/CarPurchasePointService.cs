using Microsoft.EntityFrameworkCore;
using MiniPromo.Data;
using MiniPromo.Models;

namespace MiniPromo.Services;

// Kết quả tặng điểm mua xe mới cho một hội viên.
public record CarPurchasePointOutcome(bool ok, string msg, string memberNo, string cardNo, decimal point, decimal amount, DateTime pointExpiryDTime);

// Một dòng đối soát điểm mua xe mới theo hội viên.
public record CarPurchaseReconRow(string MemberNo, int Granted, decimal PointGranted, decimal AmountGranted);

public interface ICarPurchasePointService
{
    // Danh sách nhật ký tặng điểm mua xe mới (mới nhất trước), lọc theo hội viên.
    Task<List<CarPurchasePointGrant>> GrantsAsync(string? memberNo);

    // Tặng điểm mua xe mới: cộng điểm mua xe (pointBuyCar) vào thẻ đang APPROVE của hội viên,
    // quy đổi ra tiền theo tỷ lệ paramValue (UNITPOINTTOMONEY); hạn dùng = cuối tháng 12 năm kế tiếp.
    Task<CarPurchasePointOutcome> GrantAsync(string memberNo, string cardNo, string cardTypeUse, string cardTypeInit,
        string dealerCode, string? prProgramCode, decimal pointBuyCar, decimal paramValue, DateTime? at);

    // Đối soát điểm/tiền đã tặng theo hội viên.
    Task<List<CarPurchaseReconRow>> ReconciliationAsync(string? memberNo);
}

/// <summary>
/// Nghiệp vụ tặng điểm mua xe mới (port từ Crd_Member_PerformBuyNewCarX trong Transaction.AddPoint.cs
/// + bảng Crd_CardTransaction với DealPointType = 'SALES').
/// Quy tắc nguồn: khi hội viên mua xe mới, hệ thống cộng số điểm mua xe (PointBuyCar trên Crd_Member)
/// vào thẻ đang APPROVE của hội viên (Crd_Card.CardStatus = 'APPROVE'). Điểm quy đổi ra tiền theo tỷ lệ
/// UNITPOINTTOMONEY (Mst_ParamSys.ParamValue): AmountChTotal = PointBuyCar × ParamValue. Điểm có hạn dùng
/// tới cuối tháng 12 năm kế tiếp (PointExpiryDTime — nguồn StdDateEndMonthOfNextYear).
/// Số giao dịch theo nguồn là 'NEW.yyyyMMdd.HHmmss'.
/// </summary>
public class CarPurchasePointService(AppDbContext db) : ICarPurchasePointService
{
    public Task<List<CarPurchasePointGrant>> GrantsAsync(string? memberNo)
    {
        var q = db.CarPurchasePointGrants.AsQueryable();
        if (!string.IsNullOrWhiteSpace(memberNo)) q = q.Where(g => g.MemberNo == memberNo.Trim().ToUpper());
        return q.OrderByDescending(g => g.Id).ToListAsync();
    }

    public async Task<CarPurchasePointOutcome> GrantAsync(string memberNo, string cardNo, string cardTypeUse, string cardTypeInit,
        string dealerCode, string? prProgramCode, decimal pointBuyCar, decimal paramValue, DateTime? at)
    {
        if (string.IsNullOrWhiteSpace(memberNo)) return Fail("Cần mã hội viên.");
        if (pointBuyCar <= 0) return Fail("Không có điểm mua xe để tặng.");

        memberNo = memberNo.Trim().ToUpper();
        var dtime = at ?? DateTime.UtcNow;
        var rate = paramValue <= 0 ? 1 : paramValue;
        var expiry = EndMonthOfNextYear(dtime);

        // Số giao dịch theo nguồn là NEW.yyyyMMdd.HHmmss; nếu trùng (nhiều lượt trong cùng giây) thì thêm hậu tố.
        var refNo = "NEW." + dtime.ToString("yyyyMMdd.HHmmss");
        var suffix = 0;
        while (await db.CarPurchasePointGrants.AnyAsync(g => g.RefNo == refNo))
            refNo = "NEW." + dtime.ToString("yyyyMMdd.HHmmss") + "." + (++suffix);

        var grant = new CarPurchasePointGrant
        {
            RefNo = refNo,
            MemberNo = memberNo,
            CardNo = (cardNo ?? "").Trim().ToUpper(),
            CardTypeUse = (cardTypeUse ?? "").Trim().ToUpper(),
            CardTypeInit = (cardTypeInit ?? "").Trim().ToUpper(),
            DealerCode = (dealerCode ?? "").Trim().ToUpper(),
            DealPointType = CarPurchasePointType.Sales,
            PrProgramCode = string.IsNullOrWhiteSpace(prProgramCode) ? null : prProgramCode.Trim().ToUpper(),
            PointChTotal = pointBuyCar,
            AmountChTotal = pointBuyCar * rate,
            ParamValue = rate,
            PointExpiryDTime = expiry,
            CreateDate = dtime.Date
        };
        db.CarPurchasePointGrants.Add(grant);
        await db.SaveChangesAsync();

        return new(true, $"Đã tặng {pointBuyCar:N0} điểm mua xe mới cho hội viên {memberNo}.",
            memberNo, grant.CardNo, pointBuyCar, grant.AmountChTotal, expiry);
    }

    public async Task<List<CarPurchaseReconRow>> ReconciliationAsync(string? memberNo)
    {
        var q = db.CarPurchasePointGrants.AsQueryable();
        if (!string.IsNullOrWhiteSpace(memberNo)) q = q.Where(g => g.MemberNo == memberNo.Trim().ToUpper());
        var rows = await q.ToListAsync();
        return rows
            .GroupBy(g => g.MemberNo)
            .Select(g => new CarPurchaseReconRow(
                g.Key, g.Count(), g.Sum(x => x.PointChTotal), g.Sum(x => x.AmountChTotal)))
            .OrderByDescending(r => r.PointGranted)
            .ToList();
    }

    // Hạn dùng điểm: cuối tháng 12 năm kế tiếp (nguồn StdDateEndMonthOfNextYear).
    private static DateTime EndMonthOfNextYear(DateTime dtime) =>
        new DateTime(dtime.Year + 1, 12, 31, 23, 59, 59);

    private static CarPurchasePointOutcome Fail(string msg) =>
        new(false, msg, "", "", 0, 0, DateTime.Today);
}