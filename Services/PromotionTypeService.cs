using Microsoft.EntityFrameworkCore;
using MiniPromo.Data;
using MiniPromo.Models;

namespace MiniPromo.Services;

// Kết quả kiểm tra một hình thức khuyến mại có được phép dùng cho một loại khuyến mại theo hay không.
public record PrmInMainCheck(bool ok, string msg, string mainTypeCode, string prmTypeCode);

public interface IPromotionTypeService
{
    // Danh mục "Khuyến mại theo" (Mst_PromotionMainType).
    Task<List<PromotionMainTypeDef>> MainTypesAsync();
    Task<PromotionMainTypeDef?> GetMainTypeAsync(int id);
    Task<(bool ok, string msg, int id)> CreateMainTypeAsync(PromotionMainTypeDef t);
    Task<(bool ok, string msg)> SetMainTypeActiveAsync(int id, bool active);

    // Danh mục "Hình thức khuyến mại" (Mst_PromotionPrmType).
    Task<List<PromotionPrmTypeDef>> PrmTypesAsync();
    Task<PromotionPrmTypeDef?> GetPrmTypeAsync(int id);
    Task<(bool ok, string msg, int id)> CreatePrmTypeAsync(PromotionPrmTypeDef t);
    Task<(bool ok, string msg)> SetPrmTypeActiveAsync(int id, bool active);

    // Gắn hình thức khuyến mại vào loại khuyến mại theo (Prm_PrmInMain).
    Task<List<PromotionPrmInMain>> MappingsAsync();
    Task<(bool ok, string msg, int id)> AddMappingAsync(int mainTypeId, int prmTypeId, string? remark);
    Task<(bool ok, string msg)> SetMappingActiveAsync(int id, bool active);

    // Kiểm tra một hình thức khuyến mại có được phép dùng cho một loại khuyến mại theo hay không.
    Task<PrmInMainCheck> CheckPrmInMainAsync(string mainTypeCode, string prmTypeCode);
}

/// <summary>
/// Nghiệp vụ danh mục loại khuyến mại (port từ Mst_PromotionMainType + Mst_PromotionPrmType + Prm_PrmInMain
/// của hệ Loyalty). Quy tắc: mã loại/hình thức là duy nhất; chỉ bật/tạm dừng được; một hình thức khuyến mại
/// chỉ được gắn một lần vào một loại khuyến mại theo; chỉ khi cả loại, hình thức và dòng gắn đều đang bật thì
/// hình thức mới được phép dùng cho loại đó.
/// </summary>
public class PromotionTypeService(AppDbContext db) : IPromotionTypeService
{
    public Task<List<PromotionMainTypeDef>> MainTypesAsync() =>
        db.PromotionMainTypeDefs.Include(t => t.PrmInMains).OrderByDescending(t => t.Id).ToListAsync();

    public Task<PromotionMainTypeDef?> GetMainTypeAsync(int id) =>
        db.PromotionMainTypeDefs.Include(t => t.PrmInMains).ThenInclude(m => m.PrmType).FirstOrDefaultAsync(t => t.Id == id);

    public async Task<(bool ok, string msg, int id)> CreateMainTypeAsync(PromotionMainTypeDef t)
    {
        if (string.IsNullOrWhiteSpace(t.Name)) return (false, "Cần tên loại khuyến mại theo.", 0);
        if (string.IsNullOrWhiteSpace(t.Code)) t.Code = "MT" + Guid.NewGuid().ToString("N")[..6].ToUpper();
        t.Code = t.Code.Trim().ToUpper();
        if (await db.PromotionMainTypeDefs.IgnoreQueryFilters().AnyAsync(x => x.Code == t.Code))
            return (false, "Mã loại khuyến mại theo đã tồn tại.", 0);

        db.PromotionMainTypeDefs.Add(t); await db.SaveChangesAsync();
        return (true, "Đã tạo loại khuyến mại theo.", t.Id);
    }

    public async Task<(bool ok, string msg)> SetMainTypeActiveAsync(int id, bool active)
    {
        var t = await db.PromotionMainTypeDefs.FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return (false, "Không tìm thấy loại khuyến mại theo.");
        t.FlagActive = active; await db.SaveChangesAsync();
        return (true, active ? "Đã bật loại khuyến mại theo." : "Đã tạm dừng loại khuyến mại theo.");
    }

    public Task<List<PromotionPrmTypeDef>> PrmTypesAsync() =>
        db.PromotionPrmTypeDefs.Include(t => t.PrmInMains).OrderByDescending(t => t.Id).ToListAsync();

    public Task<PromotionPrmTypeDef?> GetPrmTypeAsync(int id) =>
        db.PromotionPrmTypeDefs.Include(t => t.PrmInMains).ThenInclude(m => m.MainType).FirstOrDefaultAsync(t => t.Id == id);

