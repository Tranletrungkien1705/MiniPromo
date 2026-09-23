using Microsoft.EntityFrameworkCore;
using MiniPromo.Data;
using MiniPromo.Models;

namespace MiniPromo.Services;

public record RedeemOutcome(bool ok, string msg, decimal pointUsed, decimal pointRemain, int qtyUseRemain);
public record VoucherStat(Voucher Voucher, int Redemptions, decimal PointUsed);

public interface IVoucherService
{
    Task<List<Voucher>> VouchersAsync();
    Task<Voucher?> GetVoucherAsync(int id);
    Task<Voucher?> GetByCodeAsync(string code);            // xuyên tenant cho tra cứu công khai
    Task<(bool ok, string msg, int id)> CreateVoucherAsync(Voucher v);
    Task<(bool ok, string msg)> SetActiveAsync(int id, bool active);
    Task<List<VoucherRedemption>> RedemptionsAsync(int? voucherId);
    Task<RedeemOutcome> RedeemAsync(string voucherCode, decimal? amount, string? memberNo);
    Task<VoucherStat> StatAsync(int voucherId);
}

/// <summary>
/// Nghiệp vụ mã giảm giá / điểm voucher (port từ Crd_MemberVoucher của hệ Loyalty).
/// Quy tắc dùng: voucher phải Active, chưa hết hạn, còn điểm và còn lượt.
/// Mỗi lần dùng trừ tối đa PointLimit (không vượt điểm còn lại) và giảm 1 lượt.
/// </summary>
public class VoucherService(AppDbContext db) : IVoucherService
{
    public Task<List<Voucher>> VouchersAsync() =>
        db.Vouchers.OrderByDescending(v => v.Id).ToListAsync();

    public Task<Voucher?> GetVoucherAsync(int id) =>
        db.Vouchers.FirstOrDefaultAsync(v => v.Id == id);

    public Task<Voucher?> GetByCodeAsync(string code) =>
        db.Vouchers.IgnoreQueryFilters().FirstOrDefaultAsync(v => v.Code == code);

    public async Task<(bool ok, string msg, int id)> CreateVoucherAsync(Voucher v)
    {
        if (string.IsNullOrWhiteSpace(v.Name)) return (false, "Cần tên voucher.", 0);
        if (string.IsNullOrWhiteSpace(v.Code)) v.Code = "VC" + Guid.NewGuid().ToString("N")[..6].ToUpper();
        v.Code = v.Code.Trim().ToUpper();
        if (await db.Vouchers.IgnoreQueryFilters().AnyAsync(x => x.Code == v.Code)) return (false, "Mã voucher đã tồn tại.", 0);
        if (v.PointTotal <= 0) return (false, "Điểm voucher phải > 0.", 0);
        if (v.PointLimit <= 0) v.PointLimit = v.PointTotal;      // mặc định: dùng tối đa bằng tổng điểm
        if (v.QtyUseLimit <= 0) v.QtyUseLimit = 1;
        v.PointRemain = v.PointTotal;                            // điểm còn lại khởi tạo = tổng
        v.QtyUseRemain = v.QtyUseLimit;
        db.Vouchers.Add(v); await db.SaveChangesAsync();
        return (true, "Đã tạo voucher.", v.Id);
    }

    public async Task<(bool ok, string msg)> SetActiveAsync(int id, bool active)
    {
        var v = await db.Vouchers.FirstOrDefaultAsync(x => x.Id == id);
        if (v == null) return (false, "Không tìm thấy voucher.");
        v.Active = active; await db.SaveChangesAsync();
        return (true, active ? "Đã kích hoạt voucher." : "Đã tạm dừng voucher.");
    }

    public Task<List<VoucherRedemption>> RedemptionsAsync(int? voucherId)
    {
        var q = db.VoucherRedemptions.Include(r => r.Voucher).AsQueryable();
        if (voucherId.HasValue) q = q.Where(r => r.VoucherId == voucherId.Value);
        return q.OrderByDescending(r => r.Id).Take(500).ToListAsync();
    }

    // Dùng voucher: kiểm tra điều kiện áp dụng rồi trừ điểm + giảm lượt (chống vượt hạn mức).
    public async Task<RedeemOutcome> RedeemAsync(string voucherCode, decimal? amount, string? memberNo)
    {
        if (string.IsNullOrWhiteSpace(voucherCode)) return new(false, "Cần nhập mã voucher.", 0, 0, 0);
        var v = await GetByCodeAsync(voucherCode.Trim().ToUpper());
        if (v == null) return new(false, "Voucher không tồn tại.", 0, 0, 0);
        if (!v.Active) return new(false, "Voucher đang tạm dừng.", 0, v.PointRemain, v.QtyUseRemain);
        if (v.IsExpired) return new(false, "Voucher đã hết hạn.", 0, v.PointRemain, v.QtyUseRemain);
        if (v.QtyUseRemain <= 0) return new(false, "Voucher đã hết lượt sử dụng.", 0, v.PointRemain, 0);
        if (v.PointRemain <= 0) return new(false, "Voucher đã hết điểm.", 0, 0, v.QtyUseRemain);

        // Điểm dùng = min(hạn mức mỗi lần, điểm còn lại, số tiền cần trừ nếu có).
        var use = v.PointLimit;
        if (amount.HasValue && amount.Value > 0) use = Math.Min(use, amount.Value);
        use = Math.Min(use, v.PointRemain);
        if (use <= 0) return new(false, "Không có điểm để sử dụng.", 0, v.PointRemain, v.QtyUseRemain);

        // Kiểm tra lại trên bản ghi mới (chống đua/nhiều tab).
        var fresh = await db.Vouchers.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == v.Id);
        if (fresh == null || !fresh.IsUsable) return new(false, "Voucher không còn khả dụng.", 0, v.PointRemain, v.QtyUseRemain);

        fresh.PointRemain -= use;
        fresh.QtyUseRemain -= 1;
        db.VoucherRedemptions.Add(new VoucherRedemption
        {
            OrgId = fresh.OrgId, VoucherId = fresh.Id, MemberNo = memberNo ?? fresh.MemberNo,
            PointUsed = use, PointRemainAfter = fresh.PointRemain, QtyUseRemainAfter = fresh.QtyUseRemain
        });
        await db.SaveChangesAsync();
        return new(true, $"Đã dùng {Ui.Money(use)} từ voucher.", use, fresh.PointRemain, fresh.QtyUseRemain);
    }

    public async Task<VoucherStat> StatAsync(int voucherId)
    {
        var v = await db.Vouchers.FirstAsync(x => x.Id == voucherId);
        var reds = await db.VoucherRedemptions.Where(r => r.VoucherId == voucherId).ToListAsync();
        return new VoucherStat(v, reds.Count, reds.Sum(r => r.PointUsed));
    }
}
