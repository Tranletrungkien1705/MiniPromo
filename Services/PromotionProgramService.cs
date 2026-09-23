using Microsoft.EntityFrameworkCore;
using MiniPromo.Data;
using MiniPromo.Models;

namespace MiniPromo.Services;

// Kết quả tính khuyến mại cho một đơn hàng (port từ logic Prm_PromotionPrm).
public record PromotionCalcOutcome(bool ok, string msg, decimal productDiscount, decimal orderDiscount, decimal totalDiscount, string? programCode);

public interface IPromotionProgramService
{
    Task<List<PromotionProgram>> ProgramsAsync();
    Task<PromotionProgram?> GetProgramAsync(int id);
    Task<(bool ok, string msg, int id)> CreateProgramAsync(PromotionProgram p);
    Task<(bool ok, string msg)> AddScopeAsync(PromotionScope s);
    Task<(bool ok, string msg)> AddPrmAsync(PromotionPrm prm);
    Task<(bool ok, string msg)> AddMainAsync(PromotionMain m);
    Task<(bool ok, string msg)> SetStatusAsync(int id, PromotionStatus status);
    // Duyệt chương trình: chỉ từ Chờ duyệt.
    Task<(bool ok, string msg)> ApproveAsync(int id, string? remark);
    // Hoàn tất chương trình: chỉ từ Đã duyệt.
    Task<(bool ok, string msg)> FinishAsync(int id, string? remark);
    // Huỷ chương trình: chỉ từ Chờ duyệt/Đã duyệt.
    Task<(bool ok, string msg)> CancelAsync(int id, string? remark);
    // Chương trình đang hiệu lực (đã hoàn tất + trong khoảng thời gian).
    Task<PromotionProgram?> ActiveProgramAsync();
    // Kiểm tra điều kiện áp dụng (tháng/ngày/thứ/giờ) tại một thời điểm.
    Task<(bool ok, string msg)> CheckScopeAsync(int id, DateTime at);
    // Tính khuyến mại cho một đơn hàng theo chương trình đang hiệu lực.
    Task<PromotionCalcOutcome> CalcAsync(decimal orderAmount, int qty, DateTime? at);
}

/// <summary>
/// Nghiệp vụ chương trình khuyến mại chung (port từ Prm_Promotion của hệ Loyalty).
/// Quy tắc: vòng đời Chờ duyệt → Đã duyệt → Hoàn tất/Đã huỷ; chỉ 1 chương trình hiệu lực tại một
/// thời điểm (trừ khi bật FlagParallel); điều kiện áp dụng theo tháng/ngày/thứ/giờ (scope) và
/// điều kiện số lượng/tiền hàng (main). Hình thức giảm giá: giảm giá sản phẩm (theo tiền hoặc %,
/// có mức tối đa) và/hoặc giảm giá đơn hàng (theo tiền hoặc %, có mức tối đa).
/// </summary>
public class PromotionProgramService(AppDbContext db) : IPromotionProgramService
{
    public Task<List<PromotionProgram>> ProgramsAsync() =>
        db.PromotionPrograms.Include(p => p.Scopes).Include(p => p.Prms).Include(p => p.Mains)
            .OrderByDescending(p => p.Id).ToListAsync();

    public Task<PromotionProgram?> GetProgramAsync(int id) =>
        db.PromotionPrograms.Include(p => p.Scopes).Include(p => p.Prms).Include(p => p.Mains)
            .FirstOrDefaultAsync(p => p.Id == id);

    public async Task<(bool ok, string msg, int id)> CreateProgramAsync(PromotionProgram p)
    {
        if (string.IsNullOrWhiteSpace(p.Name)) return (false, "Cần tên chương trình.", 0);
        if (string.IsNullOrWhiteSpace(p.Code)) p.Code = "PRM" + Guid.NewGuid().ToString("N")[..6].ToUpper();
        p.Code = p.Code.Trim().ToUpper();
        if (await db.PromotionPrograms.IgnoreQueryFilters().AnyAsync(x => x.Code == p.Code)) return (false, "Mã chương trình đã tồn tại.", 0);
        if (p.EffDTimeEnd < p.EffDTimeStart) return (false, "Thời gian kết thúc phải sau thời gian bắt đầu.", 0);
        if (p.BudgetVal < 0) return (false, "Ngân sách không được âm.", 0);

        db.PromotionPrograms.Add(p); await db.SaveChangesAsync();
        return (true, "Đã tạo chương trình.", p.Id);
    }

    public async Task<(bool ok, string msg)> AddScopeAsync(PromotionScope s)
    {
        if (string.IsNullOrWhiteSpace(s.Value)) return (false, "Cần giá trị điều kiện.");
        if (!await db.PromotionPrograms.AnyAsync(p => p.Id == s.PromotionProgramId)) return (false, "Không tìm thấy chương trình.");
        s.Value = s.Value.Trim();
        if (s.ScopeType == PromotionScopeType.Time && string.IsNullOrWhiteSpace(s.ValueEnd))
            return (false, "Điều kiện giờ cần cả giờ bắt đầu và kết thúc.");
        db.PromotionScopes.Add(s); await db.SaveChangesAsync();
        return (true, "Đã thêm điều kiện áp dụng.");
    }

