using Microsoft.EntityFrameworkCore;
using MiniPromo.Data;
using MiniPromo.Models;

namespace MiniPromo.Services;

// Kết quả tra cứu quy tắc tích điểm dịch vụ cho một loại chi phí.
// point = điểm tích được, discountRate = % chiết khấu dịch vụ, countService = có tính lượt ghé thăm dịch vụ.
public record ExpensePointRule(bool ok, string msg, string expenseType, string expenseTypeName,
    decimal amount, decimal point, decimal discountRate, bool countService, bool pointRank);

public interface IPolicyExpenseTypeService
{
    // Danh mục loại chi phí (Mst_ExpenseType).
    Task<List<ExpenseType>> ExpenseTypesAsync();
    Task<(bool ok, string msg, int id)> CreateExpenseTypeAsync(ExpenseType t);
    Task<(bool ok, string msg)> SetExpenseTypeActiveAsync(int id, bool active);

    // Chính sách đối tượng tích điểm dịch vụ (Mst_PolicyExpenseType).
    Task<List<PolicyExpenseType>> PoliciesAsync(string? policyNo);
    Task<List<PolicyExpenseType>> RowsOfPolicyAsync(string policyNo);

    // Lưu toàn bộ danh sách dòng của một chính sách theo cơ chế "xoá sạch rồi ghi lại" (Mst_PolicyExpenseType_SaveX).
    Task<(bool ok, string msg, int count)> SavePolicyAsync(string policyNo, List<PolicyExpenseType> rows);

    // Tra cứu quy tắc tích điểm dịch vụ cho một loại chi phí (theo chính sách đang hoạt động).
    Task<ExpensePointRule> CalcAsync(string expenseType, decimal amount);
}

/// <summary>
/// Nghiệp vụ chính sách đối tượng tích điểm dịch vụ (port từ Mst_PolicyExpenseType + Mst_ExpenseType
/// của hệ Loyalty, logic Mst_PolicyExpenseType_SaveX). Quy tắc:
///  - Mỗi dòng gắn một loại chi phí dịch vụ (ExpenseType) với quy tắc tích điểm/chiết khấu.
///  - Loại chi phí phải tồn tại và đang hoạt động (Mst_ExpenseType_CheckDB).
///  - DiscountRate trong khoảng 0..100; nếu FlagDiscount = false thì DiscountRate phải = 0.
///  - Lưu theo cơ chế "xoá sạch rồi ghi lại": thay thế toàn bộ dòng của một PolicyExpenseTypeNo.
/// </summary>
public class PolicyExpenseTypeService(AppDbContext db) : IPolicyExpenseTypeService
{
    public Task<List<ExpenseType>> ExpenseTypesAsync() =>
        db.ExpenseTypes.OrderByDescending(t => t.Id).ToListAsync();

    public async Task<(bool ok, string msg, int id)> CreateExpenseTypeAsync(ExpenseType t)
    {
        if (string.IsNullOrWhiteSpace(t.Name)) return (false, "Cần tên loại chi phí.", 0);
        if (string.IsNullOrWhiteSpace(t.Code)) t.Code = "ET" + Guid.NewGuid().ToString("N")[..6].ToUpper();
        t.Code = t.Code.Trim().ToUpper();
        if (await db.ExpenseTypes.IgnoreQueryFilters().AnyAsync(x => x.Code == t.Code))
            return (false, "Mã loại chi phí đã tồn tại.", 0);

        db.ExpenseTypes.Add(t); await db.SaveChangesAsync();
        return (true, "Đã tạo loại chi phí.", t.Id);
    }

    public async Task<(bool ok, string msg)> SetExpenseTypeActiveAsync(int id, bool active)
    {
        var t = await db.ExpenseTypes.FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return (false, "Không tìm thấy loại chi phí.");
        t.FlagActive = active; await db.SaveChangesAsync();
        return (true, active ? "Đã bật loại chi phí." : "Đã tạm dừng loại chi phí.");
    }

    public Task<List<PolicyExpenseType>> PoliciesAsync(string? policyNo)
    {
        var q = db.PolicyExpenseTypes.AsQueryable();
        if (!string.IsNullOrWhiteSpace(policyNo)) q = q.Where(x => x.PolicyExpenseTypeNo == policyNo.Trim().ToUpper());
        return q.OrderBy(x => x.PolicyExpenseTypeNo).ThenBy(x => x.ExpenseType).ToListAsync();
    }

