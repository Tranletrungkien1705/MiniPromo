using Microsoft.EntityFrameworkCore;
using MiniPromo.Data;
using MiniPromo.Models;

namespace MiniPromo.Services;

// Kết quả sinh mã voucher: danh sách mã + số thứ tự cuối cùng đã dùng.
public record VoucherIdGen(bool ok, string msg, List<string> codes, long lastSeq);

public interface IVoucherIdService
{
    // Danh sách mã voucher đã sinh (mới nhất trước).
    Task<List<VoucherIdSequence>> SequencesAsync();

    // Sinh một mã voucher mới (tăng số thứ tự, mã hoá base36 + checksum).
    Task<VoucherIdGen> GenerateAsync(int amount, DateTime? at);

    // Kiểm tra một mã voucher có đúng định dạng + checksum hợp lệ hay không.
    (bool ok, string msg) Validate(string voucherNo);
}

/// <summary>
/// Nghiệp vụ sinh mã voucher (port từ Seq_VoucherID_GetByAmount + Mst_VoucherID của hệ Loyalty).
/// Mã gồm 12 ký tự hệ cơ số 36 (0-9, A-Z):
///   {VerGen 2}{Năm 2}{Tháng 1}{Ngày 1}{NgẫuNhiên 5}{Checksum 1}
/// - VerGen: phiên bản sinh mã (mặc định "01").
/// - Năm/Tháng/Ngày: giá trị ngày sinh mã đổi sang base36 (độ dài cố định 2/1/1).
/// - NgẫuNhiên: (số thứ tự % 10.000.000) đổi sang base36, độ dài cố định 5.
/// - Checksum: tổng giá trị base36 của 11 ký tự đầu, lấy dư 36, đổi sang base36 1 ký tự.
/// Nguồn: Seq.cs (Seq_VoucherID_GetByAmount) + Utils.cs (CMyBase36.To36/To10).
/// </summary>
public class VoucherIdService(AppDbContext db) : IVoucherIdService
{
    private const int BaseSize = 36;
    private const long MaxSeq = 10_000_000;

    public Task<List<VoucherIdSequence>> SequencesAsync() =>
        db.VoucherIdSequences.OrderByDescending(s => s.Seq).ToListAsync();

    public async Task<VoucherIdGen> GenerateAsync(int amount, DateTime? at)
    {
        if (amount <= 0) return new(false, "Số lượng mã cần sinh phải > 0.", new(), 0);
        if (amount > 1000) return new(false, "Mỗi lần chỉ sinh tối đa 1.000 mã.", new(), 0);

        var dtime = (at ?? DateTime.UtcNow);
        var last = await db.VoucherIdSequences.OrderByDescending(s => s.Seq).FirstOrDefaultAsync();
        long seq = last?.Seq ?? 0;

        var codes = new List<string>();
        for (int i = 0; i < amount; i++)
        {
            seq += 1;
            var code = BuildCode(seq, dtime);
            codes.Add(code);
            db.VoucherIdSequences.Add(new VoucherIdSequence { Seq = seq, VoucherNo = code, GeneratedAt = dtime });
        }
        await db.SaveChangesAsync();
        return new(true, $"Đã sinh {codes.Count} mã voucher.", codes, seq);
    }

    // Kiểm tra định dạng + checksum của một mã voucher.
    public (bool ok, string msg) Validate(string voucherNo)
    {
        if (string.IsNullOrWhiteSpace(voucherNo)) return (false, "Cần nhập mã voucher.");
        var s = voucherNo.Trim().ToUpper();
        if (s.Length != 12) return (false, "Mã voucher phải gồm 12 ký tự.");
        foreach (var ch in s)
            if (!((ch >= '0' && ch <= '9') || (ch >= 'A' && ch <= 'Z')))
                return (false, "Mã voucher chỉ gồm chữ số và chữ cái A-Z.");

        var expected = Checksum(s[..11]);
        if (s[11].ToString() != expected) return (false, "Mã voucher sai checksum.");
        return (true, "Mã voucher hợp lệ.");
    }

    // Sinh mã từ số thứ tự + mốc thời gian (theo công thức nguồn).
    private static string BuildCode(long seq, DateTime dtime)
    {
        var verGen = "01";
        var year36 = To36(dtime.Year, 2);
        var month36 = To36(dtime.Month, 1);
        var day36 = To36(dtime.Day, 1);
        var random36 = To36((int)(seq % MaxSeq), 5);
        var head = verGen + year36 + month36 + day36 + random36;   // 11 ký tự đầu
        return head + Checksum(head);
    }

    // Checksum: tổng giá trị base36 của các ký tự, lấy dư 36, đổi sang base36 1 ký tự.
    private static string Checksum(string head)
    {
        int sum = 0;
        foreach (var ch in head) sum += To10(ch);
        return To36(sum % BaseSize, 1);
    }

    // Đổi số nguyên sang chuỗi base36 (0-9, A-Z), độ dài cố định (pad '0' bên trái).
    private static string To36(int n, int length)
    {
        if (n < 0) n = 0;
        var s = "";
        for (int t = n; t > 0; t /= BaseSize) s = Digit(t % BaseSize) + s;
        if (s.Length == 0) s = "0";
        if (s.Length < length) s = new string('0', length - s.Length) + s;
        return s.Length > length ? s[^length..] : s;
    }

    // Đổi một ký tự base36 sang giá trị số.
    private static int To10(char ch) => ch <= '9' ? ch - '0' : ch - 'A' + 10;

    private static char Digit(int n) => n < 10 ? (char)('0' + n) : (char)('A' + n - 10);
}