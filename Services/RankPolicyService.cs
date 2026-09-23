using Microsoft.EntityFrameworkCore;
using MiniPromo.Data;
using MiniPromo.Models;

namespace MiniPromo.Services;

// Kết quả đánh giá xếp hạng thẻ theo chính sách đang bật.
// action = hành động đề xuất (Nâng/Duy trì/Giữ nguyên), cardType = hạng thẻ đích.
public record RankEval(bool ok, string msg, RankActionType action, string cardType, int value);

public interface IRankPolicyService
{
    Task<List<RankPolicy>> PoliciesAsync();
    Task<RankPolicy?> GetPolicyAsync(int id);
    Task<(bool ok, string msg, int id)> CreatePolicyAsync(RankPolicy p);
    Task<(bool ok, string msg)> SetStatusAsync(int id, RankPolicyStatus status);

    // Đánh giá một thẻ (theo hạng hiện tại + điểm tích luỹ + số lần ghé thăm) để quyết định
    // nâng hạng / duy trì hạng / giữ nguyên — port từ Crd_CardRankPolicy_PerformX.
    Task<RankEval> EvaluateAsync(string cardType, decimal point, int qtyVisit);
}

/// <summary>
/// Nghiệp vụ chính sách xếp hạng thẻ (port từ Mst_RankPolicy + logic Crd_CardRankPolicy_PerformX /
/// Mst_RankPolicy_GetUp trong CardRank.cs của hệ Loyalty).
/// Quy tắc:
///  - Chỉ xét các chính sách đang bật (FlagActive).
///  - NÂNG hạng: nếu điểm tích luỹ ≥ PointUpBegin VÀ số lần ghé thăm ≥ QtyVisitUpBegin của hạng hiện tại,
///    thì chọn hạng kế tiếp có Value lớn hơn gần nhất (Mst_RankPolicy_GetUp: Value &gt; Value hiện tại, order by Value asc).
///  - DUY TRÌ hạng: nếu không nâng được nhưng điểm ≥ PointKeepBegin VÀ số lần ghé thăm ≥ QtyVisitKeepBegin
///    của hạng hiện tại thì giữ nguyên hạng.
///  - Ngược lại: giữ nguyên hạng (không đủ điều kiện).
/// </summary>
public class RankPolicyService(AppDbContext db) : IRankPolicyService
{
    public Task<List<RankPolicy>> PoliciesAsync() =>
        db.RankPolicies.OrderByDescending(p => p.Id).ToListAsync();

    public Task<RankPolicy?> GetPolicyAsync(int id) =>
        db.RankPolicies.FirstOrDefaultAsync(p => p.Id == id);

    public async Task<(bool ok, string msg, int id)> CreatePolicyAsync(RankPolicy p)
    {
        if (string.IsNullOrWhiteSpace(p.CardType)) return (false, "Cần mã hạng thẻ.", 0);
        if (string.IsNullOrWhiteSpace(p.Code)) p.Code = "RP" + Guid.NewGuid().ToString("N")[..6].ToUpper();
        p.Code = p.Code.Trim().ToUpper();
        p.CardType = p.CardType.Trim().ToUpper();
        if (await db.RankPolicies.IgnoreQueryFilters().AnyAsync(x => x.Code == p.Code))
            return (false, "Mã chính sách đã tồn tại.", 0);
        if (p.Value < 0) return (false, "Bậc xếp hạng không được âm.", 0);
        if (p.QtyMonth <= 0) p.QtyMonth = 12;

        db.RankPolicies.Add(p); await db.SaveChangesAsync();
        return (true, "Đã tạo chính sách xếp hạng.", p.Id);
    }

    public async Task<(bool ok, string msg)> SetStatusAsync(int id, RankPolicyStatus status)
    {
        var p = await db.RankPolicies.FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return (false, "Không tìm thấy chính sách.");
        p.Status = status; await db.SaveChangesAsync();
        return (true, status == RankPolicyStatus.Active ? "Đã bật chính sách." : "Đã tạm dừng chính sách.");
    }

    // Đánh giá xếp hạng — port từ Crd_CardRankPolicy_PerformX + Mst_RankPolicy_GetUp.
    public async Task<RankEval> EvaluateAsync(string cardType, decimal point, int qtyVisit)
    {
        if (string.IsNullOrWhiteSpace(cardType)) return new(false, "Cần mã hạng thẻ.", RankActionType.Keep, "", 0);
        var code = cardType.Trim().ToUpper();

        var current = await db.RankPolicies
            .FirstOrDefaultAsync(p => p.CardType == code && p.Status == RankPolicyStatus.Active);
        if (current == null) return new(false, $"Không có chính sách xếp hạng đang bật cho hạng thẻ {code}.", RankActionType.Keep, code, 0);

        // NÂNG hạng: đủ ngưỡng nâng của hạng hiện tại → chọn hạng kế tiếp (Value lớn hơn gần nhất).
        if (point >= current.PointUpBegin && qtyVisit >= current.QtyVisitUpBegin)
        {
            var up = await db.RankPolicies
                .Where(p => p.Status == RankPolicyStatus.Active && p.CardType != code && p.Value > current.Value)
                .OrderBy(p => p.Value)
                .FirstOrDefaultAsync();
            if (up != null)
                return new(true, $"Đủ điều kiện nâng hạng lên {up.CardType}.", RankActionType.Up, up.CardType, up.Value);
        }

        // DUY TRÌ hạng: đủ ngưỡng duy trì của hạng hiện tại.
        if (point >= current.PointKeepBegin && qtyVisit >= current.QtyVisitKeepBegin)
            return new(true, $"Đủ điều kiện duy trì hạng {code}.", RankActionType.Keep, code, current.Value);

        // Không đủ điều kiện — giữ nguyên hạng.
        return new(true, $"Chưa đủ điều kiện nâng/duy trì hạng {code}.", RankActionType.Keep, code, current.Value);
    }
}