using Microsoft.EntityFrameworkCore;
using MiniPromo.Data;
using MiniPromo.Models;

namespace MiniPromo.Services;

// Kết quả kiểm tra một hội viên có đủ điều kiện nhận voucher sinh nhật hay không.
public record BirthdayVoucherEligibility(bool ok, string msg, decimal point, int expireDays, string cardType);

// Kết quả phát voucher sinh nhật cho một hội viên.
public record BirthdayVoucherOutcome(bool ok, string msg, string voucherNo, decimal point, DateTime expireDate, string cardType);

// Kết quả đối soát voucher sinh nhật đã phát theo chương trình + loại thẻ.
public record BirthdayVoucherReconRow(int BirthdayPolicyId, string PolicyCode, string PolicyName,
    string CardType, int Issued, decimal PointIssued, decimal PointRemain);

public interface IBirthdayVoucherService
{
    Task<List<BirthdayVoucher>> VouchersAsync(string? memberNo);
    Task<BirthdayVoucher?> GetVoucherAsync(int id);
    // Kiểm tra một hội viên có đủ điều kiện nhận voucher sinh nhật vào ngày at hay không.
    Task<BirthdayVoucherEligibility> CheckEligibilityAsync(string memberNo, string cardType, DateTime? dateOfBirth, DateTime? at);
    // Phát voucher sinh nhật cho một hội viên (tạo BirthdayVoucher) — chặn phát trùng trong năm.
    Task<BirthdayVoucherOutcome> IssueAsync(string memberNo, string cardNo, string cardType, DateTime? dateOfBirth, DateTime? at);
    // Đối soát voucher sinh nhật đã phát theo chương trình + loại thẻ.
    Task<List<BirthdayVoucherReconRow>> ReconciliationAsync(int? policyId);
}

/// <summary>
/// Nghiệp vụ phát voucher sinh nhật (port từ Crd_MemberVoucher + Crd_MemberVoucherTransaction,
/// logic Crd_Member_PerformVCBirhday trong Transaction.Birthday.cs — nâng cấp 20260518).
/// Quy tắc: chương trình sinh nhật đang bật + trong khoảng hiệu lực + có cờ FlagVoucher; hội viên có
/// ngày sinh trùng ngày xét (theo tháng-ngày); hạng thẻ có cấu hình voucher (VoucherValue > 0);
/// mỗi hội viên chỉ được phát 1 voucher/năm (mã 'BV.YYYY.{MemberNo}' đảm bảo idempotent).
/// Voucher do đại lý SUPPORT phát hành, hạn dùng = ngày xét + VoucherExpireDays.
/// </summary>
public class BirthdayVoucherService(AppDbContext db) : IBirthdayVoucherService
{
    public Task<List<BirthdayVoucher>> VouchersAsync(string? memberNo) =>
        db.BirthdayVouchers
            .Where(v => memberNo == null || v.MemberNo == memberNo)
            .OrderByDescending(v => v.Id).ToListAsync();

    public Task<BirthdayVoucher?> GetVoucherAsync(int id) =>
        db.BirthdayVouchers.FirstOrDefaultAsync(v => v.Id == id);

    // Chương trình sinh nhật đang hiệu lực (đang bật + trong khoảng thời gian + có phát voucher).
    private Task<BirthdayPolicy?> ActiveVoucherPolicyAsync()
    {
        var today = DateTime.Today;
        return db.BirthdayPolicies.Include(p => p.Details)
            .Where(p => p.Status == BirthdayPolicyStatus.Active && p.FlagVoucher
                && p.EffDateStart <= today && p.EffDateEnd >= today)
            .OrderBy(p => p.EffDateStart).FirstOrDefaultAsync();
    }

