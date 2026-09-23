using Microsoft.EntityFrameworkCore;
using MiniPromo.Data;
using MiniPromo.Models;

namespace MiniPromo.Services;

// Kết quả phát hành một voucher trong đợt.
public record IssueOutcome(bool ok, string msg, int voucherId, string voucherNo, DateTime? expDate);

// Kết quả kiểm tra một voucher có được dùng hay không.
public record IssueUseCheck(bool ok, string msg, string voucherNo, string favorType);

// Kết quả đối soát đợt phát hành theo trạng thái voucher.
public record IssueReconRow(int IssueVoucherId, string IssueCode, string IssueName,
    int QtyVoucher, int Issued, int Used, int Pending, int Evicted, int Cancelled);

public interface IIssueVoucherService
{
    Task<List<IssueVoucher>> BatchesAsync();
    Task<IssueVoucher?> GetBatchAsync(int id);
    Task<(bool ok, string msg, int id)> CreateBatchAsync(IssueVoucher v);
    Task<(bool ok, string msg)> AddScopeAsync(IssueVoucherScope s);
    Task<(bool ok, string msg)> AddProductAsync(IssueVoucherProduct p);
    Task<(bool ok, string msg)> AddPriceAsync(IssueVoucherPrice p);
    Task<(bool ok, string msg)> SetActiveAsync(int id, bool active);
    // Đợt phát hành đang thực sự áp dụng (đang bật + trong khoảng hiệu lực).
    Task<IssueVoucher?> ActiveBatchAsync();
    // Phát hành một voucher trong đợt (tạo IssueVoucherDtl) — chặn vượt số lượng, chặn trùng mã.
    Task<IssueOutcome> IssueAsync(int batchId, string voucherNo, string? receiver, DateTime? at);
    // Kiểm tra một voucher có được dùng hay không (đợt đang hiệu lực + voucher đã phát + chưa hết hạn).
    Task<IssueUseCheck> CheckUseAsync(string voucherNo, DateTime? at);
    // Ghi nhận sử dụng voucher (chuyển trạng thái sang Đã sử dụng).
    Task<IssueUseCheck> UseAsync(string voucherNo, string? orderNo, DateTime? at);
    // Thu hồi voucher đã phát (chuyển trạng thái sang Thu hồi).
    Task<(bool ok, string msg)> EvictAsync(int voucherId);
    // Huỷ voucher đã phát (chuyển trạng thái sang Huỷ).
    Task<(bool ok, string msg)> CancelVoucherAsync(int voucherId);
    // Đối soát đợt phát hành theo trạng thái voucher.
    Task<List<IssueReconRow>> ReconciliationAsync(int? batchId);
}

/// <summary>
/// Nghiệp vụ đợt phát hành voucher (port từ Mst_IssueVoucher + Mst_IssueVoucherDtl,
/// Mst_IssueVoucherScopeBranch/ScopeUser/ScopeCusGroup, Mst_IssueVoucherInsPrd, Mst_IssueVoucherInsPrice).
/// Quy tắc: đợt đang bật và trong khoảng hiệu lực mới phát hành/cho dùng; số voucher phát không vượt
/// QtyVoucher; mã voucher duy nhất; voucher hết hạn (ngày phát + QtyDateUse) không dùng được;
/// vòng đời voucher: Chưa phát → Đã phát → Đã dùng / Thu hồi / Huỷ.
/// </summary>
public class IssueVoucherService(AppDbContext db) : IIssueVoucherService
{
    public Task<List<IssueVoucher>> BatchesAsync() =>
        db.IssueVouchers.Include(v => v.Details).Include(v => v.Scopes).Include(v => v.Products).Include(v => v.Prices)
            .OrderByDescending(v => v.Id).ToListAsync();

    public Task<IssueVoucher?> GetBatchAsync(int id) =>
        db.IssueVouchers.Include(v => v.Details).Include(v => v.Scopes).Include(v => v.Products).Include(v => v.Prices)
            .FirstOrDefaultAsync(v => v.Id == id);

    public async Task<(bool ok, string msg, int id)> CreateBatchAsync(IssueVoucher v)
    {
        if (string.IsNullOrWhiteSpace(v.Name)) return (false, "Cần tên đợt phát hành.", 0);
        if (string.IsNullOrWhiteSpace(v.Code)) v.Code = "ISSUE" + Guid.NewGuid().ToString("N")[..6].ToUpper();
        v.Code = v.Code.Trim().ToUpper();
        if (await db.IssueVouchers.IgnoreQueryFilters().AnyAsync(x => x.Code == v.Code)) return (false, "Mã đợt phát hành đã tồn tại.", 0);
        if (v.EffDateEnd < v.EffDateStart) return (false, "Ngày kết thúc phải sau ngày bắt đầu.", 0);
        if (v.QtyVoucher <= 0) return (false, "Số lượng voucher phải > 0.", 0);
        if (v.QtyDateUse <= 0) return (false, "Thời hạn sử dụng phải > 0 ngày.", 0);

        db.IssueVouchers.Add(v); await db.SaveChangesAsync();
        return (true, "Đã tạo đợt phát hành.", v.Id);
    }

