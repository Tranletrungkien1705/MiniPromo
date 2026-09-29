using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Di trú SQLite tự động — vá BẪY #28 của fleet _labs.
///
/// VẤN ĐỀ: <c>EnsureCreated()</c> là NO-OP khi file .db ĐÃ TỒN TẠI. Mọi bảng/cột thêm vào model
/// sau lần tạo DB đầu tiên sẽ KHÔNG được tạo. Build vẫn 0 Errors, nhưng chạy lên là chết:
///     SQLite Error 1: 'no such table: X'
/// Đo thật trên máy 150 ngày 29/09/2026: 11/37 app fleet đang chết đúng vì lỗi này.
///
/// CÁCH LÀM (không gõ tay DDL — gõ tay là lệch kiểu cột với model):
///   1. BẢNG thiếu: lấy DDL từ chính metadata EF bằng GenerateCreateScript(), rồi đổi
///      "CREATE TABLE" -> "CREATE TABLE IF NOT EXISTS" và "CREATE INDEX" -> "... IF NOT EXISTS".
///   2. CỘT thiếu trên bảng ĐÃ CÓ: hỏi PRAGMA table_info trước, chỉ ALTER cột chưa có.
///      GenerateCreateScript KHÔNG làm được việc này vì nó chỉ sinh CREATE.
///   3. Idempotent: chạy lần hai không lỗi.
///   4. KHÔNG try/catch mù ở vòng ngoài — nuốt lỗi khác đi là giấu bệnh. Chỉ bỏ qua đúng
///      trường hợp "duplicate column name" vì đó là đua tranh vô hại khi nhiều tiến trình cùng mở.
/// </summary>
public static class MigrateSqliteAuto
{
    public static async Task RunAsync(DbContext db)
    {
        if (!db.Database.IsSqlite()) return;   // Postgres đã có đường ALTER riêng của từng app

        // ----- 1) bảng/chỉ mục thiếu -----
        var script = db.Database.GenerateCreateScript();
        foreach (var raw in script.Split(';'))
        {
            var sql = raw.Trim();
            if (sql.Length == 0) continue;
            if (sql.StartsWith("CREATE TABLE ", StringComparison.OrdinalIgnoreCase))
                sql = "CREATE TABLE IF NOT EXISTS " + sql.Substring("CREATE TABLE ".Length);
            else if (sql.StartsWith("CREATE INDEX ", StringComparison.OrdinalIgnoreCase))
                sql = "CREATE INDEX IF NOT EXISTS " + sql.Substring("CREATE INDEX ".Length);
            else if (sql.StartsWith("CREATE UNIQUE INDEX ", StringComparison.OrdinalIgnoreCase))
                sql = "CREATE UNIQUE INDEX IF NOT EXISTS " + sql.Substring("CREATE UNIQUE INDEX ".Length);
            else
                continue;   // bỏ qua mọi câu khác (PRAGMA, INSERT seed của EF...) cho an toàn
            await db.Database.ExecuteSqlRawAsync(sql);
        }

        // ----- 2) cột thiếu trên bảng đã có -----
        foreach (var et in db.Model.GetEntityTypes())
        {
            var bang = et.GetTableName();
            if (string.IsNullOrEmpty(bang)) continue;

            var dangCo = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = db.Database.GetDbConnection().CreateCommand())
            {
                if (cmd.Connection!.State != System.Data.ConnectionState.Open)
                    await db.Database.OpenConnectionAsync();
                cmd.CommandText = "SELECT name FROM pragma_table_info('" + bang.Replace("'", "''") + "')";
                using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync()) dangCo.Add(rd.GetString(0));
            }
            if (dangCo.Count == 0) continue;   // bảng chưa tồn tại thì bước 1 đã tạo đủ cột

            foreach (var pr in et.GetProperties())
            {
                var cot = pr.GetColumnName();
                if (string.IsNullOrEmpty(cot) || dangCo.Contains(cot)) continue;
                var kieu = pr.GetColumnType();
                if (string.IsNullOrEmpty(kieu))
                    kieu = pr.GetRelationalTypeMapping()?.StoreType ?? "TEXT";
                // cột thêm sau PHẢI cho phép NULL: SQLite không cho ADD COLUMN NOT NULL mà không mặc định
                var sql = "ALTER TABLE \"" + bang + "\" ADD COLUMN \"" + cot + "\" " + kieu + " NULL";
                try
                {
                    await db.Database.ExecuteSqlRawAsync(sql);
                }
                catch (Exception ex) when (ex.Message.Contains("duplicate column name",
                                                              StringComparison.OrdinalIgnoreCase))
                {
                    // hai tiến trình cùng thêm một cột — vô hại, bỏ qua ĐÚNG trường hợp này
                }
            }
        }
    }
}
