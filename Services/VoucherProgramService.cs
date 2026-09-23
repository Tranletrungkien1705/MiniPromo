using Microsoft.EntityFrameworkCore;
using MiniPromo.Data;
using MiniPromo.Models;

namespace MiniPromo.Services;

// Kết quả tính giá trị voucher cho một xe cụ thể (port từ Prm_VoucherNewCar_CalcPrm).
public record VoucherCalcOutcome(bool ok, string msg, decimal pointVoucher, decimal pointUseLimit, string? modelCode);

// Kết quả phát voucher cho một xe (gắn chương trình → tạo Voucher).
public record VoucherIssueOutcome(bool ok, string msg, int voucherId, string? voucherCode, DateTime expireDate);

// Một dòng đối soát: số voucher đã phát theo chương trình + model.
public record VoucherReconRow(int VoucherProgramId, string ProgramCode, string ProgramName, string ModelCode,
    int Issued, int Used, int Unused, decimal PointIssued, decimal PointUsed, decimal PointRemain);

public interface IVoucherProgramService
{
    Task<List<VoucherProgram>> ProgramsAsync();
    Task<VoucherProgram?> GetProgramAsync(int id);
    Task<(bool ok, string msg, int id)> CreateProgramAsync(VoucherProgram p);
    Task<(bool ok, string msg)> AddDetailAsync(VoucherProgramDtl d);
    Task<(bool ok, string msg)> SetStatusAsync(int id, VoucherProgramStatus status);
    // Hoàn tất chương trình (port từ Prm_VoucherNewCar_Finish): chỉ từ Đã duyệt, chặn trùng ngày bắt đầu,
    // và cắt ngày kết thúc của chương trình đang hiệu lực trước đó về (ngày bắt đầu mới − 1).
    Task<(bool ok, string msg)> FinishAsync(int id, string? remark);
    // Huỷ chương trình (port từ Prm_VoucherNewCar_Cancel): chỉ từ Chờ duyệt/Đã duyệt.
    Task<(bool ok, string msg)> CancelAsync(int id, string? remark);
    Task<VoucherProgram?> ActiveProgramAsync();                       // chương trình đang hiệu lực (duy nhất)
    Task<VoucherCalcOutcome> CalcAsync(string modelCode, DateTime? deliveryDate, DateTime? registrationDate);
    // Phát voucher cho một xe: kiểm tra điều kiện áp dụng rồi tạo Voucher gắn chương trình.
    Task<VoucherIssueOutcome> IssueAsync(string modelCode, DateTime? deliveryDate, DateTime? registrationDate, string? memberNo);
    // Đối soát số voucher đã phát theo chương trình + model (đã phát / đã dùng / còn lại).
    Task<List<VoucherReconRow>> ReconciliationAsync(int? programId);
}

/// <summary>
/// Nghiệp vụ chương trình voucher (port từ Prm_VoucherNewCar của hệ Loyalty).
/// Quy tắc: chỉ 1 chương trình hiệu lực tại một thời điểm; chương trình mới phải bắt đầu
/// từ hôm nay và sau chương trình trước; mỗi dòng chi tiết phải có giá trị voucher > 0.
/// Tính giá trị: nếu áp dụng tất cả model thì dùng giá trị chung, ngược lại tra theo model;
/// ngày giao xe phải nằm trong khoảng hiệu lực và ngày mở thẻ không vượt quá ngày giao xe + giới hạn.
/// </summary>
public class VoucherProgramService(AppDbContext db) : IVoucherProgramService
{
    public Task<List<VoucherProgram>> ProgramsAsync() =>
        db.VoucherPrograms.Include(p => p.Details).OrderByDescending(p => p.Id).ToListAsync();

    public Task<VoucherProgram?> GetProgramAsync(int id) =>
        db.VoucherPrograms.Include(p => p.Details).FirstOrDefaultAsync(p => p.Id == id);

