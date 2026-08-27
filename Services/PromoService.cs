using Microsoft.EntityFrameworkCore;
using MiniPromo.Data;
using MiniPromo.Models;

namespace MiniPromo.Services;

public record PlayOutcome(bool ok, string msg, bool win, string? prizeName, decimal prizeValue);
public record CampaignStat(Campaign Campaign, int Plays, int Wins, int PrizesTotal, int PrizesLeft, decimal ValueAwarded);
public record PromoDash(int Campaigns, int Running, int TotalPlays, int TotalWins, decimal ValueAwarded, List<CampaignStat> Top);

public interface IPromoService
{
    Task<List<Campaign>> CampaignsAsync();
    Task<Campaign?> GetCampaignAsync(int id);
    Task<Campaign?> GetByCodeAsync(string code);            // xuyên tenant cho trang chơi công khai
    Task<(bool ok, string msg, int id)> CreateCampaignAsync(Campaign c);
    Task<(bool ok, string msg)> SetStatusAsync(int id, CampaignStatus status);
    Task<(bool ok, string msg)> AddPrizeAsync(Prize p);
    Task<CampaignStat> StatAsync(int campaignId);
    Task<List<Entry>> EntriesAsync(int? campaignId, PlayResult? result);
    Task<PlayOutcome> PlayAsync(string campaignCode, string playCode, string? name, string? phone);
    Task<(bool ok, string msg)> SetClaimAsync(int entryId, ClaimStatus status);
    Task<PromoDash> DashboardAsync();
}

public class PromoService(AppDbContext db) : IPromoService
{
    private static readonly Random _rng = new();

    public Task<List<Campaign>> CampaignsAsync() =>
        db.Campaigns.Include(c => c.Prizes).OrderByDescending(c => c.Id).ToListAsync();

    public Task<Campaign?> GetCampaignAsync(int id) =>
        db.Campaigns.Include(c => c.Prizes).FirstOrDefaultAsync(c => c.Id == id);

    public Task<Campaign?> GetByCodeAsync(string code) =>
        db.Campaigns.IgnoreQueryFilters().Include(c => c.Prizes).FirstOrDefaultAsync(c => c.Code == code);

    public async Task<(bool ok, string msg, int id)> CreateCampaignAsync(Campaign c)
    {
        if (string.IsNullOrWhiteSpace(c.Name)) return (false, "Cần tên chiến dịch.", 0);
        if (string.IsNullOrWhiteSpace(c.Code)) c.Code = "KM" + Guid.NewGuid().ToString("N")[..6].ToUpper();
        if (await db.Campaigns.IgnoreQueryFilters().AnyAsync(x => x.Code == c.Code)) return (false, "Mã chiến dịch đã tồn tại.", 0);
        db.Campaigns.Add(c); await db.SaveChangesAsync();
        return (true, "Đã tạo chiến dịch.", c.Id);
    }

    public async Task<(bool ok, string msg)> SetStatusAsync(int id, CampaignStatus status)
    {
        var c = await db.Campaigns.Include(x => x.Prizes).FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return (false, "Không tìm thấy.");
        if (status == CampaignStatus.Running && c.Prizes.Count == 0) return (false, "Cần ít nhất 1 giải trước khi chạy.");
        c.Status = status; await db.SaveChangesAsync();
        return (true, $"Chiến dịch: {status}.");
    }

    public async Task<(bool ok, string msg)> AddPrizeAsync(Prize p)
    {
        if (string.IsNullOrWhiteSpace(p.Name)) return (false, "Cần tên giải.");
        if (p.Quantity <= 0) return (false, "Số suất phải > 0.");
        if (p.Weight <= 0) p.Weight = 1;
        if (!await db.Campaigns.AnyAsync(c => c.Id == p.CampaignId)) return (false, "Không tìm thấy chiến dịch.");
        db.Prizes.Add(p); await db.SaveChangesAsync();
        return (true, "Đã thêm giải.");
    }

