using Microsoft.EntityFrameworkCore;
using MiniPromo.Data;
using MiniPromo.Models;

namespace MiniPromo.Services;

// Kết quả kiểm tra một mốc ngày có nằm trong khoảng áp dụng của tham số khuyến mại hay không.
public record ParamWindowCheck(bool ok, string msg, string programCode, DateTime? start, DateTime? end);

public interface IParamPromotionService
{
    // Danh sách loại áp dụng (Mst_ParamPromotionType).
    Task<List<ParamPromotionType>> TypesAsync();
    Task<(bool ok, string msg, int id)> CreateTypeAsync(ParamPromotionType t);
    Task<(bool ok, string msg)> SetTypeActiveAsync(int id, bool active);

    // Danh sách tham số khuyến mại (Mst_ParamPromotion).
    Task<List<ParamPromotion>> ParamsAsync();
    Task<ParamPromotion?> GetParamAsync(int id);
    Task<(bool ok, string msg, int id)> CreateParamAsync(ParamPromotion p);
    Task<(bool ok, string msg)> SetParamActiveAsync(int id, bool active);

    // Kiểm tra một mốc ngày có nằm trong khoảng áp dụng của tham số đang bật theo chương trình + loại áp dụng.
    Task<ParamWindowCheck> CheckWindowAsync(string programCode, string typeCode, DateTime anchor);
}

/// <summary>
/// Nghiệp vụ tham số khuyến mại (port từ Mst_ParamPromotion + Mst_ParamPromotionType của hệ Loyalty).
/// Quy tắc: mỗi tham số gắn một chương trình (PrProgramCode) với một loại áp dụng (ParamPrType) và một
/// khoảng ngày tương đối quanh một mốc tham chiếu: từ (mốc − QtyDateBefore) đến (mốc + QtyDateAfter).
/// Chỉ tham số đang bật (FlagActive) và loại áp dụng đang bật mới được dùng để xét khoảng ngày.
/// </summary>
public class ParamPromotionService(AppDbContext db) : IParamPromotionService
{
    public Task<List<ParamPromotionType>> TypesAsync() =>
        db.ParamPromotionTypes.OrderByDescending(t => t.Id).ToListAsync();

    public async Task<(bool ok, string msg, int id)> CreateTypeAsync(ParamPromotionType t)
    {
        if (string.IsNullOrWhiteSpace(t.Name)) return (false, "Cần tên loại áp dụng.", 0);
        if (string.IsNullOrWhiteSpace(t.Code)) t.Code = "PPT" + Guid.NewGuid().ToString("N")[..6].ToUpper();
        t.Code = t.Code.Trim().ToUpper();
        if (await db.ParamPromotionTypes.IgnoreQueryFilters().AnyAsync(x => x.Code == t.Code))
            return (false, "Mã loại áp dụng đã tồn tại.", 0);

        db.ParamPromotionTypes.Add(t); await db.SaveChangesAsync();
        return (true, "Đã tạo loại áp dụng.", t.Id);
    }

    public async Task<(bool ok, string msg)> SetTypeActiveAsync(int id, bool active)
    {
        var t = await db.ParamPromotionTypes.FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return (false, "Không tìm thấy loại áp dụng.");
        t.FlagActive = active; await db.SaveChangesAsync();
        return (true, active ? "Đã bật loại áp dụng." : "Đã tạm dừng loại áp dụng.");
    }

    public Task<List<ParamPromotion>> ParamsAsync() =>
        db.ParamPromotions.Include(p => p.ParamPromotionType).OrderByDescending(p => p.Id).ToListAsync();

    public Task<ParamPromotion?> GetParamAsync(int id) =>
        db.ParamPromotions.Include(p => p.ParamPromotionType).FirstOrDefaultAsync(p => p.Id == id);

    public async Task<(bool ok, string msg, int id)> CreateParamAsync(ParamPromotion p)
    {
        if (string.IsNullOrWhiteSpace(p.ProgramCode)) return (false, "Cần mã chương trình.", 0);
        p.ProgramCode = p.ProgramCode.Trim().ToUpper();
        if (p.QtyDateBefore < 0 || p.QtyDateAfter < 0) return (false, "Số ngày trước/sau không được âm.", 0);
        var type = await db.ParamPromotionTypes.FirstOrDefaultAsync(t => t.Id == p.ParamPromotionTypeId);
        if (type == null) return (false, "Không tìm thấy loại áp dụng.", 0);
        if (await db.ParamPromotions.AnyAsync(x => x.ProgramCode == p.ProgramCode && x.ParamPromotionTypeId == p.ParamPromotionTypeId))
            return (false, "Chương trình đã có tham số cho loại áp dụng này.", 0);

        db.ParamPromotions.Add(p); await db.SaveChangesAsync();
        return (true, "Đã tạo tham số khuyến mại.", p.Id);
    }

    public async Task<(bool ok, string msg)> SetParamActiveAsync(int id, bool active)
    {
        var p = await db.ParamPromotions.FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return (false, "Không tìm thấy tham số khuyến mại.");
        p.FlagActive = active; await db.SaveChangesAsync();
        return (true, active ? "Đã bật tham số." : "Đã tạm dừng tham số.");
    }

    // Kiểm tra một mốc ngày có nằm trong khoảng áp dụng của tham số đang bật theo chương trình + loại áp dụng.
    public async Task<ParamWindowCheck> CheckWindowAsync(string programCode, string typeCode, DateTime anchor)
    {
        if (string.IsNullOrWhiteSpace(programCode)) return new(false, "Cần mã chương trình.", "", null, null);
        if (string.IsNullOrWhiteSpace(typeCode)) return new(false, "Cần mã loại áp dụng.", "", null, null);

        var code = programCode.Trim().ToUpper();
        var tcode = typeCode.Trim().ToUpper();
        var p = await db.ParamPromotions.Include(x => x.ParamPromotionType)
            .FirstOrDefaultAsync(x => x.ProgramCode == code && x.ParamPromotionType!.Code == tcode);
        if (p == null) return new(false, "Không tìm thấy tham số cho chương trình và loại áp dụng.", code, null, null);
        if (!p.FlagActive) return new(false, "Tham số khuyến mại đang tạm dừng.", code, null, null);
        if (p.ParamPromotionType == null || !p.ParamPromotionType.FlagActive)
            return new(false, "Loại áp dụng đang tạm dừng.", code, null, null);

        var (start, end) = p.Window(anchor);
        var day = anchor.Date;
        if (day < start || day > end)
            return new(false, "Mốc ngày ngoài khoảng áp dụng.", code, start, end);

        return new(true, "Mốc ngày nằm trong khoảng áp dụng.", code, start, end);
    }
}