namespace MiniPromo.Models;

public interface IOrgOwned { Guid OrgId { get; set; } }

public enum CampaignStatus { Draft = 0, Running = 1, Ended = 2 }
public enum PlayResult { Lose = 0, Win = 1 }
public enum ClaimStatus { None = 0, Pending = 1, Claimed = 2 }

// Trạng thái voucher (mã giảm giá / điểm voucher) — theo nguồn Crd_MemberVoucher.
public enum VoucherStatus { Inactive = 0, Active = 1, Expired = 2, UsedUp = 3 }

// Vòng đời chương trình voucher (mã giảm giá) — theo nguồn Prm_VoucherNewCar.
public enum VoucherProgramStatus { Pending = 0, Approved = 1, Finished = 2, Cancelled = 3 }

// Vòng đời chương trình khuyến mại mua xe mới — theo nguồn Prm_CarNew (PRMCNStatus).
public enum CarPromotionStatus { Pending = 0, Approved = 1, Finished = 2, Cancelled = 3 }

// Vòng đời chương trình khuyến mại chung — theo nguồn Prm_Promotion (PRMStatus).
public enum PromotionStatus { Pending = 0, Approved = 1, Finished = 2, Cancelled = 3 }

// Vòng đời chương trình giới thiệu xe — theo nguồn Prm_CarRecommend (PRMCRStatus).
public enum CarRecommendStatus { Pending = 0, Approved = 1, Finished = 2, Cancelled = 3 }

// "Khuyến mại theo" — theo nguồn Prm_Promotion.PRMMainType (PRMMainType).
public enum PromotionMainType { Order = 0, Product = 1, ProductAndOrder = 2 }

// "Hình thức khuyến mại" — theo nguồn Prm_Promotion.PRMPrdType (PromotionPrmType).
public enum PromotionPrmType { Order = 0, Product = 1, ProductUPDc = 2, ProductUPDcByQty = 3, Voucher = 4 }

// Loại điều kiện áp dụng (scope) — gom các bảng Prm_*Scope của nguồn về một bảng duy nhất.
public enum PromotionScopeType { Date = 0, DayOfWeek = 1, Time = 2, Month = 3, Day = 4, Org = 5, User = 6, CustomerGroup = 7 }

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

// Chương trình khuyến mại mua xe mới — port từ Prm_CarNew.
// Vòng đời: Chờ duyệt → Đã duyệt → Hoàn tất / Đã huỷ. Mỗi đại lý (DLCPCode) chỉ có 1 chương trình
// hiệu lực tại một thời điểm; chương trình mới phải bắt đầu từ hôm nay và sau chương trình trước.
// Giá trị khuyến mại: áp dụng chung cho tất cả model (PointValAllModel) hoặc theo từng model (Details).
public class CarPromotion : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string Code { get; set; } = "";                 // PRMCNCode
    public string Name { get; set; } = "";                 // PRMCNName
    public string DealerCode { get; set; } = "";           // DLCPCode — đại lý áp dụng
    public DateTime EffDateStart { get; set; } = DateTime.Today;
    public DateTime EffDateEnd { get; set; } = DateTime.Today.AddMonths(1);
    public bool FlagAllModel { get; set; } = true;          // Áp dụng cho tất cả model
    public decimal PointValAllModel { get; set; }           // Giá trị khuyến mại khi áp dụng tất cả model
    public CarPromotionStatus Status { get; set; } = CarPromotionStatus.Pending;
    public string? Remark { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<CarPromotionDtl> Details { get; set; } = new();

    public bool IsLiveNow => Status == CarPromotionStatus.Finished && DateTime.Today >= EffDateStart && DateTime.Today <= EffDateEnd;
}

// Dòng chi tiết theo model — port từ Prm_CarNewDtl + Prm_CarNewSpec.
public class CarPromotionDtl : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public int CarPromotionId { get; set; }
    public CarPromotion? CarPromotion { get; set; }
    public string ModelCode { get; set; } = "";            // Model áp dụng
    public decimal PointVal { get; set; }                   // Giá trị khuyến mại cho model này
    public string? Remark { get; set; }
}

// Chương trình khuyến mại chung — port từ Prm_Promotion của hệ Loyalty.
// Vòng đời: Chờ duyệt → Đã duyệt → Hoàn tất / Đã huỷ. Điều kiện áp dụng (tháng/ngày/thứ/giờ/chi nhánh/
// người tạo/nhóm khách hàng) lưu ở PromotionScope; hình thức giảm giá ở PromotionPrm; điều kiện
// số lượng/tiền hàng ở PromotionMain. Cờ FlagParallel cho phép áp dụng đồng thời với chương trình khác.
public class PromotionProgram : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string Code { get; set; } = "";                 // PRMCode — mã chương trình (người dùng nhập)
    public string Name { get; set; } = "";                 // PRMName
    public PromotionMainType MainType { get; set; } = PromotionMainType.Order;   // PRMMainType
    public PromotionPrmType PrmType { get; set; } = PromotionPrmType.Order;      // PRMPrdType
    public decimal BudgetVal { get; set; }                  // Ngân sách
    public DateTime EffDTimeStart { get; set; } = DateTime.Today;
    public DateTime EffDTimeEnd { get; set; } = DateTime.Today.AddMonths(1);
    public bool FlagParallel { get; set; }                  // Được áp dụng đồng thời với chương trình khác
    public bool FlagMulti { get; set; }                     // Nhân khuyến mại theo số lượng mua
    public bool FlagAllOrg { get; set; } = true;            // Tất cả chi nhánh
    public bool FlagAllUserCode { get; set; } = true;       // Tất cả người tạo
    public bool FlagAllCustomerGrp { get; set; } = true;    // Tất cả nhóm khách hàng
    public bool FlagAllMonth { get; set; } = true;          // Tất cả tháng
    public bool FlagAllDay { get; set; } = true;            // Tất cả ngày
    public bool FlagAllDayOfWeek { get; set; } = true;      // Tất cả thứ
    public bool FlagAllTime { get; set; } = true;           // Tất cả giờ
    public PromotionStatus Status { get; set; } = PromotionStatus.Pending;
    public string? Remark { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<PromotionScope> Scopes { get; set; } = new();
    public List<PromotionPrm> Prms { get; set; } = new();
    public List<PromotionMain> Mains { get; set; } = new();

    public bool IsLiveNow => Status == PromotionStatus.Finished && DateTime.Today >= EffDTimeStart.Date && DateTime.Today <= EffDTimeEnd.Date;
}

