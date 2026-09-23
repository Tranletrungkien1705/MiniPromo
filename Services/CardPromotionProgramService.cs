using Microsoft.EntityFrameworkCore;
using MiniPromo.Data;
using MiniPromo.Models;

namespace MiniPromo.Services;

// Kết quả kiểm tra một giao dịch có được dùng ưu đãi của chương trình theo loại thẻ hay không.
public record CardPromotionUseOutcome(bool ok, string msg, int qtyRemain, int qtyUsed);

// Kết quả đối soát số lượng ưu đãi đã dùng theo chương trình + loại thẻ.
public record CardPromotionReconRow(int CardPromotionProgramId, string ProgramCode, string ProgramName,
    string CardType, int Qty, int QtyUsed, int QtyRemain);

// Một chương trình ưu đãi khả dụng cho một hội viên/thẻ — port từ Crd_Card_GetForPromotion.
// FlagShow: 1 = chương trình có cửa sổ ngày sinh nhật đang mở (Mst_ParamPromotion), 0 = không có, 2 = khác.
public record AvailablePromotionRow(int CardPromotionProgramId, string ProgramCode, string ProgramName,
    DateTime EffDateStart, int QtyPr, int QtyPrUsed, int QtyRemain, int FlagShow);

public interface ICardPromotionProgramService
{
    Task<List<CardPromotionProgram>> ProgramsAsync();
    Task<CardPromotionProgram?> GetProgramAsync(int id);
    Task<(bool ok, string msg, int id)> CreateProgramAsync(CardPromotionProgram p);
    Task<(bool ok, string msg)> AddDetailAsync(CardPromotionProgramDtl d);
    Task<(bool ok, string msg)> AddDealerAsync(CardPromotionProgramSpec s);
    Task<(bool ok, string msg)> SetStatusAsync(int id, CardPromotionProgramStatus status);
    // Chương trình đang hiệu lực (đang bật + trong khoảng thời gian).
    Task<CardPromotionProgram?> ActiveProgramAsync();
    // Số lượng ưu đãi còn lại của một loại thẻ trong chương trình (Qty − đã dùng).
    Task<int> QtyRemainAsync(int programId, string cardType);
    // Kiểm tra một giao dịch có được dùng ưu đãi không (đại lý thuộc phạm vi, loại thẻ có hạn mức, còn số lượng).
    Task<CardPromotionUseOutcome> CheckUseAsync(string dealerCode, string cardType, int qty);
    // Ghi nhận sử dụng ưu đãi cho một giao dịch (tạo CardPromotionUsage).
    // memberNo: mã hội viên — dùng cho luật "1 ngày + 1 chương trình + 1 hội viên + 1 loại thẻ ≤ 1".
    Task<CardPromotionUseOutcome> UseAsync(string dealNo, string dealerCode, string cardNo, string cardType, int qty, string? memberNo = null, DateTime? at = null);
    // Đối soát số lượng ưu đãi đã dùng theo chương trình + loại thẻ.
    Task<List<CardPromotionReconRow>> ReconciliationAsync(int? programId);
    // Danh sách chương trình ưu đãi khả dụng cho một hội viên/thẻ (port từ Crd_Card_GetForPromotion).
    Task<List<AvailablePromotionRow>> AvailableForCardAsync(string cardType, string dealerCode, DateTime? at = null);
}

/// <summary>
/// Nghiệp vụ chương trình khuyến mại theo loại thẻ (port từ Mst_PromotionProgram của hệ Loyalty).
/// Quy tắc: mỗi chương trình cấp một số lượng ưu đãi (Qty) cho từng loại thẻ (CardType); phạm vi
/// đại lý áp dụng theo FlagAllDL hoặc danh sách CardPromotionProgramSpec. Một giao dịch chỉ được
/// dùng ưu đãi khi chương trình đang hiệu lực, đại lý thuộc phạm vi, loại thẻ có hạn mức và số
/// lượng còn lại đủ; mỗi lần dùng ghi nhận vào CardPromotionUsage.
/// </summary>
public class CardPromotionProgramService(AppDbContext db) : ICardPromotionProgramService
{
    public Task<List<CardPromotionProgram>> ProgramsAsync() =>
        db.CardPromotionPrograms.Include(p => p.Details).Include(p => p.Dealers)
            .OrderByDescending(p => p.Id).ToListAsync();

