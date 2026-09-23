using Microsoft.EntityFrameworkCore;
using MiniPromo.Data;
using MiniPromo.Models;

namespace MiniPromo.Services;

// Kết quả kiểm tra một mã giảm giá có hợp lệ cho một đơn hàng hay không.
public record DiscountCheck(bool ok, string msg, string code, decimal discountAmount, DiscountCodeType discountType);

// Kết quả áp dụng mã giảm giá: số tiền giảm thực tế + số lượt còn lại sau khi dùng.
public record DiscountApply(bool ok, string msg, string code, decimal discount, decimal orderAmount, decimal payable, int remainQty);

public interface IDiscountCodeService
{
    // Danh sách mã giảm giá.
    Task<List<DiscountCode>> CodesAsync();
    Task<DiscountCode?> GetCodeAsync(int id);
    Task<DiscountCode?> GetByCodeAsync(string code);       // xuyên tenant cho tra cứu công khai
    Task<(bool ok, string msg, int id)> CreateCodeAsync(DiscountCode c);
    Task<(bool ok, string msg)> SetEnabledAsync(int id, bool enabled);

    // Ánh xạ đại lý ↔ mã giảm giá.
    Task<List<DealerDiscountMap>> MapsAsync(string? dealerCode);
    Task<(bool ok, string msg, int id)> AddMapAsync(DealerDiscountMap m);
    Task<(bool ok, string msg)> SetMapActiveAsync(int id, bool active);

    // Kiểm tra mã giảm giá có hợp lệ cho một đơn hàng (tồn tại + đang bật + còn lượt + trong hiệu lực).
    Task<DiscountCheck> CheckAsync(string code, decimal orderAmount, DateTime? at);

    // Áp dụng mã giảm giá: tính số tiền giảm và trừ 1 lượt sử dụng.
    Task<DiscountApply> ApplyAsync(string code, decimal orderAmount, DateTime? at);
}

/// <summary>
/// Nghiệp vụ mã giảm giá (port từ Inos_DiscountCode + Map_DealerDiscount của hệ Loyalty).
/// Quy tắc dùng (nguồn Master.cs): mã phải tồn tại và đang bật (Enabled) mới hợp lệ cho đơn hàng.
/// Bổ sung theo model nguồn: còn lượt sử dụng (RemainQty) và trong khoảng ngày hiệu lực.
/// Kiểu giảm giá: Percent (giảm theo % giá trị đơn) hoặc Absolute (giảm số tiền cố định).
/// </summary>
public class DiscountCodeService(AppDbContext db) : IDiscountCodeService
{
    public Task<List<DiscountCode>> CodesAsync() =>
        db.DiscountCodes.OrderByDescending(c => c.Id).ToListAsync();

    public Task<DiscountCode?> GetCodeAsync(int id) =>
        db.DiscountCodes.FirstOrDefaultAsync(c => c.Id == id);

