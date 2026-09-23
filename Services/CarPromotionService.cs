using Microsoft.EntityFrameworkCore;
using MiniPromo.Data;
using MiniPromo.Models;

namespace MiniPromo.Services;

// Kết quả tính giá trị khuyến mại cho một model xe (port từ Prm_CarNew_CalcPrm).
public record CarPromoCalcOutcome(bool ok, string msg, decimal pointVal, string? modelCode);

public interface ICarPromotionService
{
    Task<List<CarPromotion>> PromotionsAsync();
    Task<CarPromotion?> GetPromotionAsync(int id);
    Task<(bool ok, string msg, int id)> CreatePromotionAsync(CarPromotion p);
    Task<(bool ok, string msg)> AddDetailAsync(CarPromotionDtl d);
    Task<(bool ok, string msg)> SetStatusAsync(int id, CarPromotionStatus status);
    // Duyệt chương trình (port từ Prm_CarNew_Appr): chỉ từ Chờ duyệt.
    Task<(bool ok, string msg)> ApproveAsync(int id, string? remark);
    // Hoàn tất chương trình (port từ Prm_CarNew_Finish): chỉ từ Đã duyệt, cắt ngày kết thúc
    // của chương trình đang hiệu lực trước đó về (ngày bắt đầu mới − 1).
    Task<(bool ok, string msg)> FinishAsync(int id, string? remark);
    // Huỷ chương trình (port từ Prm_CarNew_Cancel): chỉ từ Chờ duyệt/Đã duyệt.
    Task<(bool ok, string msg)> CancelAsync(int id, string? remark);
    Task<CarPromotion?> ActivePromotionAsync(string dealerCode);       // chương trình đang hiệu lực của đại lý
    // Tính giá trị khuyến mại cho một model theo chương trình đang hiệu lực của đại lý.
    Task<CarPromoCalcOutcome> CalcAsync(string dealerCode, string modelCode);
}

/// <summary>
/// Nghiệp vụ chương trình khuyến mại mua xe mới (port từ Prm_CarNew của hệ Loyalty).
/// Quy tắc: mỗi đại lý chỉ có 1 chương trình hiệu lực tại một thời điểm; chương trình mới phải
/// bắt đầu từ hôm nay và sau chương trình đang hiệu lực gần nhất; mỗi dòng chi tiết phải có
/// giá trị khuyến mại > 0. Tính giá trị: nếu áp dụng tất cả model thì dùng giá trị chung,
/// ngược lại tra theo model.
/// </summary>
public class CarPromotionService(AppDbContext db) : ICarPromotionService
{
    public Task<List<CarPromotion>> PromotionsAsync() =>
        db.CarPromotions.Include(p => p.Details).OrderByDescending(p => p.Id).ToListAsync();

    public Task<CarPromotion?> GetPromotionAsync(int id) =>
        db.CarPromotions.Include(p => p.Details).FirstOrDefaultAsync(p => p.Id == id);

    public async Task<(bool ok, string msg, int id)> CreatePromotionAsync(CarPromotion p)
    {
        if (string.IsNullOrWhiteSpace(p.Name)) return (false, "Cần tên chương trình.", 0);
        if (string.IsNullOrWhiteSpace(p.DealerCode)) return (false, "Cần mã đại lý.", 0);
        if (string.IsNullOrWhiteSpace(p.Code)) p.Code = "PRMCN" + Guid.NewGuid().ToString("N")[..6].ToUpper();
        p.Code = p.Code.Trim().ToUpper();
        p.DealerCode = p.DealerCode.Trim().ToUpper();
        if (await db.CarPromotions.IgnoreQueryFilters().AnyAsync(x => x.Code == p.Code)) return (false, "Mã chương trình đã tồn tại.", 0);
        if (p.EffDateEnd < p.EffDateStart) return (false, "Ngày kết thúc phải sau ngày bắt đầu.", 0);

        // Chương trình mới phải bắt đầu từ hôm nay và sau chương trình đang hiệu lực gần nhất của đại lý.
        var today = DateTime.Today;
        if (p.EffDateStart < today) return (false, "Ngày bắt đầu không được trước hôm nay.", 0);
        var prev = await db.CarPromotions.IgnoreQueryFilters()
            .Where(x => x.DealerCode == p.DealerCode && x.Status == CarPromotionStatus.Finished
                && x.EffDateStart <= today && x.EffDateEnd >= today)
            .OrderBy(x => x.EffDateStart).FirstOrDefaultAsync();
        if (prev != null && p.EffDateStart <= prev.EffDateStart)
            return (false, $"Ngày bắt đầu phải sau chương trình trước ({prev.Code}).", 0);

        db.CarPromotions.Add(p); await db.SaveChangesAsync();
        return (true, "Đã tạo chương trình.", p.Id);
    }

    public async Task<(bool ok, string msg)> AddDetailAsync(CarPromotionDtl d)
    {
        if (string.IsNullOrWhiteSpace(d.ModelCode)) return (false, "Cần mã model.");
        if (d.PointVal <= 0) return (false, "Giá trị khuyến mại phải > 0.");
        if (!await db.CarPromotions.AnyAsync(p => p.Id == d.CarPromotionId)) return (false, "Không tìm thấy chương trình.");
        d.ModelCode = d.ModelCode.Trim().ToUpper();
        db.CarPromotionDtls.Add(d); await db.SaveChangesAsync();
        return (true, "Đã thêm dòng model.");
    }

