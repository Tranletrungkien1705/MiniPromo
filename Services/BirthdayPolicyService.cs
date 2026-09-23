using Microsoft.EntityFrameworkCore;
using MiniPromo.Data;
using MiniPromo.Models;

namespace MiniPromo.Services;

// Kết quả kiểm tra một hội viên có đủ điều kiện nhận điểm sinh nhật hay không.
public record BirthdayEligibility(bool ok, string msg, decimal point, decimal amount, string cardType);

// Kết quả tặng điểm sinh nhật cho một hội viên.
public record BirthdayGrantOutcome(bool ok, string msg, decimal point, decimal amount, string cardType);

// Kết quả đối soát điểm sinh nhật đã tặng theo chương trình + loại thẻ.
public record BirthdayReconRow(int BirthdayPolicyId, string PolicyCode, string PolicyName,
    string CardType, int Granted, decimal PointGranted, decimal AmountGranted);

public interface IBirthdayPolicyService
{
    Task<List<BirthdayPolicy>> PoliciesAsync();
    Task<BirthdayPolicy?> GetPolicyAsync(int id);
    Task<(bool ok, string msg, int id)> CreatePolicyAsync(BirthdayPolicy p);
    Task<(bool ok, string msg)> AddDetailAsync(BirthdayPolicyDtl d);
    Task<(bool ok, string msg)> SetStatusAsync(int id, BirthdayPolicyStatus status);
    // Chương trình sinh nhật đang hiệu lực (đang bật + trong khoảng thời gian + có tặng điểm).
    Task<BirthdayPolicy?> ActivePolicyAsync();
    // Kiểm tra một hội viên có đủ điều kiện nhận điểm sinh nhật vào ngày at hay không.
    Task<BirthdayEligibility> CheckEligibilityAsync(string memberNo, string cardType, DateTime? dateOfBirth, DateTime? at);
    // Tặng điểm sinh nhật cho một hội viên (tạo BirthdayGrant) — chặn tặng trùng trong năm.
    Task<BirthdayGrantOutcome> GrantAsync(string memberNo, string cardNo, string cardType, string dealerCode, DateTime? dateOfBirth, DateTime? at);
    // Đối soát điểm sinh nhật đã tặng theo chương trình + loại thẻ.
    Task<List<BirthdayReconRow>> ReconciliationAsync(int? policyId);
}

/// <summary>
/// Nghiệp vụ chương trình tặng điểm sinh nhật (port từ Mst_BirthPolicy + Mst_BirthPolicyDtl,
/// Crd_Member_PerformBirhday trong Transaction.Birthday.cs của hệ Loyalty).
/// Quy tắc: chương trình đang bật, trong khoảng hiệu lực, có cờ FlagPoint; hội viên có ngày sinh
/// trùng ngày xét (theo tháng-ngày); loại thẻ có mức điểm riêng; mỗi hội viên chỉ được tặng 1 lần
/// trong một năm. Điểm được quy đổi ra tiền theo tỷ lệ ParamValue (UNITPOINTTOMONEY).
/// </summary>
public class BirthdayPolicyService(AppDbContext db) : IBirthdayPolicyService
{
    public Task<List<BirthdayPolicy>> PoliciesAsync() =>
        db.BirthdayPolicies.Include(p => p.Details)
            .OrderByDescending(p => p.Id).ToListAsync();

    public Task<BirthdayPolicy?> GetPolicyAsync(int id) =>
        db.BirthdayPolicies.Include(p => p.Details)
            .FirstOrDefaultAsync(p => p.Id == id);

    public async Task<(bool ok, string msg, int id)> CreatePolicyAsync(BirthdayPolicy p)
    {
        if (string.IsNullOrWhiteSpace(p.Name)) return (false, "Cần tên chương trình.", 0);
        if (string.IsNullOrWhiteSpace(p.Code)) p.Code = "BIRTH" + Guid.NewGuid().ToString("N")[..6].ToUpper();
        p.Code = p.Code.Trim().ToUpper();
        if (await db.BirthdayPolicies.IgnoreQueryFilters().AnyAsync(x => x.Code == p.Code)) return (false, "Mã chương trình đã tồn tại.", 0);
        if (p.EffDateEnd < p.EffDateStart) return (false, "Ngày kết thúc phải sau ngày bắt đầu.", 0);
        if (p.ParamValue <= 0) p.ParamValue = 1;

        db.BirthdayPolicies.Add(p); await db.SaveChangesAsync();
        return (true, "Đã tạo chương trình.", p.Id);
    }

    public async Task<(bool ok, string msg)> AddDetailAsync(BirthdayPolicyDtl d)
    {
        if (string.IsNullOrWhiteSpace(d.CardType)) return (false, "Cần loại thẻ.");
        if (d.Point <= 0) return (false, "Điểm tặng phải > 0.");
        if (!await db.BirthdayPolicies.AnyAsync(p => p.Id == d.BirthdayPolicyId)) return (false, "Không tìm thấy chương trình.");
        d.CardType = d.CardType.Trim().ToUpper();
        if (await db.BirthdayPolicyDtls.AnyAsync(x => x.BirthdayPolicyId == d.BirthdayPolicyId && x.CardType == d.CardType))
            return (false, "Loại thẻ đã có trong chương trình.");
        db.BirthdayPolicyDtls.Add(d); await db.SaveChangesAsync();
        return (true, "Đã thêm dòng loại thẻ.");
    }