    public Task<DiscountCode?> GetByCodeAsync(string code) =>
        db.DiscountCodes.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Code == code);

    public async Task<(bool ok, string msg, int id)> CreateCodeAsync(DiscountCode c)
    {
        if (string.IsNullOrWhiteSpace(c.Code)) return (false, "Cần mã giảm giá.", 0);
        c.Code = c.Code.Trim().ToUpper();
        if (await db.DiscountCodes.IgnoreQueryFilters().AnyAsync(x => x.Code == c.Code))
            return (false, "Mã giảm giá đã tồn tại.", 0);
        if (c.DiscountAmount <= 0) return (false, "Giá trị giảm phải > 0.", 0);
        if (c.DiscountType == DiscountCodeType.Percent && c.DiscountAmount > 100)
            return (false, "Giảm theo % không được vượt 100.", 0);
        if (c.RemainQty < 0) return (false, "Số lượt còn lại không được âm.", 0);
        if (c.EffectDateTo.Date < c.EffectDateFrom.Date)
            return (false, "Ngày kết thúc phải sau ngày bắt đầu.", 0);

        db.DiscountCodes.Add(c); await db.SaveChangesAsync();
        return (true, "Đã tạo mã giảm giá.", c.Id);
    }

    public async Task<(bool ok, string msg)> SetEnabledAsync(int id, bool enabled)
    {
        var c = await db.DiscountCodes.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return (false, "Không tìm thấy mã giảm giá.");
        c.Enabled = enabled; await db.SaveChangesAsync();
        return (true, enabled ? "Đã bật mã giảm giá." : "Đã tắt mã giảm giá.");
    }

    public Task<List<DealerDiscountMap>> MapsAsync(string? dealerCode)
    {
        var q = db.DealerDiscountMaps.AsQueryable();
        if (!string.IsNullOrWhiteSpace(dealerCode)) q = q.Where(m => m.DealerCode == dealerCode.Trim().ToUpper());
        return q.OrderByDescending(m => m.Id).ToListAsync();
    }

    public async Task<(bool ok, string msg, int id)> AddMapAsync(DealerDiscountMap m)
    {
        if (string.IsNullOrWhiteSpace(m.DealerCode)) return (false, "Cần mã đại lý.", 0);
        if (string.IsNullOrWhiteSpace(m.DiscountCode)) return (false, "Cần mã giảm giá.", 0);
        m.DealerCode = m.DealerCode.Trim().ToUpper();
        m.DiscountCode = m.DiscountCode.Trim().ToUpper();
        if (await db.DealerDiscountMaps.AnyAsync(x => x.DealerCode == m.DealerCode && x.DiscountCode == m.DiscountCode))
            return (false, "Đại lý đã được gán mã giảm giá này.", 0);

        db.DealerDiscountMaps.Add(m); await db.SaveChangesAsync();
        return (true, "Đã gán mã giảm giá cho đại lý.", m.Id);
    }

    public async Task<(bool ok, string msg)> SetMapActiveAsync(int id, bool active)
    {
        var m = await db.DealerDiscountMaps.FirstOrDefaultAsync(x => x.Id == id);
        if (m == null) return (false, "Không tìm thấy ánh xạ đại lý.");
        m.FlagActive = active; await db.SaveChangesAsync();
        return (true, active ? "Đã bật ánh xạ." : "Đã tạm dừng ánh xạ.");
    }

    // Kiểm tra mã giảm giá có hợp lệ cho một đơn hàng.
    public async Task<DiscountCheck> CheckAsync(string code, decimal orderAmount, DateTime? at)
    {
        if (string.IsNullOrWhiteSpace(code)) return new(false, "Cần nhập mã giảm giá.", "", 0, DiscountCodeType.Percent);
        var c = await GetByCodeAsync(code.Trim().ToUpper());
        if (c == null) return new(false, "Mã giảm giá không tồn tại.", "", 0, DiscountCodeType.Percent);
        if (!c.Enabled) return new(false, "Mã giảm giá đang tắt.", c.Code, 0, c.DiscountType);
        var day = (at ?? DateTime.Today).Date;
        if (day < c.EffectDateFrom.Date || day > c.EffectDateTo.Date)
            return new(false, "Mã giảm giá ngoài khoảng hiệu lực.", c.Code, 0, c.DiscountType);
        if (c.RemainQty <= 0) return new(false, "Mã giảm giá đã hết lượt sử dụng.", c.Code, 0, c.DiscountType);

        var discount = CalcDiscount(c, orderAmount);
        return new(true, "Mã giảm giá hợp lệ.", c.Code, discount, c.DiscountType);
    }

    // Áp dụng mã giảm giá: tính số tiền giảm, trừ 1 lượt sử dụng (chống vượt hạn mức).
    public async Task<DiscountApply> ApplyAsync(string code, decimal orderAmount, DateTime? at)
    {
        var check = await CheckAsync(code, orderAmount, at);
        if (!check.ok) return new(false, check.msg, check.code, 0, orderAmount, orderAmount, 0);

        var fresh = await db.DiscountCodes.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Code == check.code);
        if (fresh == null || !fresh.IsLiveNow)
            return new(false, "Mã giảm giá không còn khả dụng.", check.code, 0, orderAmount, orderAmount, fresh?.RemainQty ?? 0);

        var discount = CalcDiscount(fresh, orderAmount);
        fresh.RemainQty -= 1;
        await db.SaveChangesAsync();

        var payable = Math.Max(0, orderAmount - discount);
        return new(true, $"Đã áp dụng mã {fresh.Code}, giảm {Ui.Money(discount)}.", fresh.Code, discount, orderAmount, payable, fresh.RemainQty);
    }

    // Tính số tiền giảm: Percent → orderAmount × %/100; Absolute → số tiền cố định (không vượt giá trị đơn).
    private static decimal CalcDiscount(DiscountCode c, decimal orderAmount)
    {
        if (orderAmount <= 0) return 0;
        var d = c.DiscountType == DiscountCodeType.Percent
            ? orderAmount * c.DiscountAmount / 100m
            : c.DiscountAmount;
        return Math.Min(d, orderAmount);
    }
}