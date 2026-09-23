using Microsoft.EntityFrameworkCore;
using MiniPromo.Data;
using MiniPromo.Models;

namespace MiniPromo.Services;

// Kết quả quy đổi tiền dịch vụ → điểm cho một hạng thẻ theo chính sách đang hiệu lực.
// point = điểm quy đổi được, discountRate = % chiết khấu dịch vụ, qtyVisit = số lượt xét hạng (0/1).
public record MoneyToPointCalc(bool ok, string msg, string policyCode, string cardType,
    decimal amount, decimal point, decimal discountRate, int qtyVisit, decimal valueRankCardType);

public interface IPolicyMoneyToPointService
{
    Task<List<PolicyMoneyToPoint>> PoliciesAsync();
    Task<PolicyMoneyToPoint?> GetPolicyAsync(int id);
    Task<(bool ok, string msg, int id)> CreatePolicyAsync(PolicyMoneyToPoint p);
    Task<(bool ok, string msg)> SetStatusAsync(int id, PolicyMoneyToPointStatus status);
    Task<(bool ok, string msg)> AddDetailAsync(PolicyMoneyToPointDtl d);

    // Quy đổi một số tiền dịch vụ thành điểm cho một hạng thẻ theo chính sách đang hiệu lực.
    // Port từ logic tính điểm trong Card.Deal.cs: Point = Amount × (ConvertPoint / ConvertValue).
    Task<MoneyToPointCalc> CalcAsync(string cardType, decimal amount, DateTime? at);
}

/// <summary>
/// Nghiệp vụ chính sách quy đổi tiền dịch vụ → điểm (port từ Mst_PolicyMoneyToPointService +
/// Mst_PolicyMoneyToPointServiceDtl của hệ Loyalty, logic tính điểm trong Card.Deal.cs).
/// Quy tắc:
///  - Chỉ xét chính sách đang bật (FlagActive) và trong khoảng EffDateStart..EffDateEnd.
///  - Mỗi hạng thẻ (CardType) có tỷ lệ riêng: cứ ConvertValue tiền dịch vụ thì được ConvertPoint điểm.
///    Điểm = Amount × (ConvertPoint / ConvertValue).
///  - ValueRankCardType là mốc doanh thu: nếu Amount ≥ mốc thì tính 1 lượt xét hạng (QtyVisit = 1).
///  - DiscountRate là % chiết khấu dịch vụ của hạng thẻ đó.
/// </summary>
public class PolicyMoneyToPointService(AppDbContext db) : IPolicyMoneyToPointService
{
    public Task<List<PolicyMoneyToPoint>> PoliciesAsync() =>
        db.PolicyMoneyToPoints.Include(p => p.Details).OrderByDescending(p => p.Id).ToListAsync();

    public Task<PolicyMoneyToPoint?> GetPolicyAsync(int id) =>
        db.PolicyMoneyToPoints.Include(p => p.Details).FirstOrDefaultAsync(p => p.Id == id);

    public async Task<(bool ok, string msg, int id)> CreatePolicyAsync(PolicyMoneyToPoint p)
    {
        if (string.IsNullOrWhiteSpace(p.Code)) p.Code = "PMTP" + Guid.NewGuid().ToString("N")[..6].ToUpper();
        p.Code = p.Code.Trim().ToUpper();
        if (await db.PolicyMoneyToPoints.IgnoreQueryFilters().AnyAsync(x => x.Code == p.Code))
            return (false, "Mã chính sách đã tồn tại.", 0);
        if (p.EffDateEnd < p.EffDateStart) return (false, "Ngày kết thúc phải sau ngày bắt đầu.", 0);

        db.PolicyMoneyToPoints.Add(p); await db.SaveChangesAsync();
        return (true, "Đã tạo chính sách quy đổi tiền → điểm.", p.Id);
    }

    public async Task<(bool ok, string msg)> SetStatusAsync(int id, PolicyMoneyToPointStatus status)
    {
        var p = await db.PolicyMoneyToPoints.Include(x => x.Details).FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return (false, "Không tìm thấy chính sách.");
        if (status == PolicyMoneyToPointStatus.Active && p.Details.Count == 0)
            return (false, "Cần ít nhất một dòng hạng thẻ trước khi bật chính sách.");
        p.Status = status; await db.SaveChangesAsync();
        return (true, status == PolicyMoneyToPointStatus.Active ? "Đã bật chính sách." : "Đã tạm dừng chính sách.");
    }

    public async Task<(bool ok, string msg)> AddDetailAsync(PolicyMoneyToPointDtl d)
    {
        if (string.IsNullOrWhiteSpace(d.CardType)) return (false, "Cần hạng thẻ.");
        d.CardType = d.CardType.Trim().ToUpper();
        if (d.ConvertValue <= 0) return (false, "Giá trị quy đổi phải lớn hơn 0.");
        if (d.ConvertPoint < 0) return (false, "Điểm quy đổi không được âm.");
        if (d.DiscountRate < 0 || d.DiscountRate > 100) return (false, "Chiết khấu phải trong khoảng 0..100.");
        var p = await db.PolicyMoneyToPoints.FirstOrDefaultAsync(x => x.Id == d.PolicyMoneyToPointId);
        if (p == null) return (false, "Không tìm thấy chính sách.");
        if (await db.PolicyMoneyToPointDtls.AnyAsync(x => x.PolicyMoneyToPointId == d.PolicyMoneyToPointId && x.CardType == d.CardType))
            return (false, "Hạng thẻ này đã có trong chính sách.");

        db.PolicyMoneyToPointDtls.Add(d); await db.SaveChangesAsync();
        return (true, "Đã thêm dòng hạng thẻ.");
    }

    // Quy đổi tiền dịch vụ → điểm — port từ logic tính điểm trong Card.Deal.cs.
    public async Task<MoneyToPointCalc> CalcAsync(string cardType, decimal amount, DateTime? at)
    {
        if (string.IsNullOrWhiteSpace(cardType)) return new(false, "Cần hạng thẻ.", "", "", amount, 0, 0, 0, 0);
        if (amount < 0) return new(false, "Số tiền không được âm.", "", cardType.Trim().ToUpper(), amount, 0, 0, 0, 0);
        var code = cardType.Trim().ToUpper();
        var day = (at ?? DateTime.Today).Date;

        var policy = await db.PolicyMoneyToPoints
            .Where(p => p.Status == PolicyMoneyToPointStatus.Active && p.EffDateStart <= day && day <= p.EffDateEnd)
            .OrderByDescending(p => p.Id)
            .FirstOrDefaultAsync();
        if (policy == null) return new(false, "Không có chính sách quy đổi đang hiệu lực.", "", code, amount, 0, 0, 0, 0);

        var dtl = await db.PolicyMoneyToPointDtls
            .FirstOrDefaultAsync(d => d.PolicyMoneyToPointId == policy.Id && d.CardType == code && d.FlagActive);
        if (dtl == null) return new(false, $"Hạng thẻ {code} không có trong chính sách.", policy.Code, code, amount, 0, 0, 0, 0);

        var point = Math.Round(amount * dtl.Rate, 2);
        var qtyVisit = dtl.ValueRankCardType > 0 && amount >= dtl.ValueRankCardType ? 1 : 0;
        return new(true, "Đã quy đổi tiền dịch vụ thành điểm.", policy.Code, code, amount, point, dtl.DiscountRate, qtyVisit, dtl.ValueRankCardType);
    }
}