    public async Task<(bool ok, string msg)> SetStatusAsync(int id, CarPromotionStatus status)
    {
        var p = await db.CarPromotions.Include(x => x.Details).FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return (false, "Không tìm thấy chương trình.");
        if (status == CarPromotionStatus.Finished && !p.FlagAllModel && p.Details.Count == 0)
            return (false, "Cần ít nhất 1 dòng model trước khi hoàn tất.");
        if (status == CarPromotionStatus.Finished && p.FlagAllModel && p.PointValAllModel <= 0)
            return (false, "Cần giá trị khuyến mại chung > 0 trước khi hoàn tất.");
        p.Status = status; await db.SaveChangesAsync();
        return (true, $"Chương trình: {status}.");
    }

    // Duyệt chương trình — port từ Prm_CarNew_Appr. Chỉ duyệt được từ trạng thái Chờ duyệt.
    public async Task<(bool ok, string msg)> ApproveAsync(int id, string? remark)
    {
        var p = await db.CarPromotions.FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return (false, "Không tìm thấy chương trình.");
        if (p.Status != CarPromotionStatus.Pending)
            return (false, "Chỉ duyệt được chương trình đang ở trạng thái Chờ duyệt.");
        p.Status = CarPromotionStatus.Approved;
        if (!string.IsNullOrWhiteSpace(remark)) p.Remark = remark.Trim();
        await db.SaveChangesAsync();
        return (true, "Đã duyệt chương trình.");
    }

    // Hoàn tất chương trình — port từ Prm_CarNew_Finish.
    // Chỉ hoàn tất được từ trạng thái Đã duyệt; sau khi hoàn tất, cắt ngày kết thúc của
    // chương trình đang hiệu lực trước đó về (ngày bắt đầu mới − 1).
    public async Task<(bool ok, string msg)> FinishAsync(int id, string? remark)
    {
        var p = await db.CarPromotions.Include(x => x.Details).FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return (false, "Không tìm thấy chương trình.");
        if (p.Status != CarPromotionStatus.Approved)
            return (false, "Chỉ hoàn tất được chương trình đang ở trạng thái Đã duyệt.");
        if (!p.FlagAllModel && p.Details.Count == 0)
            return (false, "Cần ít nhất 1 dòng model trước khi hoàn tất.");
        if (p.FlagAllModel && p.PointValAllModel <= 0)
            return (false, "Cần giá trị khuyến mại chung > 0 trước khi hoàn tất.");

        p.Status = CarPromotionStatus.Finished;
        if (!string.IsNullOrWhiteSpace(remark)) p.Remark = remark.Trim();

        // Cắt ngày kết thúc của chương trình đang hiệu lực trước đó về (ngày bắt đầu mới − 1).
        var today = DateTime.Today;
        if (p.EffDateStart <= today)
        {
            var prev = await db.CarPromotions.IgnoreQueryFilters()
                .Where(x => x.Id != p.Id && x.DealerCode == p.DealerCode && x.Status == CarPromotionStatus.Finished
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

    // Huỷ chương trình — port từ Prm_CarNew_Cancel. Chỉ huỷ được từ Chờ duyệt/Đã duyệt.
    public async Task<(bool ok, string msg)> CancelAsync(int id, string? remark)
    {
        var p = await db.CarPromotions.FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return (false, "Không tìm thấy chương trình.");
        if (p.Status != CarPromotionStatus.Pending && p.Status != CarPromotionStatus.Approved)
            return (false, "Chỉ huỷ được chương trình đang Chờ duyệt hoặc Đã duyệt.");
        p.Status = CarPromotionStatus.Cancelled;
        if (!string.IsNullOrWhiteSpace(remark)) p.Remark = remark.Trim();
        await db.SaveChangesAsync();
        return (true, "Đã huỷ chương trình.");
    }

    public Task<CarPromotion?> ActivePromotionAsync(string dealerCode)
    {
        var today = DateTime.Today;
        var code = (dealerCode ?? "").Trim().ToUpper();
        return db.CarPromotions.Include(p => p.Details)
            .Where(p => p.DealerCode == code && p.Status == CarPromotionStatus.Finished
                && p.EffDateStart <= today && p.EffDateEnd >= today)
            .OrderBy(p => p.EffDateStart).FirstOrDefaultAsync();
    }

    // Tính giá trị khuyến mại cho một model — port từ Prm_CarNew_CalcPrm.
    public async Task<CarPromoCalcOutcome> CalcAsync(string dealerCode, string modelCode)
    {
        if (string.IsNullOrWhiteSpace(dealerCode)) return new(false, "Cần mã đại lý.", 0, null);
        if (string.IsNullOrWhiteSpace(modelCode)) return new(false, "Cần mã model.", 0, null);
        var p = await ActivePromotionAsync(dealerCode);
        if (p == null) return new(false, "Không có chương trình khuyến mại đang hiệu lực.", 0, null);

        modelCode = modelCode.Trim().ToUpper();
        if (p.FlagAllModel) return new(true, "Đủ điều kiện áp dụng.", p.PointValAllModel, modelCode);

        var d = p.Details.FirstOrDefault(x => x.ModelCode == modelCode);
        if (d == null) return new(false, $"Model {modelCode} không thuộc chương trình.", 0, modelCode);
        return new(true, "Đủ điều kiện áp dụng.", d.PointVal, modelCode);
    }
}