    public async Task<(bool ok, string msg)> SetStatusAsync(int id, BirthdayPolicyStatus status)
    {
        var p = await db.BirthdayPolicies.Include(x => x.Details).FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return (false, "Không tìm thấy chương trình.");
        if (status == BirthdayPolicyStatus.Active && p.Details.Count == 0)
            return (false, "Cần ít nhất 1 dòng loại thẻ trước khi bật chương trình.");
        p.Status = status; await db.SaveChangesAsync();
        return (true, $"Chương trình: {status}.");
    }

    public Task<BirthdayPolicy?> ActivePolicyAsync()
    {
        var today = DateTime.Today;
        return db.BirthdayPolicies.Include(p => p.Details)
            .Where(p => p.Status == BirthdayPolicyStatus.Active && p.FlagPoint
                && p.EffDateStart <= today && p.EffDateEnd >= today)
            .OrderBy(p => p.EffDateStart).FirstOrDefaultAsync();
    }

    // Kiểm tra một hội viên có đủ điều kiện nhận điểm sinh nhật — port từ Crd_Member_PerformBirhday.
    public async Task<BirthdayEligibility> CheckEligibilityAsync(string memberNo, string cardType, DateTime? dateOfBirth, DateTime? at)
    {
        if (string.IsNullOrWhiteSpace(memberNo)) return new(false, "Cần mã hội viên.", 0, 0, "");
        if (dateOfBirth == null) return new(false, "Cần ngày sinh hội viên.", 0, 0, "");
        var day = (at ?? DateTime.Today).Date;

        var p = await ActivePolicyAsync();
        if (p == null) return new(false, "Không có chương trình tặng điểm sinh nhật đang hiệu lực.", 0, 0, "");

        // Ngày sinh phải trùng tháng-ngày với ngày xét.
        if (dateOfBirth.Value.Month != day.Month || dateOfBirth.Value.Day != day.Day)
            return new(false, "Hôm nay không phải sinh nhật của hội viên.", 0, 0, "");

        var code = (cardType ?? "").Trim().ToUpper();
        var dtl = p.Details.FirstOrDefault(x => x.CardType == code);
        if (dtl == null) return new(false, $"Loại thẻ {code} không thuộc chương trình.", 0, 0, "");

        // Mỗi hội viên chỉ được tặng 1 lần trong một năm.
        var yearStart = new DateTime(day.Year, 1, 1);
        var yearEnd = new DateTime(day.Year, 12, 31, 23, 59, 59);
        var already = await db.BirthdayGrants.AnyAsync(g =>
            g.MemberNo == memberNo.Trim() && g.GrantedAt >= yearStart && g.GrantedAt <= yearEnd);
        if (already) return new(false, "Hội viên đã nhận điểm sinh nhật trong năm nay.", 0, 0, "");

        var amount = dtl.Point * p.ParamValue;
        if (amount <= 0) return new(false, "Giá trị điểm sinh nhật không hợp lệ.", 0, 0, "");
        return new(true, "Đủ điều kiện nhận điểm sinh nhật.", dtl.Point, amount, code);
    }

    // Tặng điểm sinh nhật cho một hội viên — port từ Crd_Member_PerformBirhday.
    public async Task<BirthdayGrantOutcome> GrantAsync(string memberNo, string cardNo, string cardType, string dealerCode, DateTime? dateOfBirth, DateTime? at)
    {
        var check = await CheckEligibilityAsync(memberNo, cardType, dateOfBirth, at);
        if (!check.ok) return new(false, check.msg, 0, 0, "");

        var p = await ActivePolicyAsync();
        db.BirthdayGrants.Add(new BirthdayGrant
        {
            MemberNo = memberNo.Trim(), CardNo = (cardNo ?? "").Trim(), CardType = check.cardType,
            DealerCode = (dealerCode ?? "").Trim().ToUpper(), BirthdayPolicyId = p!.Id,
            Point = check.point, Amount = check.amount, GrantedAt = at ?? DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return new(true, "Đã tặng điểm sinh nhật.", check.point, check.amount, check.cardType);
    }

    // Đối soát điểm sinh nhật đã tặng theo chương trình + loại thẻ.
    public async Task<List<BirthdayReconRow>> ReconciliationAsync(int? policyId)
    {
        var policies = await db.BirthdayPolicies.Include(p => p.Details)
            .Where(p => policyId == null || p.Id == policyId)
            .OrderByDescending(p => p.Id).ToListAsync();

        var rows = new List<BirthdayReconRow>();
        foreach (var p in policies)
        {
            // SQLite không hỗ trợ SUM trên decimal → gom nhóm phía client.
            var grants = await db.BirthdayGrants
                .Where(g => g.BirthdayPolicyId == p.Id)
                .Select(g => new { g.CardType, g.Point, g.Amount })
                .ToListAsync();
            foreach (var d in p.Details)
            {
                var g = grants.Where(x => x.CardType == d.CardType).ToList();
                rows.Add(new BirthdayReconRow(p.Id, p.Code, p.Name, d.CardType,
                    g.Count, g.Sum(x => x.Point), g.Sum(x => x.Amount)));
            }
        }
        return rows;
    }
}