    public async Task<(bool ok, string msg)> AddScopeAsync(IssueVoucherScope s)
    {
        if (string.IsNullOrWhiteSpace(s.Value)) return (false, "Cần giá trị phạm vi.");
        if (!await db.IssueVouchers.AnyAsync(v => v.Id == s.IssueVoucherId)) return (false, "Không tìm thấy đợt phát hành.");
        s.Value = s.Value.Trim().ToUpper();
        if (await db.IssueVoucherScopes.AnyAsync(x => x.IssueVoucherId == s.IssueVoucherId && x.ScopeType == s.ScopeType && x.Value == s.Value))
            return (false, "Phạm vi đã có trong đợt.");
        db.IssueVoucherScopes.Add(s); await db.SaveChangesAsync();
        return (true, "Đã thêm phạm vi áp dụng.");
    }

    public async Task<(bool ok, string msg)> AddProductAsync(IssueVoucherProduct p)
    {
        if (string.IsNullOrWhiteSpace(p.RefCode)) return (false, "Cần mã hàng/nhóm hàng.");
        if (!await db.IssueVouchers.AnyAsync(v => v.Id == p.IssueVoucherId)) return (false, "Không tìm thấy đợt phát hành.");
        p.RefCode = p.RefCode.Trim().ToUpper();
        if (await db.IssueVoucherProducts.AnyAsync(x => x.IssueVoucherId == p.IssueVoucherId && x.RefType == p.RefType && x.RefCode == p.RefCode))
            return (false, "Hàng hoá đã có trong đợt.");
        db.IssueVoucherProducts.Add(p); await db.SaveChangesAsync();
        return (true, "Đã thêm điều kiện hàng hoá.");
    }

    public async Task<(bool ok, string msg)> AddPriceAsync(IssueVoucherPrice p)
    {
        if (string.IsNullOrWhiteSpace(p.IssueTypeDtl)) return (false, "Cần loại giá trị ưu đãi.");
        if (p.UPDc < 0 || p.UPRateDc < 0 || p.UPDcMax < 0) return (false, "Giá trị ưu đãi không được âm.");
        if (p.UPDc == 0 && p.UPRateDc == 0) return (false, "Cần nhập giảm giá theo tiền hoặc theo %.");
        if (!await db.IssueVouchers.AnyAsync(v => v.Id == p.IssueVoucherId)) return (false, "Không tìm thấy đợt phát hành.");
        db.IssueVoucherPrices.Add(p); await db.SaveChangesAsync();
        return (true, "Đã thêm giá trị ưu đãi.");
    }

    public async Task<(bool ok, string msg)> SetActiveAsync(int id, bool active)
    {
        var v = await db.IssueVouchers.Include(x => x.Prices).FirstOrDefaultAsync(x => x.Id == id);
        if (v == null) return (false, "Không tìm thấy đợt phát hành.");
        if (active && v.Prices.Count == 0)
            return (false, "Cần ít nhất 1 dòng giá trị ưu đãi trước khi bật đợt.");
        v.FlagActive = active; await db.SaveChangesAsync();
        return (true, active ? "Đã bật đợt phát hành." : "Đã tạm dừng đợt phát hành.");
    }

    public Task<IssueVoucher?> ActiveBatchAsync()
    {
        var today = DateTime.Today;
        return db.IssueVouchers.Include(v => v.Details).Include(v => v.Prices)
            .Where(v => v.FlagActive && v.EffDateStart <= today && v.EffDateEnd >= today)
            .OrderBy(v => v.EffDateStart).FirstOrDefaultAsync();
    }

    // Phát hành một voucher trong đợt — port từ Mst_IssueVoucherDtl (IssueStatus = ISSUE).
    public async Task<IssueOutcome> IssueAsync(int batchId, string voucherNo, string? receiver, DateTime? at)
    {
        if (string.IsNullOrWhiteSpace(voucherNo)) return new(false, "Cần mã voucher.", 0, "", null);
        var v = await db.IssueVouchers.Include(x => x.Details).FirstOrDefaultAsync(x => x.Id == batchId);
        if (v == null) return new(false, "Không tìm thấy đợt phát hành.", 0, "", null);
        if (!v.IsLiveNow) return new(false, "Đợt phát hành chưa/không còn hiệu lực.", 0, "", null);

        var code = voucherNo.Trim().ToUpper();
        if (await db.IssueVoucherDtls.IgnoreQueryFilters().AnyAsync(x => x.VoucherNo == code))
            return new(false, "Mã voucher đã tồn tại.", 0, "", null);

        // Số voucher đã phát (không tính dòng đã huỷ) không được vượt số lượng của đợt.
        var issued = v.Details.Count(d => d.Status != IssueVoucherStatus.Cancelled);
        if (issued >= v.QtyVoucher) return new(false, "Đợt đã phát đủ số lượng voucher.", 0, "", null);

        var issueDate = (at ?? DateTime.Today).Date;
        var expDate = issueDate.AddDays(v.QtyDateUse);
        var dtl = new IssueVoucherDtl
        {
            IssueVoucherId = v.Id, VoucherNo = code, Receiver = (receiver ?? "").Trim(),
            Status = IssueVoucherStatus.Issued, IssueDate = issueDate, ExpDate = expDate
        };
        db.IssueVoucherDtls.Add(dtl); await db.SaveChangesAsync();
        return new(true, "Đã phát hành voucher.", dtl.Id, code, expDate);
    }

