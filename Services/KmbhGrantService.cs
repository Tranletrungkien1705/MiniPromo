using Microsoft.EntityFrameworkCore;
using MiniPromo.Data;
using MiniPromo.Models;

namespace MiniPromo.Services;

// Kết quả tặng điểm khuyến mại bán hàng (HTV) cho một hội viên.
public record KmbhGrantOutcome(bool ok, string msg, string memberNo, string cardNo, decimal point, decimal amount, DateTime pointExpiryDTime);

// Một dòng đối soát điểm khuyến mại bán hàng theo hội viên.
public record KmbhReconRow(string MemberNo, int Granted, decimal PointGranted, decimal AmountGranted);

public interface IKmbhGrantService
{
    // Danh sách nhật ký tặng điểm khuyến mại bán hàng (mới nhất trước), lọc theo hội viên.
    Task<List<KmbhGrant>> GrantsAsync(string? memberNo);

    // Tặng điểm khuyến mại bán hàng: cộng điểm khuyến mại (pointBuyCreta) vào thẻ đang APPROVE của hội viên,
    // quy đổi ra tiền theo tỷ lệ paramValue (UNITPOINTTOMONEY); hạn dùng = cuối tháng 12 năm kế tiếp.
    // Chỉ tặng khi hạng thẻ sử dụng KHÁC hạng thẻ gốc (đặc cách) — theo nguồn Crd_Member_PerformBuyCretaX.
    Task<KmbhGrantOutcome> GrantAsync(string memberNo, string cardNo, string cardTypeUse, string cardTypeInit,
        decimal pointBuyCreta, decimal paramValue, DateTime? at);

    // Đối soát điểm/tiền đã tặng theo hội viên.
    Task<List<KmbhReconRow>> ReconciliationAsync(string? memberNo);
}

/// <summary>
/// Nghiệp vụ tặng điểm khuyến mại bán hàng (HTV) — port từ Crd_Member_PerformBuyCretaX trong
/// Transaction.AddPoint.cs + bảng Crd_CardTransaction với DealPointType = 'KMBH'.
/// Quy tắc nguồn: khi hội viên mua xe trong chương trình khuyến mại của HTV, hệ thống cộng số điểm
/// khuyến mại (PointBuyCreta trên Crd_Member) vào thẻ đang APPROVE của hội viên (Crd_Card.CardStatus = 'APPROVE').
/// Điểm quy đổi ra tiền theo tỷ lệ UNITPOINTTOMONEY (Mst_ParamSys.ParamValue): AmountChTotal = PointBuyCreta × ParamValue.
/// Điểm có hạn dùng tới cuối tháng 12 năm kế tiếp (PointExpiryDTime — nguồn StdDateEndMonthOfNextYear).
/// Đại lý ghi nhận là 'HTV' (điểm từ nhà sản xuất). Số giao dịch theo nguồn là 'KMBH.yyyyMMdd.HHmmss'.
/// Nguồn chỉ gọi khi hạng thẻ sử dụng khác hạng thẻ gốc (đặc cách) — điều kiện này được kiểm tra ở đây.
/// </summary>
public class KmbhGrantService(AppDbContext db) : IKmbhGrantService
{
    public Task<List<KmbhGrant>> GrantsAsync(string? memberNo)
    {
        var q = db.KmbhGrants.AsQueryable();
        if (!string.IsNullOrWhiteSpace(memberNo)) q = q.Where(g => g.MemberNo == memberNo.Trim().ToUpper());
        return q.OrderByDescending(g => g.Id).ToListAsync();
    }

    public async Task<KmbhGrantOutcome> GrantAsync(string memberNo, string cardNo, string cardTypeUse, string cardTypeInit,
        decimal pointBuyCreta, decimal paramValue, DateTime? at)
    {
        if (string.IsNullOrWhiteSpace(memberNo)) return Fail("Cần mã hội viên.");
        if (pointBuyCreta <= 0) return Fail("Không có điểm khuyến mại bán hàng để tặng.");

        memberNo = memberNo.Trim().ToUpper();
        var use = (cardTypeUse ?? "").Trim().ToUpper();
        var init = (cardTypeInit ?? "").Trim().ToUpper();

        // Nguồn: chỉ tặng điểm khuyến mại bán hàng khi hạng thẻ sử dụng KHÁC hạng thẻ gốc (đặc cách).
        if (string.IsNullOrWhiteSpace(use) || string.IsNullOrWhiteSpace(init) || use == init)
            return Fail("Chỉ tặng điểm khuyến mại bán hàng khi hạng thẻ sử dụng khác hạng thẻ gốc.");

        var dtime = at ?? DateTime.UtcNow;
        var rate = paramValue <= 0 ? 1 : paramValue;
        var expiry = EndMonthOfNextYear(dtime);

        // Số giao dịch theo nguồn là KMBH.yyyyMMdd.HHmmss; nếu trùng (nhiều lượt trong cùng giây) thì thêm hậu tố.
        var refNo = "KMBH." + dtime.ToString("yyyyMMdd.HHmmss");
        var suffix = 0;
        while (await db.KmbhGrants.AnyAsync(g => g.RefNo == refNo))
            refNo = "KMBH." + dtime.ToString("yyyyMMdd.HHmmss") + "." + (++suffix);

        var grant = new KmbhGrant
        {
            RefNo = refNo,
            MemberNo = memberNo,
            CardNo = (cardNo ?? "").Trim().ToUpper(),
            CardTypeUse = use,
            CardTypeInit = init,
            DealerCode = "HTV",                       // Nguồn hardcode DLCode = 'HTV'
            DealPointType = KmbhPointType.Kmbh,
            PointChTotal = pointBuyCreta,
            AmountChTotal = pointBuyCreta * rate,
            ParamValue = rate,
            PointExpiryDTime = expiry,
            CreateDate = dtime.Date
        };
        db.KmbhGrants.Add(grant);
        await db.SaveChangesAsync();

        return new(true, $"Đã tặng {pointBuyCreta:N0} điểm khuyến mại bán hàng (HTV) cho hội viên {memberNo}.",
            memberNo, grant.CardNo, pointBuyCreta, grant.AmountChTotal, expiry);
    }

    public async Task<List<KmbhReconRow>> ReconciliationAsync(string? memberNo)
    {
        var q = db.KmbhGrants.AsQueryable();
        if (!string.IsNullOrWhiteSpace(memberNo)) q = q.Where(g => g.MemberNo == memberNo.Trim().ToUpper());
        var rows = await q.ToListAsync();
        return rows
            .GroupBy(g => g.MemberNo)
            .Select(g => new KmbhReconRow(
                g.Key, g.Count(), g.Sum(x => x.PointChTotal), g.Sum(x => x.AmountChTotal)))
            .OrderByDescending(r => r.PointGranted)
            .ToList();
    }

    // Hạn dùng điểm: cuối tháng 12 năm kế tiếp (nguồn StdDateEndMonthOfNextYear).
    private static DateTime EndMonthOfNextYear(DateTime dtime) =>
        new DateTime(dtime.Year + 1, 12, 31, 23, 59, 59);

    private static KmbhGrantOutcome Fail(string msg) =>
        new(false, msg, "", "", 0, 0, DateTime.Today);
}