    public async Task<(bool ok, string msg)> AddPrmAsync(PromotionPrm prm)
    {
        if (!await db.PromotionPrograms.AnyAsync(p => p.Id == prm.PromotionProgramId)) return (false, "Không tìm thấy chương trình.");
        if (prm.UPDc < 0 || prm.UPRateDc < 0 || prm.ValOrdDc < 0 || prm.ValOrdRateDc < 0)
            return (false, "Giá trị giảm giá không được âm.");
        if (prm.UPRateDc > 100 || prm.ValOrdRateDc > 100)
            return (false, "Tỷ lệ giảm giá không được vượt quá 100%.");
        if (prm.UPDc == 0 && prm.UPRateDc == 0 && prm.ValOrdDc == 0 && prm.ValOrdRateDc == 0)
            return (false, "Cần ít nhất một hình thức giảm giá.");
        db.PromotionPrms.Add(prm); await db.SaveChangesAsync();
        return (true, "Đã thêm hình thức khuyến mại.");
    }

    public async Task<(bool ok, string msg)> AddMainAsync(PromotionMain m)
    {
        if (!await db.PromotionPrograms.AnyAsync(p => p.Id == m.PromotionProgramId)) return (false, "Không tìm thấy chương trình.");
        if (m.Qty < 0 || m.Amount < 0 || m.TotalValOrd < 0) return (false, "Điều kiện số lượng/tiền hàng không được âm.");
        db.PromotionMains.Add(m); await db.SaveChangesAsync();
        return (true, "Đã thêm điều kiện số lượng/tiền hàng.");
    }

    public async Task<(bool ok, string msg)> SetStatusAsync(int id, PromotionStatus status)
    {
        var p = await db.PromotionPrograms.Include(x => x.Prms).FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return (false, "Không tìm thấy chương trình.");
        if (status == PromotionStatus.Finished && p.Prms.Count == 0)
            return (false, "Cần ít nhất 1 hình thức khuyến mại trước khi hoàn tất.");
        p.Status = status; await db.SaveChangesAsync();
        return (true, $"Chương trình: {status}.");
    }

    // Duyệt chương trình — chỉ từ trạng thái Chờ duyệt.
    public async Task<(bool ok, string msg)> ApproveAsync(int id, string? remark)
    {
        var p = await db.PromotionPrograms.FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return (false, "Không tìm thấy chương trình.");
        if (p.Status != PromotionStatus.Pending)
            return (false, "Chỉ duyệt được chương trình đang ở trạng thái Chờ duyệt.");
        p.Status = PromotionStatus.Approved;
        if (!string.IsNullOrWhiteSpace(remark)) p.Remark = remark.Trim();
        await db.SaveChangesAsync();
        return (true, "Đã duyệt chương trình.");
    }

    // Hoàn tất chương trình — chỉ từ trạng thái Đã duyệt; cần ít nhất 1 hình thức khuyến mại.
    public async Task<(bool ok, string msg)> FinishAsync(int id, string? remark)
    {
        var p = await db.PromotionPrograms.Include(x => x.Prms).FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return (false, "Không tìm thấy chương trình.");
        if (p.Status != PromotionStatus.Approved)
            return (false, "Chỉ hoàn tất được chương trình đang ở trạng thái Đã duyệt.");
        if (p.Prms.Count == 0)
            return (false, "Cần ít nhất 1 hình thức khuyến mại trước khi hoàn tất.");
        p.Status = PromotionStatus.Finished;
        if (!string.IsNullOrWhiteSpace(remark)) p.Remark = remark.Trim();
        await db.SaveChangesAsync();
        return (true, "Đã hoàn tất chương trình.");
    }

    // Huỷ chương trình — chỉ từ Chờ duyệt/Đã duyệt.
    public async Task<(bool ok, string msg)> CancelAsync(int id, string? remark)
    {
        var p = await db.PromotionPrograms.FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return (false, "Không tìm thấy chương trình.");
        if (p.Status != PromotionStatus.Pending && p.Status != PromotionStatus.Approved)
            return (false, "Chỉ huỷ được chương trình đang Chờ duyệt hoặc Đã duyệt.");
        p.Status = PromotionStatus.Cancelled;
        if (!string.IsNullOrWhiteSpace(remark)) p.Remark = remark.Trim();
        await db.SaveChangesAsync();
        return (true, "Đã huỷ chương trình.");
    }

    public Task<PromotionProgram?> ActiveProgramAsync()
    {
        var today = DateTime.Today;
        return db.PromotionPrograms.Include(p => p.Scopes).Include(p => p.Prms).Include(p => p.Mains)
            .Where(p => p.Status == PromotionStatus.Finished && p.EffDTimeStart <= today && p.EffDTimeEnd >= today)
            .OrderBy(p => p.EffDTimeStart).FirstOrDefaultAsync();
    }

