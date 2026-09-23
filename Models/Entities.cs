namespace MiniPromo.Models;

public interface IOrgOwned { Guid OrgId { get; set; } }

public enum CampaignStatus { Draft = 0, Running = 1, Ended = 2 }
public enum PlayResult { Lose = 0, Win = 1 }
public enum ClaimStatus { None = 0, Pending = 1, Claimed = 2 }

// Trạng thái voucher (mã giảm giá / điểm voucher) — theo nguồn Crd_MemberVoucher.
public enum VoucherStatus { Inactive = 0, Active = 1, Expired = 2, UsedUp = 3 }

// Vòng đời chương trình voucher (mã giảm giá) — theo nguồn Prm_VoucherNewCar.
public enum VoucherProgramStatus { Pending = 0, Approved = 1, Finished = 2, Cancelled = 3 }

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

// Mã giảm giá / điểm voucher (port từ Crd_MemberVoucher của hệ Loyalty).
// Người dùng dùng voucher để trừ điểm vào hoá đơn; mỗi lần dùng bị chặn bởi hạn mức/lượt/hạn dùng.
public class Voucher : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string Code { get; set; } = "";               // Mã voucher công khai (tra cứu xuyên tenant)
    public string Name { get; set; } = "";
    public string? MemberNo { get; set; }                 // Mã hội viên sở hữu (tuỳ chọn)
    public int? VoucherProgramId { get; set; }            // Chương trình đã phát voucher này (nếu có)
    public VoucherProgram? VoucherProgram { get; set; }
    public string? ModelCode { get; set; }                // Model xe được phát (nếu phát từ chương trình)
    public decimal PointTotal { get; set; }               // Điểm voucher ban đầu
    public decimal PointRemain { get; set; }              // Điểm voucher còn lại
    public decimal PointLimit { get; set; }               // Điểm tối đa mỗi lần sử dụng
    public int QtyUseLimit { get; set; } = 1;             // Giới hạn số lần sử dụng
    public int QtyUseRemain { get; set; } = 1;            // Số lần sử dụng còn lại
    public DateTime ExpireDate { get; set; } = DateTime.Today.AddMonths(1);
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<VoucherRedemption> Redemptions { get; set; } = new();

    public bool IsExpired => DateTime.Today > ExpireDate;
    public bool IsUsable => Active && !IsExpired && PointRemain > 0 && QtyUseRemain > 0;
    public VoucherStatus Status => !Active ? VoucherStatus.Inactive
        : IsExpired ? VoucherStatus.Expired
        : QtyUseRemain <= 0 || PointRemain <= 0 ? VoucherStatus.UsedUp
        : VoucherStatus.Active;
}

// Nhật ký một lần sử dụng voucher (port từ Crd_MemberVoucherTransaction).
public class VoucherRedemption : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public int VoucherId { get; set; }
    public Voucher? Voucher { get; set; }
    public string? MemberNo { get; set; }
    public decimal PointUsed { get; set; }                // Điểm đã trừ trong lần này
    public decimal PointRemainAfter { get; set; }         // Điểm còn lại sau khi dùng
    public int QtyUseRemainAfter { get; set; }            // Số lượt còn lại sau khi dùng
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// Chương trình voucher (mã giảm giá) — port từ Prm_VoucherNewCar.
// Điều kiện áp dụng: khoảng ngày hiệu lực, thời hạn voucher sau khi phát,
// giới hạn ngày kể từ ngày giao xe, và danh sách model áp dụng (hoặc tất cả model).
public class VoucherProgram : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string Code { get; set; } = "";                 // PrmVoucherCode
    public string Name { get; set; } = "";                 // PrmVoucherName
    public DateTime EffDateStart { get; set; } = DateTime.Today;
    public DateTime EffDateEnd { get; set; } = DateTime.Today.AddMonths(1);
    public int ValidityPeriod { get; set; }                 // Số ngày voucher còn hiệu lực sau khi phát
    public int QtyDayLimitFDlvDate { get; set; }            // Giới hạn số ngày kể từ ngày giao xe để được phát
    public bool FlagAllModel { get; set; } = true;          // Áp dụng cho tất cả model
    public decimal PointVoucherAllModel { get; set; }       // Giá trị voucher khi áp dụng tất cả model
    public decimal PointUseLimitAllModel { get; set; }      // Hạn mức sử dụng khi áp dụng tất cả model
    public VoucherProgramStatus Status { get; set; } = VoucherProgramStatus.Pending;
    public string? Remark { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<VoucherProgramDtl> Details { get; set; } = new();

    public bool IsLiveNow => Status == VoucherProgramStatus.Finished && DateTime.Today >= EffDateStart && DateTime.Today <= EffDateEnd;
}

// Dòng chi tiết theo model — port từ Prm_VoucherNewCarDtl + Prm_VoucherNewCarSpec.
public class VoucherProgramDtl : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public int VoucherProgramId { get; set; }
    public VoucherProgram? VoucherProgram { get; set; }
    public string ModelCode { get; set; } = "";            // Model áp dụng
    public decimal PointVoucher { get; set; }               // Giá trị voucher cho model này
    public decimal PointUseLimit { get; set; }              // Hạn mức sử dụng cho model này
    public string? Remark { get; set; }
}