    public Task<List<PolicyExpenseType>> RowsOfPolicyAsync(string policyNo)
    {
        var code = (policyNo ?? "").Trim().ToUpper();
        return db.PolicyExpenseTypes.Where(x => x.PolicyExpenseTypeNo == code)
            .OrderBy(x => x.ExpenseType).ToListAsync();
    }

    // Lưu toàn bộ dòng của một chính sách — port từ Mst_PolicyExpenseType_SaveX (clear-all → insert-all).
    public async Task<(bool ok, string msg, int count)> SavePolicyAsync(string policyNo, List<PolicyExpenseType> rows)
    {
        if (string.IsNullOrWhiteSpace(policyNo)) return (false, "Cần mã chính sách (PolicyExpenseTypeNo).", 0);
        var code = policyNo.Trim().ToUpper();
        rows ??= new();

        // Kiểm tra từng dòng trước khi ghi (giống nguồn: kiểm tra hết rồi mới ghi).
        var seen = new HashSet<string>();
        foreach (var r in rows)
        {
            if (string.IsNullOrWhiteSpace(r.ExpenseType)) return (false, "Cần mã loại chi phí cho mỗi dòng.", 0);
            r.ExpenseType = r.ExpenseType.Trim().ToUpper();
            if (!seen.Add(r.ExpenseType)) return (false, $"Loại chi phí {r.ExpenseType} bị lặp trong chính sách.", 0);

            var et = await db.ExpenseTypes.FirstOrDefaultAsync(x => x.Code == r.ExpenseType);
            if (et == null) return (false, $"Không tìm thấy loại chi phí {r.ExpenseType}.", 0);
            if (!et.FlagActive) return (false, $"Loại chi phí {r.ExpenseType} đang tạm dừng.", 0);

            if (r.DiscountRate < 0 || r.DiscountRate > 100) return (false, "Tỉ lệ chiết khấu phải trong khoảng 0..100.", 0);
            if (!r.FlagDiscount && r.DiscountRate != 0)
                return (false, $"Loại chi phí {r.ExpenseType}: không chiết khấu thì tỉ lệ chiết khấu phải bằng 0.", 0);
        }

        // Xoá sạch rồi ghi lại toàn bộ dòng của chính sách này.
        var old = await db.PolicyExpenseTypes.Where(x => x.PolicyExpenseTypeNo == code).ToListAsync();
        if (old.Count > 0) db.PolicyExpenseTypes.RemoveRange(old);

        foreach (var r in rows)
        {
            r.Id = 0;
            r.PolicyExpenseTypeNo = code;
            r.FlagActive = true;
            db.PolicyExpenseTypes.Add(r);
        }
        await db.SaveChangesAsync();
        return (true, $"Đã lưu chính sách {code} với {rows.Count} dòng.", rows.Count);
    }

    // Tra cứu quy tắc tích điểm dịch vụ cho một loại chi phí theo chính sách đang hoạt động.
    public async Task<ExpensePointRule> CalcAsync(string expenseType, decimal amount)
    {
        if (string.IsNullOrWhiteSpace(expenseType)) return new(false, "Cần mã loại chi phí.", "", "", amount, 0, 0, false, false);
        if (amount < 0) return new(false, "Số tiền không được âm.", expenseType.Trim().ToUpper(), "", amount, 0, 0, false, false);
        var code = expenseType.Trim().ToUpper();

        var row = await db.PolicyExpenseTypes
            .Where(x => x.ExpenseType == code && x.FlagActive)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync();
        if (row == null) return new(false, $"Loại chi phí {code} không có trong chính sách tích điểm dịch vụ.", code, "", amount, 0, 0, false, false);

        var point = row.FlagPoint ? Math.Round(amount * row.AmountRate, 2) : 0;
        if (row.MaxAccumulationPoint > 0 && point > row.MaxAccumulationPoint) point = row.MaxAccumulationPoint;
        return new(true, "Đã tra cứu quy tắc tích điểm dịch vụ.", code, row.ExpenseTypeNameActual, amount, point, row.DiscountRate, row.FlagCountService, row.FlagPointRank);
    }
}