    public async Task<(bool ok, string msg, int id)> CreateProgramAsync(VoucherProgram p)
    {
        if (string.IsNullOrWhiteSpace(p.Name)) return (false, "Cần tên chương trình.", 0);
        if (string.IsNullOrWhiteSpace(p.Code)) p.Code = "PRM" + Guid.NewGuid().ToString("N")[..6].ToUpper();
        p.Code = p.Code.Trim().ToUpper();
        if (await db.VoucherPrograms.IgnoreQueryFilters().AnyAsync(x => x.Code == p.Code)) return (false, "Mã chương trình đã tồn tại.", 0);
        if (p.EffDateEnd < p.EffDateStart) return (false, "Ngày kết thúc phải sau ngày bắt đầu.", 0);
        if (p.ValidityPeriod <= 0) p.ValidityPeriod = 30;
        if (p.QtyDayLimitFDlvDate < 0) p.QtyDayLimitFDlvDate = 0;

        // Chương trình mới phải bắt đầu từ hôm nay và sau chương trình đang/đã hiệu lực gần nhất.
        var today = DateTime.Today;
        if (p.EffDateStart < today) return (false, "Ngày bắt đầu không được trước hôm nay.", 0);
        var prev = await db.VoucherPrograms.IgnoreQueryFilters()
            .Where(x => x.Status == VoucherProgramStatus.Finished && x.EffDateStart <= today && x.EffDateEnd >= today)
            .OrderBy(x => x.EffDateStart).FirstOrDefaultAsync();
        if (prev != null && p.EffDateStart <= prev.EffDateStart)
            return (false, $"Ngày bắt đầu phải sau chương trình trước ({prev.Code}).", 0);

        db.VoucherPrograms.Add(p); await db.SaveChangesAsync();
        return (true, "Đã tạo chương trình.", p.Id);
    }

    public async Task<(bool ok, string msg)> AddDetailAsync(VoucherProgramDtl d)
    {
        if (string.IsNullOrWhiteSpace(d.ModelCode)) return (false, "Cần mã model.");
        if (d.PointVoucher <= 0) return (false, "Giá trị voucher phải > 0.");
        if (d.PointUseLimit <= 0) d.PointUseLimit = d.PointVoucher;
        if (!await db.VoucherPrograms.AnyAsync(p => p.Id == d.VoucherProgramId)) return (false, "Không tìm thấy chương trình.");
        d.ModelCode = d.ModelCode.Trim().ToUpper();
        db.VoucherProgramDtls.Add(d); await db.SaveChangesAsync();
        return (true, "Đã thêm dòng model.");
    }

    public async Task<(bool ok, string msg)> SetStatusAsync(int id, VoucherProgramStatus status)
    {
        var p = await db.VoucherPrograms.Include(x => x.Details).FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return (false, "Không tìm thấy chương trình.");
        if (status == VoucherProgramStatus.Finished && !p.FlagAllModel && p.Details.Count == 0)
            return (false, "Cần ít nhất 1 dòng model trước khi hoàn tất.");
        if (status == VoucherProgramStatus.Finished && p.PointVoucherAllModel <= 0 && p.FlagAllModel)
            return (false, "Cần giá trị voucher chung > 0 trước khi hoàn tất.");
        p.Status = status; await db.SaveChangesAsync();
        return (true, $"Chương trình: {status}.");
    }