    // Kiểm tra điều kiện áp dụng (tháng/ngày/thứ/giờ) tại một thời điểm.
    // Nếu chương trình bật cờ "tất cả" cho một loại scope thì bỏ qua loại đó; ngược lại phải khớp
    // ít nhất một dòng scope đang bật của loại tương ứng.
    public async Task<(bool ok, string msg)> CheckScopeAsync(int id, DateTime at)
    {
        var p = await db.PromotionPrograms.Include(x => x.Scopes).FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return (false, "Không tìm thấy chương trình.");
        if (at.Date < p.EffDTimeStart.Date || at.Date > p.EffDTimeEnd.Date)
            return (false, "Thời điểm ngoài khoảng hiệu lực chương trình.");

        var active = p.Scopes.Where(s => s.Active).ToList();
        bool Match(PromotionScopeType t, Func<PromotionScope, bool> pred) =>
            active.Where(s => s.ScopeType == t).Any(pred);

        if (!p.FlagAllMonth && !Match(PromotionScopeType.Month, s => s.Value == at.Month.ToString()))
            return (false, "Không thuộc tháng áp dụng.");
        if (!p.FlagAllDay && !Match(PromotionScopeType.Day, s => s.Value == at.Day.ToString()))
            return (false, "Không thuộc ngày áp dụng.");
        if (!p.FlagAllDayOfWeek && !Match(PromotionScopeType.DayOfWeek, s => s.Value == ((int)at.DayOfWeek).ToString()))
            return (false, "Không thuộc thứ áp dụng.");
        if (!p.FlagAllTime && !Match(PromotionScopeType.Time, s => InTimeWindow(at, s.Value, s.ValueEnd)))
            return (false, "Ngoài khung giờ áp dụng.");

        return (true, "Đủ điều kiện áp dụng.");
    }

    private static bool InTimeWindow(DateTime at, string start, string? end)
    {
        if (!TimeSpan.TryParse(start, out var s) || !TimeSpan.TryParse(end, out var e)) return false;
        var t = at.TimeOfDay;
        return s <= e ? t >= s && t <= e : t >= s || t <= e;   // hỗ trợ khung giờ qua nửa đêm
    }

    // Tính khuyến mại cho một đơn hàng — port từ logic Prm_PromotionPrm.
    // Giảm giá sản phẩm: theo tiền (UPDc) hoặc theo % (UPRateDc, chặn bởi UPDcMax).
    // Giảm giá đơn hàng: theo tiền (ValOrdDc) hoặc theo % (ValOrdRateDc, chặn bởi ValOrdDcMax).
    // Nếu FlagMulti: nhân phần giảm giá sản phẩm theo số lượng mua.
    public async Task<PromotionCalcOutcome> CalcAsync(decimal orderAmount, int qty, DateTime? at)
    {
        if (orderAmount < 0) return new(false, "Số tiền đơn hàng không hợp lệ.", 0, 0, 0, null);
        var p = await ActiveProgramAsync();
        if (p == null) return new(false, "Không có chương trình khuyến mại đang hiệu lực.", 0, 0, 0, null);

        var when = at ?? DateTime.Now;
        var scope = await CheckScopeAsync(p.Id, when);
        if (!scope.ok) return new(false, scope.msg, 0, 0, 0, p.Code);

        // Điều kiện số lượng/tiền hàng: nếu có dòng main đang bật thì phải thoả ít nhất một dòng.
        var mains = p.Mains.Where(m => m.FlagActive).ToList();
        if (mains.Count > 0)
        {
            var pass = mains.Any(m => qty >= m.Qty && orderAmount >= m.Amount && orderAmount >= m.TotalValOrd);
            if (!pass) return new(false, "Đơn hàng chưa đạt điều kiện số lượng/tiền hàng.", 0, 0, 0, p.Code);
        }

        var prm = p.Prms.Where(x => x.FlagActive).OrderBy(x => x.Idx).FirstOrDefault();
        if (prm == null) return new(false, "Chương trình chưa có hình thức khuyến mại.", 0, 0, 0, p.Code);

        // Giảm giá sản phẩm.
        decimal productDiscount = prm.UPDc;
        if (prm.UPRateDc > 0)
        {
            var byRate = orderAmount * prm.UPRateDc / 100m;
            if (prm.UPDcMax > 0 && byRate > prm.UPDcMax) byRate = prm.UPDcMax;
            productDiscount += byRate;
        }
        if (p.FlagMulti && qty > 1) productDiscount *= qty;

        // Giảm giá đơn hàng.
        decimal orderDiscount = prm.ValOrdDc;
        if (prm.ValOrdRateDc > 0)
        {
            var byRate = orderAmount * prm.ValOrdRateDc / 100m;
            if (prm.ValOrdDcMax > 0 && byRate > prm.ValOrdDcMax) byRate = prm.ValOrdDcMax;
            orderDiscount += byRate;
        }

        var total = productDiscount + orderDiscount;
        if (total > orderAmount) total = orderAmount;   // không giảm quá giá trị đơn hàng
        return new(true, "Đủ điều kiện áp dụng.", productDiscount, orderDiscount, total, p.Code);
    }
}