    // Quét mã → quay số. Đảm bảo KHÔNG phát vượt số suất (kiểm tra lại Remaining trước khi tăng Awarded).
    public async Task<PlayOutcome> PlayAsync(string campaignCode, string playCode, string? name, string? phone)
    {
        if (string.IsNullOrWhiteSpace(playCode)) return new(false, "Cần nhập mã dự thưởng.", false, null, 0);
        var c = await GetByCodeAsync(campaignCode);
        if (c == null) return new(false, "Chiến dịch không tồn tại.", false, null, 0);
        if (!c.IsLiveNow) return new(false, "Chiến dịch chưa mở hoặc đã kết thúc.", false, null, 0);

        var org = c.OrgId;
        playCode = playCode.Trim();
        // 1 mã chơi 1 lần / chiến dịch (dùng IgnoreQueryFilters vì play có thể xuyên tenant qua mã công khai)
        if (await db.Entries.IgnoreQueryFilters().AnyAsync(e => e.CampaignId == c.Id && e.Code == playCode))
            return new(false, "Mã này đã được sử dụng.", false, null, 0);

        // Vòng quay có trọng số: các giải còn suất + bucket "trượt".
        var prizes = c.Prizes.Where(p => p.Remaining > 0).ToList();
        Prize? won = null;
        if (prizes.Count > 0)
        {
            long total = c.LoseWeight + prizes.Sum(p => (long)p.Weight);
            long roll = (long)(_rng.NextDouble() * total);
            long acc = c.LoseWeight;                         // bucket trượt đứng trước
            if (roll >= acc)
            {
                foreach (var p in prizes)
                {
                    acc += p.Weight;
                    if (roll < acc) { won = p; break; }
                }
            }
        }

        var entry = new Entry
        {
            OrgId = org, CampaignId = c.Id, Code = playCode, CustomerName = name, Phone = phone,
            Result = PlayResult.Lose, Claim = ClaimStatus.None
        };

        if (won != null)
        {
            // Kiểm tra lại suất (chống phát vượt do đua/nhiều tab).
            var fresh = await db.Prizes.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == won.Id);
            if (fresh != null && fresh.Awarded < fresh.Quantity)
            {
                fresh.Awarded += 1;
                entry.Result = PlayResult.Win; entry.PrizeId = fresh.Id; entry.PrizeName = fresh.Name;
                entry.Claim = ClaimStatus.Pending;
                db.Entries.Add(entry);
                await db.SaveChangesAsync();
                return new(true, "Chúc mừng bạn đã trúng thưởng!", true, fresh.Name, fresh.Value);
            }
        }

        db.Entries.Add(entry);
        await db.SaveChangesAsync();
        return new(true, "Chúc bạn may mắn lần sau.", false, null, 0);
    }

    public async Task<CampaignStat> StatAsync(int campaignId)
    {
        var c = await db.Campaigns.Include(x => x.Prizes).FirstAsync(x => x.Id == campaignId);
        var plays = await db.Entries.CountAsync(e => e.CampaignId == campaignId);
        var wins = await db.Entries.CountAsync(e => e.CampaignId == campaignId && e.Result == PlayResult.Win);
        var awardedVal = c.Prizes.Sum(p => p.Value * p.Awarded);
        return new CampaignStat(c, plays, wins, c.Prizes.Sum(p => p.Quantity), c.Prizes.Sum(p => p.Remaining), awardedVal);
    }

    public Task<List<Entry>> EntriesAsync(int? campaignId, PlayResult? result)
    {
        var q = db.Entries.Include(e => e.Campaign).AsQueryable();
        if (campaignId.HasValue) q = q.Where(e => e.CampaignId == campaignId.Value);
        if (result.HasValue) q = q.Where(e => e.Result == result.Value);
        return q.OrderByDescending(e => e.Id).Take(500).ToListAsync();
    }

    public async Task<(bool ok, string msg)> SetClaimAsync(int entryId, ClaimStatus status)
    {
        var e = await db.Entries.FirstOrDefaultAsync(x => x.Id == entryId);
        if (e == null || e.Result != PlayResult.Win) return (false, "Không hợp lệ.");
        e.Claim = status; await db.SaveChangesAsync();
        return (true, status == ClaimStatus.Claimed ? "Đã trao giải." : "Đã cập nhật.");
    }

    public async Task<PromoDash> DashboardAsync()
    {
        var camps = await db.Campaigns.Include(c => c.Prizes).ToListAsync();
        var plays = await db.Entries.CountAsync();
        var wins = await db.Entries.CountAsync(e => e.Result == PlayResult.Win);
        decimal awarded = 0;
        var stats = new List<CampaignStat>();
        foreach (var c in camps)
        {
            var p = await db.Entries.CountAsync(e => e.CampaignId == c.Id);
            var w = await db.Entries.CountAsync(e => e.CampaignId == c.Id && e.Result == PlayResult.Win);
            var av = c.Prizes.Sum(x => x.Value * x.Awarded);
            awarded += av;
            stats.Add(new CampaignStat(c, p, w, c.Prizes.Sum(x => x.Quantity), c.Prizes.Sum(x => x.Remaining), av));
        }
        return new PromoDash(camps.Count, camps.Count(c => c.IsLiveNow), plays, wins, awarded,
            stats.OrderByDescending(s => s.Plays).Take(6).ToList());
    }
}
