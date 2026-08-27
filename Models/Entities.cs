namespace MiniPromo.Models;

public interface IOrgOwned { Guid OrgId { get; set; } }

public enum CampaignStatus { Draft = 0, Running = 1, Ended = 2 }
public enum PlayResult { Lose = 0, Win = 1 }
public enum ClaimStatus { None = 0, Pending = 1, Claimed = 2 }

public class Org
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Campaign : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string Code { get; set; } = "";               // Mã công khai để người dùng tham gia
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTime FromDate { get; set; } = DateTime.Today;
    public DateTime ToDate { get; set; } = DateTime.Today.AddMonths(1);
    public CampaignStatus Status { get; set; } = CampaignStatus.Draft;
    public int LoseWeight { get; set; } = 100;           // Trọng số "không trúng" trong vòng quay
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<Prize> Prizes { get; set; } = new();

    public bool IsLiveNow => Status == CampaignStatus.Running && DateTime.Today >= FromDate && DateTime.Today <= ToDate;
}

public class Prize : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public int CampaignId { get; set; }
    public Campaign? Campaign { get; set; }
    public string Name { get; set; } = "";
    public string Tier { get; set; } = "";               // Hạng giải (Nhất/Nhì/Ba...)
    public decimal Value { get; set; }                   // Giá trị giải (đ)
    public int Quantity { get; set; }                    // Tổng số suất
    public int Awarded { get; set; }                     // Đã phát
    public int Weight { get; set; } = 1;                 // Trọng số xác suất khi còn suất

    public int Remaining => Math.Max(0, Quantity - Awarded);
}

public class Entry : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public int CampaignId { get; set; }
    public Campaign? Campaign { get; set; }
    public string Code { get; set; } = "";               // Mã tem/QR người dùng nhập (1 mã chơi 1 lần)
    public string? CustomerName { get; set; }
    public string? Phone { get; set; }
    public PlayResult Result { get; set; }
    public int? PrizeId { get; set; }
    public Prize? Prize { get; set; }
    public string? PrizeName { get; set; }               // Snapshot tên giải lúc trúng
    public ClaimStatus Claim { get; set; } = ClaimStatus.None;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
