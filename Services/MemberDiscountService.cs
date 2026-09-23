using Microsoft.EntityFrameworkCore;
using MiniPromo.Data;
using MiniPromo.Models;

namespace MiniPromo.Services;

// Một dòng hàng của giao dịch để tính chiết khấu: số tiền được chiết khấu (AmountForDC),
// tỷ lệ chiết khấu của đối tượng thanh toán (PaymentDiscountRate) và cờ có tính chiết khấu (FlagDiscount).
public record MemberDiscountLine(decimal AmountForDC, decimal PaymentDiscountRate, bool FlagDiscount = true);

// Kết quả tính chiết khấu cho một giao dịch.
// amountForDC = tổng tiền được chiết khấu, discount = giá trị chiết khấu, cardTypeApply = hạng thẻ áp dụng.
public record MemberDiscountCalc(bool ok, string msg, string refNo, string cardTypeApply,
    decimal amountForDC, decimal discount, decimal policyDiscountRate);

// Kết quả ghi nhận một giao dịch chiết khấu.
public record MemberDiscountRecord(bool ok, string msg, int id, decimal amountForDC, decimal discount);

// Đối soát chiết khấu theo hạng thẻ áp dụng: số giao dịch, tổng tiền được chiết khấu, tổng chiết khấu.
public record MemberDiscountRecon(string CardTypeApply, int Deals, decimal AmountForDC, decimal Discount);

public interface IMemberDiscountService
{
    Task<List<MemberDiscountTransaction>> TransactionsAsync(string? refNo);
    Task<MemberDiscountTransaction?> GetTransactionAsync(int id);

    // Tính giá trị chiết khấu cho một giao dịch theo hạng thẻ áp dụng.
    // Port từ logic trong Card.Deal.cs: mỗi dòng AmountDiscount = AmountForDC × dttt_DiscountRate/100 × ht_DiscountRate/100.
    Task<MemberDiscountCalc> CalcAsync(string refNo, string cardTypeApply, IEnumerable<MemberDiscountLine> lines, DateTime? at);

    // Ghi nhận giao dịch chiết khấu (chỉ khi giá trị chiết khấu > 0).
    Task<MemberDiscountRecord> RecordAsync(string refNo, string dealerCode, string memberNo, string cardNo,
        string cardTypeUse, string cardTypeInit, string cardTypeApply, IEnumerable<MemberDiscountLine> lines, DateTime? at);

    // Đối soát chiết khấu theo hạng thẻ áp dụng.
    Task<List<MemberDiscountRecon>> ReconciliationAsync(string? cardTypeApply);
}

/// <summary>
/// Nghiệp vụ chiết khấu hội viên (port từ Crd_MemberDiscountTransaction của hệ Loyalty,
/// logic tính trong Card.Deal.cs). Quy tắc:
///  - Chỉ tính các dòng hàng có cờ FlagDiscount (nguồn: where FlagDiscount = '1').
///  - Giá trị chiết khấu mỗi dòng = AmountForDC × (tỷ lệ chiết khấu đối tượng thanh toán / 100)
///    × (tỷ lệ chiết khấu hạng thẻ áp dụng / 100).
///  - Tỷ lệ chiết khấu hạng thẻ lấy từ chính sách quy đổi tiền → điểm đang hiệu lực
///    (Mst_PolicyMoneyToPointServiceDtl.DiscountRate theo CardTypeApply).
///  - Tổng hợp theo giao dịch; chỉ ghi nhận khi tổng chiết khấu > 0 (nguồn: PointChTotal > 0).
/// </summary>
public class MemberDiscountService(AppDbContext db) : IMemberDiscountService
{
    public Task<List<MemberDiscountTransaction>> TransactionsAsync(string? refNo) =>
        db.MemberDiscountTransactions
            .Where(t => string.IsNullOrEmpty(refNo) || t.RefNo == refNo)
            .OrderByDescending(t => t.Id)
            .ToListAsync();

    public Task<MemberDiscountTransaction?> GetTransactionAsync(int id) =>
        db.MemberDiscountTransactions.FirstOrDefaultAsync(t => t.Id == id);

    // Lấy tỷ lệ chiết khấu của hạng thẻ áp dụng từ chính sách quy đổi tiền → điểm đang hiệu lực.
    private async Task<(decimal rate, string? policyCode)> CardTypeDiscountRateAsync(string cardTypeApply, DateTime day)
    {
        var code = (cardTypeApply ?? "").Trim().ToUpper();
        if (string.IsNullOrEmpty(code)) return (0, null);

        var policy = await db.PolicyMoneyToPoints
            .Where(p => p.Status == PolicyMoneyToPointStatus.Active && p.EffDateStart <= day && day <= p.EffDateEnd)
            .OrderByDescending(p => p.Id)
            .FirstOrDefaultAsync();
        if (policy == null) return (0, null);

        var dtl = await db.PolicyMoneyToPointDtls
            .FirstOrDefaultAsync(d => d.PolicyMoneyToPointId == policy.Id && d.CardType == code && d.FlagActive);
        return dtl == null ? (0, policy.Code) : (dtl.DiscountRate, policy.Code);
    }