    public Task<CardPromotionProgram?> GetProgramAsync(int id) =>
        db.CardPromotionPrograms.Include(p => p.Details).Include(p => p.Dealers)
            .FirstOrDefaultAsync(p => p.Id == id);

    public async Task<(bool ok, string msg, int id)> CreateProgramAsync(CardPromotionProgram p)
    {
        if (string.IsNullOrWhiteSpace(p.Name)) return (false, "Cần tên chương trình.", 0);
        if (string.IsNullOrWhiteSpace(p.Code)) p.Code = "PRMPR" + Guid.NewGuid().ToString("N")[..6].ToUpper();
        p.Code = p.Code.Trim().ToUpper();
        if (await db.CardPromotionPrograms.IgnoreQueryFilters().AnyAsync(x => x.Code == p.Code)) return (false, "Mã chương trình đã tồn tại.", 0);
        if (p.EffDateEnd < p.EffDateStart) return (false, "Ngày kết thúc phải sau ngày bắt đầu.", 0);

        db.CardPromotionPrograms.Add(p); await db.SaveChangesAsync();
        return (true, "Đã tạo chương trình.", p.Id);
    }

    public async Task<(bool ok, string msg)> AddDetailAsync(CardPromotionProgramDtl d)
    {
        if (string.IsNullOrWhiteSpace(d.CardType)) return (false, "Cần loại thẻ.");
        if (d.Qty <= 0) return (false, "Số lượng ưu đãi phải > 0.");
        if (!await db.CardPromotionPrograms.AnyAsync(p => p.Id == d.CardPromotionProgramId)) return (false, "Không tìm thấy chương trình.");
        d.CardType = d.CardType.Trim().ToUpper();
        if (await db.CardPromotionProgramDtls.AnyAsync(x => x.CardPromotionProgramId == d.CardPromotionProgramId && x.CardType == d.CardType))
            return (false, "Loại thẻ đã có trong chương trình.");
        db.CardPromotionProgramDtls.Add(d); await db.SaveChangesAsync();
        return (true, "Đã thêm dòng loại thẻ.");
    }

    public async Task<(bool ok, string msg)> AddDealerAsync(CardPromotionProgramSpec s)
    {
        if (string.IsNullOrWhiteSpace(s.DealerCode)) return (false, "Cần mã đại lý.");
        if (!await db.CardPromotionPrograms.AnyAsync(p => p.Id == s.CardPromotionProgramId)) return (false, "Không tìm thấy chương trình.");
        s.DealerCode = s.DealerCode.Trim().ToUpper();
        if (await db.CardPromotionProgramSpecs.AnyAsync(x => x.CardPromotionProgramId == s.CardPromotionProgramId && x.DealerCode == s.DealerCode))
            return (false, "Đại lý đã có trong chương trình.");
        db.CardPromotionProgramSpecs.Add(s); await db.SaveChangesAsync();
        return (true, "Đã thêm đại lý áp dụng.");
    }

    public async Task<(bool ok, string msg)> SetStatusAsync(int id, CardPromotionProgramStatus status)
    {
        var p = await db.CardPromotionPrograms.Include(x => x.Details).Include(x => x.Dealers).FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return (false, "Không tìm thấy chương trình.");
        if (status == CardPromotionProgramStatus.Active)
        {
            if (p.Details.Count == 0) return (false, "Cần ít nhất 1 dòng loại thẻ trước khi bật chương trình.");
            if (!p.FlagAllDL && p.Dealers.Count == 0) return (false, "Cần ít nhất 1 đại lý khi không áp dụng tất cả đại lý.");
        }
        p.Status = status; await db.SaveChangesAsync();
        return (true, $"Chương trình: {status}.");
    }