    // Hoàn tất chương trình — port từ Prm_VoucherNewCar_Finish.
    // Chỉ hoàn tất được từ trạng thái Đã duyệt; không cho trùng ngày bắt đầu với chương trình đã hoàn tất khác;
    // sau khi hoàn tất, cắt ngày kết thúc của chương trình đang hiệu lực trước đó về (ngày bắt đầu mới − 1).
    public async Task<(bool ok, string msg)> FinishAsync(int id, string? remark)
    {
        var p = await db.VoucherPrograms.Include(x => x.Details).FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return (false, "Không tìm thấy chương trình.");
        if (p.Status != VoucherProgramStatus.Approved)
            return (false, "Chỉ hoàn tất được chương trình đang ở trạng thái Đã duyệt.");
        if (!p.FlagAllModel && p.Details.Count == 0)
            return (false, "Cần ít nhất 1 dòng model trước khi hoàn tất.");
        if (p.FlagAllModel && p.PointVoucherAllModel <= 0)
            return (false, "Cần giá trị voucher chung > 0 trước khi hoàn tất.");

        // Chặn trùng ngày bắt đầu với một chương trình đã hoàn tất khác.
        var dup = await db.VoucherPrograms.IgnoreQueryFilters()
            .Where(x => x.Id != p.Id && x.Status == VoucherProgramStatus.Finished && x.EffDateStart == p.EffDateStart)
            .FirstOrDefaultAsync();
        if (dup != null)
            return (false, $"Đã có chương trình hoàn tất cùng ngày bắt đầu ({dup.Code}).");

        p.Status = VoucherProgramStatus.Finished;
        if (!string.IsNullOrWhiteSpace(remark)) p.Remark = remark.Trim();

        // Cắt ngày kết thúc của chương trình đang hiệu lực trước đó về (ngày bắt đầu mới − 1).
        var today = DateTime.Today;
        if (p.EffDateStart <= today)
        {
            var prev = await db.VoucherPrograms.IgnoreQueryFilters()
                .Where(x => x.Id != p.Id && x.Status == VoucherProgramStatus.Finished
                    && x.EffDateStart <= today && x.EffDateEnd >= today)
                .OrderByDescending(x => x.EffDateStart).FirstOrDefaultAsync();
            if (prev != null)
            {
                var newEnd = p.EffDateStart.Date.AddDays(-1);
                if (newEnd < prev.EffDateEnd.Date) prev.EffDateEnd = newEnd;
            }
        }

        await db.SaveChangesAsync();
        return (true, "Đã hoàn tất chương trình.");
    }

    // Huỷ chương trình — port từ Prm_VoucherNewCar_Cancel. Chỉ huỷ được từ Chờ duyệt/Đã duyệt.
    public async Task<(bool ok, string msg)> CancelAsync(int id, string? remark)
    {
        var p = await db.VoucherPrograms.FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return (false, "Không tìm thấy chương trình.");
        if (p.Status != VoucherProgramStatus.Pending && p.Status != VoucherProgramStatus.Approved)
            return (false, "Chỉ huỷ được chương trình đang Chờ duyệt hoặc Đã duyệt.");
        p.Status = VoucherProgramStatus.Cancelled;
        if (!string.IsNullOrWhiteSpace(remark)) p.Remark = remark.Trim();
        await db.SaveChangesAsync();
        return (true, "Đã huỷ chương trình.");
    }

    // Đối soát số voucher đã phát theo chương trình + model.
    // Gom nhóm voucher gắn chương trình (VoucherProgramId != null) theo (chương trình, model).
    public async Task<List<VoucherReconRow>> ReconciliationAsync(int? programId)
    {
        var q = db.Vouchers.IgnoreQueryFilters().Where(v => v.VoucherProgramId != null);
        if (programId.HasValue) q = q.Where(v => v.VoucherProgramId == programId.Value);
        var vouchers = await q.ToListAsync();
        var programIds = vouchers.Select(v => v.VoucherProgramId!.Value).Distinct().ToList();
        var programs = await db.VoucherPrograms.IgnoreQueryFilters()
            .Where(p => programIds.Contains(p.Id)).ToListAsync();
        var usedByVoucher = await db.VoucherRedemptions.IgnoreQueryFilters()
            .Where(r => vouchers.Select(v => v.Id).Contains(r.VoucherId))
            .GroupBy(r => r.VoucherId)
            .Select(g => new { VoucherId = g.Key, Count = g.Count() })
            .ToListAsync();
        var usedMap = usedByVoucher.ToDictionary(x => x.VoucherId, x => x.Count);

        return vouchers
            .GroupBy(v => new { v.VoucherProgramId, Model = v.ModelCode ?? "" })
            .Select(g =>
            {
                var prog = programs.FirstOrDefault(p => p.Id == g.Key.VoucherProgramId);
                var used = g.Sum(v => usedMap.TryGetValue(v.Id, out var c) ? c : 0);
                return new VoucherReconRow(
                    g.Key.VoucherProgramId!.Value, prog?.Code ?? "", prog?.Name ?? "", g.Key.Model,
                    Issued: g.Count(),
                    Used: used,
                    Unused: g.Count() - used,
                    PointIssued: g.Sum(v => v.PointTotal),
                    PointUsed: g.Sum(v => v.PointTotal - v.PointRemain),
                    PointRemain: g.Sum(v => v.PointRemain));
            })
            .OrderBy(r => r.ProgramCode).ThenBy(r => r.ModelCode)
            .ToList();
    }

