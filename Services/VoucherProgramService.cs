using Microsoft.EntityFrameworkCore;
using MiniPromo.Data;
using MiniPromo.Models;

namespace MiniPromo.Services;

// Kết quả tính giá trị voucher cho một xe cụ thể (port từ Prm_VoucherNewCar_CalcPrm).
public record VoucherCalcOutcome(bool ok, string msg, decimal pointVoucher, decimal pointUseLimit, string? modelCode);

public interface IVoucherProgramService
{
    Task<List<VoucherProgram>> ProgramsAsync();
    Task<VoucherProgram?> GetProgramAsync(int id);
    Task<(bool ok, string msg, int id)> CreateProgramAsync(VoucherProgram p);
    Task<(bool ok, string msg)> AddDetailAsync(VoucherProgramDtl d);
    Task<(bool ok, string msg)> SetStatusAsync(int id, VoucherProgramStatus status);
    Task<VoucherProgram?> ActiveProgramAsync();                       // chương trình đang hiệu lực (duy nhất)
    Task<VoucherCalcOutcome> CalcAsync(string modelCode, DateTime? deliveryDate, DateTime? registrationDate);
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
}