    // Kiểm tra một hội viên có đủ điều kiện nhận voucher sinh nhật — port từ Crd_Member_PerformVCBirhday.
    public async Task<BirthdayVoucherEligibility> CheckEligibilityAsync(string memberNo, string cardType, DateTime? dateOfBirth, DateTime? at)
    {
        if (string.IsNullOrWhiteSpace(memberNo)) return new(false, "Cần mã hội viên.", 0, 0, "");
        if (dateOfBirth == null) return new(false, "Cần ngày sinh hội viên.", 0, 0, "");
        var day = (at ?? DateTime.Today).Date;

        var p = await ActiveVoucherPolicyAsync();
        if (p == null) return new(false, "Không có chương trình phát voucher sinh nhật đang hiệu lực.", 0, 0, "");

        // Ngày sinh phải trùng tháng-ngày với ngày xét.
        if (dateOfBirth.Value.Month != day.Month || dateOfBirth.Value.Day != day.Day)
            return new(false, "Hôm nay không phải sinh nhật của hội viên.", 0, 0, "");

        var code = (cardType ?? "").Trim().ToUpper();
        var dtl = p.Details.FirstOrDefault(x => x.CardType == code);
        if (dtl == null) return new(false, $"Hạng thẻ {code} không thuộc chương trình.", 0, 0, "");
        if (dtl.VoucherValue <= 0) return new(false, $"Hạng thẻ {code} không được cấu hình phát voucher.", 0, 0, "");

        // Mỗi hội viên chỉ được phát 1 voucher/năm — mã 'BV.YYYY.{MemberNo}' đảm bảo idempotent.
        var voucherNo = $"BV.{day.Year}.{memberNo.Trim()}";
        if (await db.BirthdayVouchers.IgnoreQueryFilters().AnyAsync(v => v.VoucherNo == voucherNo))
            return new(false, "Hội viên đã nhận voucher sinh nhật trong năm nay.", 0, 0, "");

        return new(true, "Đủ điều kiện nhận voucher sinh nhật.", dtl.VoucherValue, dtl.VoucherExpireDays, code);
    }

    // Phát voucher sinh nhật cho một hội viên — port từ Crd_Member_PerformVCBirhday.
    public async Task<BirthdayVoucherOutcome> IssueAsync(string memberNo, string cardNo, string cardType, DateTime? dateOfBirth, DateTime? at)
    {
        var check = await CheckEligibilityAsync(memberNo, cardType, dateOfBirth, at);
        if (!check.ok) return new(false, check.msg, "", 0, default, "");

        var day = (at ?? DateTime.Today).Date;
        var p = await ActiveVoucherPolicyAsync();
        var expire = day.AddDays(check.expireDays);
        var voucherNo = $"BV.{day.Year}.{memberNo.Trim()}";
        var refNo = $"VCSN.{day:yyyyMMdd}.{DateTime.UtcNow:HHmmss}";

        db.BirthdayVouchers.Add(new BirthdayVoucher
        {
            VoucherNo = voucherNo, RefNo = refNo,
            MemberNo = memberNo.Trim(), CardNo = (cardNo ?? "").Trim(),
            CardTypeUse = check.cardType, CardTypeInit = check.cardType,
            DealerCode = "SUPPORT", BirthdayPolicyId = p!.Id,
            PointVCTotal = check.point, PointVCRemain = check.point, PointVCLimit = check.point,
            QtyUseVCLimit = 1, QtyUseVCRemain = 1,
            PointExpiryDate = expire, CreateDate = day,
            Remark = $"Voucher sinh nhật {day.Year} do đại lý SUPPORT phát hành."
        });
        await db.SaveChangesAsync();
        return new(true, "Đã phát voucher sinh nhật.", voucherNo, check.point, expire, check.cardType);
    }

    // Đối soát voucher sinh nhật đã phát theo chương trình + loại thẻ.
    public async Task<List<BirthdayVoucherReconRow>> ReconciliationAsync(int? policyId)
    {
        var policies = await db.BirthdayPolicies.Include(p => p.Details)
            .Where(p => policyId == null || p.Id == policyId)
            .OrderByDescending(p => p.Id).ToListAsync();

        var rows = new List<BirthdayVoucherReconRow>();
        foreach (var p in policies)
        {
            // SQLite không hỗ trợ SUM trên decimal → gom nhóm phía client.
            var vouchers = await db.BirthdayVouchers
                .Where(v => v.BirthdayPolicyId == p.Id)
                .Select(v => new { v.CardTypeUse, v.PointVCTotal, v.PointVCRemain })
                .ToListAsync();
            foreach (var d in p.Details)
            {
                var g = vouchers.Where(x => x.CardTypeUse == d.CardType).ToList();
                rows.Add(new BirthdayVoucherReconRow(p.Id, p.Code, p.Name, d.CardType,
                    g.Count, g.Sum(x => x.PointVCTotal), g.Sum(x => x.PointVCRemain)));
            }
        }
        return rows;
    }
}