// Một dòng điều kiện áp dụng — gom Prm_DateScope/Prm_DayOfWeekScope/Prm_TimeScope/Prm_MonthScope/
// Prm_DayScope/Prm_OrgScope/Prm_UserScope/Prm_CustomerGroupScope về một bảng có phân loại.
public class PromotionScope : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public int PromotionProgramId { get; set; }
    public PromotionProgram? PromotionProgram { get; set; }
    public PromotionScopeType ScopeType { get; set; }
    public string Value { get; set; } = "";               // Ngày (yyyy-MM-dd) / Thứ (0-6) / Tháng (1-12) / Ngày (1-31) / Mã chi nhánh / UserCode / Nhóm KH
    public string? ValueEnd { get; set; }                   // Giờ kết thúc (chỉ dùng cho ScopeType.Time, dạng HH:mm)
    public bool Active { get; set; } = true;
}

// Hình thức khuyến mại — port từ Prm_PromotionPrm.
// Giảm giá sản phẩm (UPDc/UPRateDc/UPDcMax) và/hoặc giảm giá đơn hàng (ValOrdDc/ValOrdRateDc/ValOrdDcMax).
public class PromotionPrm : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public int PromotionProgramId { get; set; }
    public PromotionProgram? PromotionProgram { get; set; }
    public int Idx { get; set; }                            // Thứ tự dòng
    public int Qty { get; set; }                            // Số lượng áp dụng
    public decimal UPDc { get; set; }                       // Giảm giá sản phẩm theo tiền
    public decimal UPRateDc { get; set; }                   // Giảm giá sản phẩm theo %
    public decimal UPDcMax { get; set; }                    // Mức giảm tối đa khi giảm theo %
    public decimal ValOrdDc { get; set; }                   // Giảm giá đơn hàng theo tiền
    public decimal ValOrdRateDc { get; set; }               // Giảm giá đơn hàng theo %
    public decimal ValOrdDcMax { get; set; }                // Mức giảm tối đa khi giảm đơn hàng theo %
    public bool FlagActive { get; set; } = true;
    public string? Remark { get; set; }
}

// Điều kiện số lượng/tiền hàng — port từ Prm_PromotionMain.
public class PromotionMain : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public int PromotionProgramId { get; set; }
    public PromotionProgram? PromotionProgram { get; set; }
    public int Idx { get; set; }
    public int Qty { get; set; }                            // Số lượng tối thiểu
    public decimal Amount { get; set; }                     // Số tiền tối thiểu
    public decimal TotalValOrd { get; set; }                // Tổng tiền hàng tối thiểu
    public bool FlagActive { get; set; } = true;
}

// Chương trình giới thiệu xe — port từ Prm_CarRecommend của hệ Loyalty.
// Vòng đời: Chờ duyệt → Đã duyệt → Hoàn tất / Đã huỷ. Mỗi đại lý (DLCPCode) chỉ có 1 chương trình
// hiệu lực tại một thời điểm; chương trình mới phải bắt đầu từ hôm nay và sau chương trình trước.
// Giá trị thưởng: áp dụng chung cho tất cả model (PointValAllModel) hoặc theo từng model (Details).
public class CarRecommend : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string Code { get; set; } = "";                 // PRMCRCode
    public string Name { get; set; } = "";                 // PRMCRName
    public string DealerCode { get; set; } = "";           // DLCPCode — đại lý áp dụng
    public DateTime EffDateStart { get; set; } = DateTime.Today;
    public DateTime EffDateEnd { get; set; } = DateTime.Today.AddMonths(1);
    public bool FlagAllModel { get; set; } = true;          // Áp dụng cho tất cả model
    public decimal PointValAllModel { get; set; }           // Giá trị thưởng khi áp dụng tất cả model
    public CarRecommendStatus Status { get; set; } = CarRecommendStatus.Pending;
    public string? Remark { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<CarRecommendDtl> Details { get; set; } = new();

    public bool IsLiveNow => Status == CarRecommendStatus.Finished && DateTime.Today >= EffDateStart && DateTime.Today <= EffDateEnd;
}

// Dòng chi tiết theo model — port từ Prm_CarRecommendDtl + Prm_CarRecommendSpec.
public class CarRecommendDtl : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public int CarRecommendId { get; set; }
    public CarRecommend? CarRecommend { get; set; }
    public string ModelCode { get; set; } = "";            // Model áp dụng
    public decimal PointVal { get; set; }                   // Giá trị thưởng cho model này
    public string? Remark { get; set; }
}