    public Task<VoucherProgram?> ActiveProgramAsync()
    {
        var today = DateTime.Today;
        return db.VoucherPrograms.Include(p => p.Details)
            .Where(p => p.Status == VoucherProgramStatus.Finished && p.EffDateStart <= today && p.EffDateEnd >= today)
            .OrderBy(p => p.EffDateStart).FirstOrDefaultAsync();
    }

    // Tính giá trị voucher cho một xe — port từ Prm_VoucherNewCar_CalcPrm.
    public async Task<VoucherCalcOutcome> CalcAsync(string modelCode, DateTime? deliveryDate, DateTime? registrationDate)
    {
        if (string.IsNullOrWhiteSpace(modelCode)) return new(false, "Cần mã model.", 0, 0, null);
        var p = await ActiveProgramAsync();
        if (p == null) return new(false, "Không có chương trình voucher đang hiệu lực.", 0, 0, null);

        modelCode = modelCode.Trim().ToUpper();
        decimal point, limit;
        if (p.FlagAllModel)
        {
            point = p.PointVoucherAllModel; limit = p.PointUseLimitAllModel;
        }
        else
        {
            var d = p.Details.FirstOrDefault(x => x.ModelCode == modelCode);
            if (d == null) return new(false, $"Model {modelCode} không thuộc chương trình.", 0, 0, modelCode);
            point = d.PointVoucher; limit = d.PointUseLimit;
        }

        // Ngày giao xe phải nằm trong khoảng hiệu lực của chương trình.
        if (deliveryDate.HasValue && (deliveryDate.Value.Date < p.EffDateStart.Date || deliveryDate.Value.Date > p.EffDateEnd.Date))
            return new(false, "Ngày giao xe ngoài khoảng hiệu lực chương trình.", 0, 0, modelCode);

        // Ngày mở thẻ không vượt quá ngày giao xe + giới hạn ngày.
        if (deliveryDate.HasValue && registrationDate.HasValue)
        {
            var dayLimit = deliveryDate.Value.Date.AddDays(p.QtyDayLimitFDlvDate);
            if (registrationDate.Value.Date > dayLimit)
                return new(false, "Ngày mở thẻ vượt quá giới hạn kể từ ngày giao xe.", 0, 0, modelCode);
        }

        return new(true, "Đủ điều kiện áp dụng.", point, limit, modelCode);
    }

    // Phát voucher cho một xe — gắn chương trình đang hiệu lực với Voucher đã có.
    // Dùng lại CalcAsync để kiểm tra điều kiện (model, ngày giao xe, giới hạn ngày mở thẻ),
    // rồi tạo Voucher: điểm = giá trị chương trình, hạn dùng = hôm nay + ValidityPeriod.
    public async Task<VoucherIssueOutcome> IssueAsync(string modelCode, DateTime? deliveryDate, DateTime? registrationDate, string? memberNo)
    {
        var calc = await CalcAsync(modelCode, deliveryDate, registrationDate);
        if (!calc.ok) return new(false, calc.msg, 0, null, default);

        var p = await ActiveProgramAsync();
        if (p == null) return new(false, "Không có chương trình voucher đang hiệu lực.", 0, null, default);

        var expire = DateTime.Today.AddDays(p.ValidityPeriod > 0 ? p.ValidityPeriod : 30);
        var voucher = new Voucher
        {
            Code = "VC" + Guid.NewGuid().ToString("N")[..8].ToUpper(),
            Name = $"{p.Name} — {calc.modelCode}",
            MemberNo = memberNo,
            VoucherProgramId = p.Id,
            ModelCode = calc.modelCode,
            PointTotal = calc.pointVoucher,
            PointRemain = calc.pointVoucher,
            PointLimit = calc.pointUseLimit > 0 ? calc.pointUseLimit : calc.pointVoucher,
            QtyUseLimit = 1,
            QtyUseRemain = 1,
            ExpireDate = expire,
            Active = true
        };
        db.Vouchers.Add(voucher);
        await db.SaveChangesAsync();
        return new(true, $"Đã phát voucher {voucher.Code} cho model {calc.modelCode}.", voucher.Id, voucher.Code, expire);
    }
}