    // Kiểm tra một voucher có được dùng hay không — port từ Mst_IssueVoucherDtl (IssueStatus).
    public async Task<IssueUseCheck> CheckUseAsync(string voucherNo, DateTime? at)
    {
        if (string.IsNullOrWhiteSpace(voucherNo)) return new(false, "Cần mã voucher.", "", "");
        var code = voucherNo.Trim().ToUpper();
        var dtl = await db.IssueVoucherDtls.Include(d => d.IssueVoucher)
            .FirstOrDefaultAsync(d => d.VoucherNo == code);
        if (dtl == null) return new(false, "Không tìm thấy voucher.", code, "");
        if (dtl.Status == IssueVoucherStatus.Used) return new(false, "Voucher đã được sử dụng.", code, "");
        if (dtl.Status == IssueVoucherStatus.Evicted) return new(false, "Voucher đã bị thu hồi.", code, "");
        if (dtl.Status == IssueVoucherStatus.Cancelled) return new(false, "Voucher đã bị huỷ.", code, "");
        if (dtl.Status == IssueVoucherStatus.Pending) return new(false, "Voucher chưa được phát hành.", code, "");

        var day = (at ?? DateTime.Today).Date;
        if (dtl.ExpDate != null && day > dtl.ExpDate.Value.Date)
            return new(false, "Voucher đã hết hạn sử dụng.", code, "");

        var batch = dtl.IssueVoucher;
        if (batch == null || !batch.IsLiveNow)
            return new(false, "Đợt phát hành không còn hiệu lực.", code, "");

        return new(true, "Voucher hợp lệ.", code, batch.FavorType.ToString());
    }

    // Ghi nhận sử dụng voucher — port từ Mst_IssueVoucherDtl (IssueStatus = USE).
    public async Task<IssueUseCheck> UseAsync(string voucherNo, string? orderNo, DateTime? at)
    {
        var check = await CheckUseAsync(voucherNo, at);
        if (!check.ok) return check;

        var dtl = await db.IssueVoucherDtls.FirstAsync(d => d.VoucherNo == check.voucherNo);
        dtl.Status = IssueVoucherStatus.Used;
        dtl.UseDate = (at ?? DateTime.Today).Date;
        dtl.OrderNo = (orderNo ?? "").Trim();
        await db.SaveChangesAsync();
        return new(true, "Đã ghi nhận sử dụng voucher.", check.voucherNo, check.favorType);
    }

    // Thu hồi voucher đã phát — port từ Mst_IssueVoucherDtl (IssueStatus = EVICT).
    public async Task<(bool ok, string msg)> EvictAsync(int voucherId)
    {
        var dtl = await db.IssueVoucherDtls.FirstOrDefaultAsync(d => d.Id == voucherId);
        if (dtl == null) return (false, "Không tìm thấy voucher.");
        if (dtl.Status != IssueVoucherStatus.Issued) return (false, "Chỉ thu hồi được voucher đã phát.");
        dtl.Status = IssueVoucherStatus.Evicted; await db.SaveChangesAsync();
        return (true, "Đã thu hồi voucher.");
    }

    // Huỷ voucher đã phát — port từ Mst_IssueVoucherDtl (IssueStatus = CANCEL).
    public async Task<(bool ok, string msg)> CancelVoucherAsync(int voucherId)
    {
        var dtl = await db.IssueVoucherDtls.FirstOrDefaultAsync(d => d.Id == voucherId);
        if (dtl == null) return (false, "Không tìm thấy voucher.");
        if (dtl.Status == IssueVoucherStatus.Used) return (false, "Không huỷ được voucher đã sử dụng.");
        if (dtl.Status == IssueVoucherStatus.Cancelled) return (false, "Voucher đã bị huỷ.");
        dtl.Status = IssueVoucherStatus.Cancelled; await db.SaveChangesAsync();
        return (true, "Đã huỷ voucher.");
    }

    // Đối soát đợt phát hành theo trạng thái voucher.
    public async Task<List<IssueReconRow>> ReconciliationAsync(int? batchId)
    {
        var batches = await db.IssueVouchers.Include(v => v.Details)
            .Where(v => batchId == null || v.Id == batchId)
            .OrderByDescending(v => v.Id).ToListAsync();

        return batches.Select(v => new IssueReconRow(v.Id, v.Code, v.Name, v.QtyVoucher,
            v.Details.Count(d => d.Status == IssueVoucherStatus.Issued),
            v.Details.Count(d => d.Status == IssueVoucherStatus.Used),
            v.Details.Count(d => d.Status == IssueVoucherStatus.Pending),
            v.Details.Count(d => d.Status == IssueVoucherStatus.Evicted),
            v.Details.Count(d => d.Status == IssueVoucherStatus.Cancelled))).ToList();
    }
}