    // Tính giá trị chiết khấu cho một giao dịch — port từ logic trong Card.Deal.cs.
    public async Task<MemberDiscountCalc> CalcAsync(string refNo, string cardTypeApply, IEnumerable<MemberDiscountLine> lines, DateTime? at)
    {
        var code = (cardTypeApply ?? "").Trim().ToUpper();
        if (string.IsNullOrWhiteSpace(code)) return new(false, "Cần hạng thẻ áp dụng.", refNo, "", 0, 0, 0);
        var list = (lines ?? Enumerable.Empty<MemberDiscountLine>()).ToList();
        if (list.Count == 0) return new(false, "Cần ít nhất một dòng hàng.", refNo, code, 0, 0, 0);

        var day = (at ?? DateTime.Today).Date;
        var (rate, policyCode) = await CardTypeDiscountRateAsync(code, day);
        if (rate <= 0) return new(false, $"Hạng thẻ {code} không có tỷ lệ chiết khấu đang hiệu lực.", refNo, code, 0, 0, 0);

        decimal amountForDC = 0, discount = 0;
        foreach (var l in list)
        {
            if (!l.FlagDiscount) continue;                       // nguồn: where FlagDiscount = '1'
            if (l.AmountForDC <= 0) continue;
            amountForDC += l.AmountForDC;
            discount += l.AmountForDC * (l.PaymentDiscountRate / 100m) * (rate / 100m);
        }
        discount = Math.Round(discount, 2);
        return new(true, "Đã tính chiết khấu.", refNo, code, amountForDC, discount, rate);
    }

    // Ghi nhận giao dịch chiết khấu (chỉ khi giá trị chiết khấu > 0).
    public async Task<MemberDiscountRecord> RecordAsync(string refNo, string dealerCode, string memberNo, string cardNo,
        string cardTypeUse, string cardTypeInit, string cardTypeApply, IEnumerable<MemberDiscountLine> lines, DateTime? at)
    {
        if (string.IsNullOrWhiteSpace(refNo)) return new(false, "Cần số giao dịch.", 0, 0, 0);
        var day = (at ?? DateTime.Today).Date;

        var calc = await CalcAsync(refNo, cardTypeApply, lines, day);
        if (!calc.ok) return new(false, calc.msg, 0, 0, 0);
        if (calc.discount <= 0) return new(false, "Giá trị chiết khấu bằng 0, không ghi nhận.", 0, calc.amountForDC, 0);

        var (_, policyCode) = await CardTypeDiscountRateAsync(calc.cardTypeApply, day);
        var t = new MemberDiscountTransaction
        {
            RefNo = refNo.Trim(), DealerCode = (dealerCode ?? "").Trim().ToUpper(),
            MemberNo = (memberNo ?? "").Trim(), CardNo = (cardNo ?? "").Trim(),
            CardTypeUse = (cardTypeUse ?? "").Trim().ToUpper(), CardTypeInit = (cardTypeInit ?? "").Trim().ToUpper(),
            CardTypeApply = calc.cardTypeApply, DealPointType = MemberDiscountPointType.DiscountRO,
            PolicyCode = policyCode, PolicyDiscountRate = calc.policyDiscountRate,
            AmountForDC = calc.amountForDC, PointChTotal = calc.discount, CreateDate = day
        };
        db.MemberDiscountTransactions.Add(t); await db.SaveChangesAsync();
        return new(true, "Đã ghi nhận giao dịch chiết khấu.", t.Id, t.AmountForDC, t.PointChTotal);
    }

    // Đối soát chiết khấu theo hạng thẻ áp dụng.
    public async Task<List<MemberDiscountRecon>> ReconciliationAsync(string? cardTypeApply)
    {
        var code = (cardTypeApply ?? "").Trim().ToUpper();
        var q = db.MemberDiscountTransactions.AsQueryable();
        if (!string.IsNullOrEmpty(code)) q = q.Where(t => t.CardTypeApply == code);

        var rows = await q.ToListAsync();
        return rows
            .GroupBy(t => t.CardTypeApply)
            .Select(g => new MemberDiscountRecon(g.Key, g.Count(), g.Sum(x => x.AmountForDC), g.Sum(x => x.PointChTotal)))
            .OrderByDescending(r => r.Discount)
            .ToList();
    }
}