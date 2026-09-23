using Microsoft.EntityFrameworkCore;
using MiniPromo.Data;
using MiniPromo.Models;

namespace MiniPromo.Services;

// Kết quả tặng điểm giới thiệu cho người giới thiệu.
public record IntroductionGrantOutcome(bool ok, string msg, string memberNo, string newMemberNo, decimal point, decimal amount, DateTime pointExpiryDTime);

// Một dòng đối soát điểm giới thiệu theo hội viên được thưởng.
public record IntroductionReconRow(string MemberNo, int Granted, decimal PointGranted, decimal AmountGranted);

public interface IIntroductionGrantService
{
    // Danh sách nhật ký tặng điểm giới thiệu (mới nhất trước), lọc theo hội viên được thưởng.
    Task<List<IntroductionGrant>> GrantsAsync(string? memberNo);

    // Tặng điểm giới thiệu: cộng PointIntro điểm cho NGƯỜI GIỚI THIỆU của hội viên mới.
    // Idempotent: mỗi hội viên mới chỉ thưởng 1 lần (chống trùng theo NewMemberNo).
    Task<IntroductionGrantOutcome> GrantAsync(string newMemberNo, string referrerMemberNo, string cardNo,
        string cardTypeUse, string cardTypeInit, string dealerCode, decimal pointIntro, decimal paramValue, DateTime? at);

    // Đối soát điểm/tiền đã tặng theo hội viên được thưởng.
    Task<List<IntroductionReconRow>> ReconciliationAsync(string? memberNo);
}

/// <summary>
/// Nghiệp vụ tặng điểm giới thiệu (port từ Crd_Member_PerformIntroX trong Transaction.AddPoint.cs
/// + bảng Crd_CardTransaction với DealPointType = 'INTRODUCTION').
/// Quy tắc nguồn: khi một hội viên mới hoàn tất đăng ký và có khai báo người giới thiệu (MemberNoIntro)
/// kèm số điểm thưởng (PointIntro), hệ thống cộng PointIntro điểm cho NGƯỜI GIỚI THIỆU (không phải
/// hội viên mới). Điểm quy đổi ra tiền theo tỷ lệ UNITPOINTTOMONEY (Mst_ParamSys.ParamValue) và có
/// hạn dùng tới cuối tháng 12 năm kế tiếp (PointExpiryDTime). Mỗi hội viên mới chỉ được thưởng 1 lần.
/// </summary>
public class IntroductionGrantService(AppDbContext db) : IIntroductionGrantService
{
    public Task<List<IntroductionGrant>> GrantsAsync(string? memberNo)
    {
        var q = db.IntroductionGrants.AsQueryable();
        if (!string.IsNullOrWhiteSpace(memberNo)) q = q.Where(g => g.MemberNo == memberNo.Trim().ToUpper());
        return q.OrderByDescending(g => g.Id).ToListAsync();
    }

    public async Task<IntroductionGrantOutcome> GrantAsync(string newMemberNo, string referrerMemberNo, string cardNo,
        string cardTypeUse, string cardTypeInit, string dealerCode, decimal pointIntro, decimal paramValue, DateTime? at)
    {
        if (string.IsNullOrWhiteSpace(newMemberNo)) return Fail("Cần mã hội viên mới.");
        if (string.IsNullOrWhiteSpace(referrerMemberNo)) return Fail("Hội viên mới không khai báo người giới thiệu.");
        if (pointIntro <= 0) return Fail("Không có điểm thưởng giới thiệu.");

        newMemberNo = newMemberNo.Trim().ToUpper();
        referrerMemberNo = referrerMemberNo.Trim().ToUpper();
        if (newMemberNo == referrerMemberNo) return Fail("Không thể tự giới thiệu chính mình.");

        // Mỗi hội viên mới chỉ được thưởng điểm giới thiệu 1 lần (chống trùng theo NewMemberNo).
        if (await db.IntroductionGrants.AnyAsync(g => g.NewMemberNo == newMemberNo))
            return Fail("Đã thưởng điểm giới thiệu cho hội viên này rồi.");

        var dtime = at ?? DateTime.UtcNow;
        var rate = paramValue <= 0 ? 1 : paramValue;
        var expiry = EndMonthOfNextYear(dtime);

        // Số giao dịch theo nguồn là INT.yyyyMMdd.HHmmss; nếu trùng (nhiều lượt trong cùng giây) thì thêm hậu tố.
        var refNo = "INT." + dtime.ToString("yyyyMMdd.HHmmss");
        var suffix = 0;
        while (await db.IntroductionGrants.AnyAsync(g => g.RefNo == refNo))
            refNo = "INT." + dtime.ToString("yyyyMMdd.HHmmss") + "." + (++suffix);

        var grant = new IntroductionGrant
        {
            RefNo = refNo,
            MemberNo = referrerMemberNo,          // người ĐƯỢC thưởng = người giới thiệu
            NewMemberNo = newMemberNo,            // hội viên mới (người được giới thiệu)
            CardNo = (cardNo ?? "").Trim().ToUpper(),
            CardTypeUse = (cardTypeUse ?? "").Trim().ToUpper(),
            CardTypeInit = (cardTypeInit ?? "").Trim().ToUpper(),
            DealerCode = (dealerCode ?? "").Trim().ToUpper(),
            DealPointType = IntroductionPointType.Introduction,
            PointChTotal = pointIntro,
            AmountChTotal = pointIntro * rate,
            ParamValue = rate,
            PointExpiryDTime = expiry,
            CreateDate = dtime.Date
        };
        db.IntroductionGrants.Add(grant);
        await db.SaveChangesAsync();

        return new(true, $"Đã thưởng {pointIntro:N0} điểm giới thiệu cho hội viên {referrerMemberNo}.",
            referrerMemberNo, newMemberNo, pointIntro, grant.AmountChTotal, expiry);
    }

    public async Task<List<IntroductionReconRow>> ReconciliationAsync(string? memberNo)
    {
        var q = db.IntroductionGrants.AsQueryable();
        if (!string.IsNullOrWhiteSpace(memberNo)) q = q.Where(g => g.MemberNo == memberNo.Trim().ToUpper());
        var rows = await q.ToListAsync();
        return rows
            .GroupBy(g => g.MemberNo)
            .Select(g => new IntroductionReconRow(
                g.Key, g.Count(), g.Sum(x => x.PointChTotal), g.Sum(x => x.AmountChTotal)))
            .OrderByDescending(r => r.PointGranted)
            .ToList();
    }

    // Hạn dùng điểm: cuối tháng 12 năm kế tiếp (nguồn StdDateEndMonthOfNextYear).
    private static DateTime EndMonthOfNextYear(DateTime dtime) =>
        new DateTime(dtime.Year + 1, 12, 31, 23, 59, 59);

    private static IntroductionGrantOutcome Fail(string msg) =>
        new(false, msg, "", "", 0, 0, DateTime.Today);
}