    public Task<CardPromotionProgram?> ActiveProgramAsync()
    {
        var today = DateTime.Today;
        return db.CardPromotionPrograms.Include(p => p.Details).Include(p => p.Dealers)
            .Where(p => p.Status == CardPromotionProgramStatus.Active && p.EffDateStart <= today && p.EffDateEnd >= today)
            .OrderBy(p => p.EffDateStart).FirstOrDefaultAsync();
    }

    // Số lượng ưu đãi còn lại của một loại thẻ = Qty − tổng đã dùng.
    public async Task<int> QtyRemainAsync(int programId, string cardType)
    {
        var code = (cardType ?? "").Trim().ToUpper();
        var dtl = await db.CardPromotionProgramDtls
            .FirstOrDefaultAsync(x => x.CardPromotionProgramId == programId && x.CardType == code);
        if (dtl == null) return 0;
        var used = await db.CardPromotionUsages
            .Where(u => u.CardPromotionProgramId == programId && u.CardType == code)
            .SumAsync(u => (int?)u.QtyUsed) ?? 0;
        return Math.Max(0, dtl.Qty - used);
    }

    // Kiểm tra một giao dịch có được dùng ưu đãi không — port từ Crd_DealUsePromotion_SaveX.
    public async Task<CardPromotionUseOutcome> CheckUseAsync(string dealerCode, string cardType, int qty)
    {
        if (qty <= 0) return new(false, "Số lượng ưu đãi phải > 0.", 0, 0);
        if (string.IsNullOrWhiteSpace(cardType)) return new(false, "Cần loại thẻ.", 0, 0);
        var p = await ActiveProgramAsync();
        if (p == null) return new(false, "Không có chương trình khuyến mại theo loại thẻ đang hiệu lực.", 0, 0);

        var dl = (dealerCode ?? "").Trim().ToUpper();
        if (!p.FlagAllDL && !p.Dealers.Any(x => x.DealerCode == dl))
            return new(false, $"Đại lý {dl} không thuộc phạm vi chương trình.", 0, 0);

        var code = cardType.Trim().ToUpper();
        var dtl = p.Details.FirstOrDefault(x => x.CardType == code && x.FlagActive);
        if (dtl == null) return new(false, $"Loại thẻ {code} không thuộc chương trình.", 0, 0);

        var remain = await QtyRemainAsync(p.Id, code);
        if (remain < qty) return new(false, $"Loại thẻ {code} chỉ còn {remain} ưu đãi.", remain, 0);
        return new(true, "Đủ điều kiện dùng ưu đãi.", remain, 0);
    }