    public async Task<(bool ok, string msg, int id)> CreatePrmTypeAsync(PromotionPrmTypeDef t)
    {
        if (string.IsNullOrWhiteSpace(t.Name)) return (false, "Cần tên hình thức khuyến mại.", 0);
        if (string.IsNullOrWhiteSpace(t.Code)) t.Code = "PT" + Guid.NewGuid().ToString("N")[..6].ToUpper();
        t.Code = t.Code.Trim().ToUpper();
        if (await db.PromotionPrmTypeDefs.IgnoreQueryFilters().AnyAsync(x => x.Code == t.Code))
            return (false, "Mã hình thức khuyến mại đã tồn tại.", 0);

        db.PromotionPrmTypeDefs.Add(t); await db.SaveChangesAsync();
        return (true, "Đã tạo hình thức khuyến mại.", t.Id);
    }

    public async Task<(bool ok, string msg)> SetPrmTypeActiveAsync(int id, bool active)
    {
        var t = await db.PromotionPrmTypeDefs.FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return (false, "Không tìm thấy hình thức khuyến mại.");
        t.FlagActive = active; await db.SaveChangesAsync();
        return (true, active ? "Đã bật hình thức khuyến mại." : "Đã tạm dừng hình thức khuyến mại.");
    }

    public Task<List<PromotionPrmInMain>> MappingsAsync() =>
        db.PromotionPrmInMains.Include(m => m.MainType).Include(m => m.PrmType).OrderByDescending(m => m.Id).ToListAsync();

    // Gắn hình thức khuyến mại vào loại khuyến mại theo — port từ Prm_PrmInMain.
    public async Task<(bool ok, string msg, int id)> AddMappingAsync(int mainTypeId, int prmTypeId, string? remark)
    {
        if (!await db.PromotionMainTypeDefs.AnyAsync(t => t.Id == mainTypeId)) return (false, "Không tìm thấy loại khuyến mại theo.", 0);
        if (!await db.PromotionPrmTypeDefs.AnyAsync(t => t.Id == prmTypeId)) return (false, "Không tìm thấy hình thức khuyến mại.", 0);
        if (await db.PromotionPrmInMains.AnyAsync(m => m.MainTypeId == mainTypeId && m.PrmTypeId == prmTypeId))
            return (false, "Hình thức khuyến mại đã được gắn vào loại khuyến mại theo này.", 0);

        var m = new PromotionPrmInMain { MainTypeId = mainTypeId, PrmTypeId = prmTypeId, Remark = remark };
        db.PromotionPrmInMains.Add(m); await db.SaveChangesAsync();
        return (true, "Đã gắn hình thức khuyến mại vào loại khuyến mại theo.", m.Id);
    }

    public async Task<(bool ok, string msg)> SetMappingActiveAsync(int id, bool active)
    {
        var m = await db.PromotionPrmInMains.FirstOrDefaultAsync(x => x.Id == id);
        if (m == null) return (false, "Không tìm thấy dòng gắn hình thức.");
        m.FlagActive = active; await db.SaveChangesAsync();
        return (true, active ? "Đã bật dòng gắn hình thức." : "Đã tạm dừng dòng gắn hình thức.");
    }

    // Kiểm tra một hình thức khuyến mại có được phép dùng cho một loại khuyến mại theo hay không.
    // Chỉ đạt khi: loại khuyến mại theo đang bật, hình thức đang bật, và có dòng gắn đang bật giữa hai mã.
    public async Task<PrmInMainCheck> CheckPrmInMainAsync(string mainTypeCode, string prmTypeCode)
    {
        if (string.IsNullOrWhiteSpace(mainTypeCode)) return new(false, "Cần mã loại khuyến mại theo.", "", "");
        if (string.IsNullOrWhiteSpace(prmTypeCode)) return new(false, "Cần mã hình thức khuyến mại.", "", "");

        var mcode = mainTypeCode.Trim().ToUpper();
        var pcode = prmTypeCode.Trim().ToUpper();
        var main = await db.PromotionMainTypeDefs.FirstOrDefaultAsync(t => t.Code == mcode);
        if (main == null) return new(false, "Không tìm thấy loại khuyến mại theo.", mcode, pcode);
        if (!main.FlagActive) return new(false, "Loại khuyến mại theo đang tạm dừng.", mcode, pcode);

        var prm = await db.PromotionPrmTypeDefs.FirstOrDefaultAsync(t => t.Code == pcode);
        if (prm == null) return new(false, "Không tìm thấy hình thức khuyến mại.", mcode, pcode);
        if (!prm.FlagActive) return new(false, "Hình thức khuyến mại đang tạm dừng.", mcode, pcode);

        var map = await db.PromotionPrmInMains
            .FirstOrDefaultAsync(m => m.MainTypeId == main.Id && m.PrmTypeId == prm.Id);
        if (map == null) return new(false, "Hình thức khuyến mại không được phép dùng cho loại khuyến mại theo này.", mcode, pcode);
        if (!map.FlagActive) return new(false, "Dòng gắn hình thức khuyến mại đang tạm dừng.", mcode, pcode);

        return new(true, "Hình thức khuyến mại được phép dùng cho loại khuyến mại theo này.", mcode, pcode);
    }
}