    // Ghi nhận sử dụng ưu đãi cho một giao dịch — port từ Crd_DealUsePromotion_SaveX.
    public async Task<CardPromotionUseOutcome> UseAsync(string dealNo, string dealerCode, string cardNo, string cardType, int qty, string? memberNo = null, DateTime? at = null)
    {
        if (string.IsNullOrWhiteSpace(dealNo)) return new(false, "Cần số giao dịch.", 0, 0);
        var check = await CheckUseAsync(dealerCode, cardType, qty);
        if (!check.ok) return check;

        var p = await ActiveProgramAsync();
        var code = cardType.Trim().ToUpper();
        var member = (memberNo ?? "").Trim();
        var day = (at ?? DateTime.Today).Date;

        // Luật nguồn: trong 1 ngày + 1 chương trình + 1 hội viên + 1 loại thẻ, tổng ưu đãi ghi nhận không được > 1.
        if (!string.IsNullOrWhiteSpace(member))
        {
            var usedToday = await db.CardPromotionUsages
                .Where(u => u.CardPromotionProgramId == p!.Id && u.MemberNo == member && u.CardType == code
                    && u.UsedAt >= day && u.UsedAt < day.AddDays(1))
                .SumAsync(u => (int?)u.QtyUsed) ?? 0;
            if (usedToday + qty > 1)
                return new(false, $"Hội viên {member} đã ghi nhận ưu đãi loại thẻ {code} trong ngày {day:dd/MM/yyyy} (tối đa 1/ngày).", check.qtyRemain, 0);
        }

        db.CardPromotionUsages.Add(new CardPromotionUsage
        {
            DealNo = dealNo.Trim(), DealerCode = (dealerCode ?? "").Trim().ToUpper(),
            CardNo = (cardNo ?? "").Trim(), MemberNo = member, CardType = code,
            CardPromotionProgramId = p!.Id, QtyUsed = qty, UsedAt = at ?? DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return new(true, "Đã ghi nhận sử dụng ưu đãi.", check.qtyRemain - qty, qty);
    }

    // Đối soát số lượng ưu đãi đã dùng theo chương trình + loại thẻ.
    public async Task<List<CardPromotionReconRow>> ReconciliationAsync(int? programId)
    {
        var programs = await db.CardPromotionPrograms.Include(p => p.Details)
            .Where(p => programId == null || p.Id == programId)
            .OrderByDescending(p => p.Id).ToListAsync();

        var rows = new List<CardPromotionReconRow>();
        foreach (var p in programs)
        {
            var used = await db.CardPromotionUsages
                .Where(u => u.CardPromotionProgramId == p.Id)
                .GroupBy(u => u.CardType)
                .Select(g => new { CardType = g.Key, Qty = g.Sum(x => x.QtyUsed) })
                .ToListAsync();
            foreach (var d in p.Details)
            {
                var u = used.FirstOrDefault(x => x.CardType == d.CardType)?.Qty ?? 0;
                rows.Add(new CardPromotionReconRow(p.Id, p.Code, p.Name, d.CardType, d.Qty, u, Math.Max(0, d.Qty - u)));
            }
        }
        return rows;
    }

    // Danh sách chương trình ưu đãi khả dụng cho một hội viên/thẻ — port từ Crd_Card_GetForPromotion.
    // Quy tắc nguồn: chỉ xét chương trình đang bật + trong khoảng hiệu lực; dòng loại thẻ khớp CardTypeUse
    // và còn hạn mức (Qty − đã dùng > 0); chương trình áp dụng tất cả đại lý (FlagAllDL) hoặc đại lý của thẻ
    // nằm trong danh sách CardPromotionProgramSpec. FlagShow = 1 khi chương trình có cửa sổ ngày sinh nhật
    // đang mở (Mst_ParamPromotion), 0 khi không có, 2 khi khác.
    public async Task<List<AvailablePromotionRow>> AvailableForCardAsync(string cardType, string dealerCode, DateTime? at = null)
    {
        var code = (cardType ?? "").Trim().ToUpper();
        var dl = (dealerCode ?? "").Trim().ToUpper();
        if (string.IsNullOrWhiteSpace(code)) return new();

        var today = (at ?? DateTime.Today).Date;
        var programs = await db.CardPromotionPrograms.Include(p => p.Details).Include(p => p.Dealers)
            .Where(p => p.Status == CardPromotionProgramStatus.Active && p.EffDateStart <= today && p.EffDateEnd >= today)
            .OrderBy(p => p.EffDateStart).ToListAsync();

        var rows = new List<AvailablePromotionRow>();
        foreach (var p in programs)
        {
            // Phạm vi đại lý: tất cả đại lý, hoặc đại lý của thẻ thuộc danh sách chỉ định.
            if (!p.FlagAllDL && !p.Dealers.Any(x => x.DealerCode == dl)) continue;

            var dtl = p.Details.FirstOrDefault(x => x.CardType == code && x.FlagActive);
            if (dtl == null) continue;

            var used = await db.CardPromotionUsages
                .Where(u => u.CardPromotionProgramId == p.Id && u.CardType == code)
                .SumAsync(u => (int?)u.QtyUsed) ?? 0;
            var remain = Math.Max(0, dtl.Qty - used);
            if (remain <= 0) continue;

            rows.Add(new AvailablePromotionRow(p.Id, p.Code, p.Name, p.EffDateStart, dtl.Qty, used, remain, 0));
        }
        return rows;
    }
}