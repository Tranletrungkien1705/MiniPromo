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

// Loại đối tượng áp dụng của phạm vi sản phẩm — theo nguồn PromotionRefType (Const.Main.BE.cs).
public enum PromotionRefType { Product = 0, ProductGroup = 1, VoucherIssue = 2, Voucher = 3 }

// Vai trò của dòng phạm vi sản phẩm — theo nguồn Prm_PromotionMainSpec (điều kiện) và Prm_PromotionPrmSpec (hình thức).
public enum PromotionProductScopeKind { Main = 0, Prm = 1 }

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
    public List<PromotionProductScope> ProductScopes { get; set; } = new();

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

// Phạm vi sản phẩm/nhóm sản phẩm áp dụng — port từ Prm_PromotionMainSpec + Prm_PromotionPrmSpec.
// Mỗi dòng gắn một đối tượng (sản phẩm / nhóm sản phẩm / voucher) vào chương trình khuyến mại.
// Kind phân biệt dòng thuộc điều kiện (Main) hay thuộc hình thức khuyến mại (Prm); MapIdx dùng cho
// trường hợp "giảm giá theo số lượng mua" để lấy danh sách sản phẩm theo từng dòng hình thức.
public class PromotionProductScope : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public int PromotionProgramId { get; set; }
    public PromotionProgram? PromotionProgram { get; set; }
    public PromotionProductScopeKind Kind { get; set; } = PromotionProductScopeKind.Main;
    public int Idx { get; set; }                            // Thứ tự dòng (khớp với PromotionPrm/PromotionMain.Idx)
    public PromotionRefType RefType { get; set; } = PromotionRefType.Product;
    public string RefCode { get; set; } = "";              // Mã sản phẩm / nhóm sản phẩm / voucher (hệ thống)
    public string? RefName { get; set; }                    // Tên do người dùng nhập
    public int? MapIdx { get; set; }                        // Ánh xạ sang dòng hình thức khi giảm giá theo số lượng
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

// Trạng thái chương trình khuyến mại theo loại thẻ — theo nguồn Mst_PromotionProgram.FlagActive.
public enum CardPromotionProgramStatus { Inactive = 0, Active = 1 }

// Chương trình khuyến mại theo loại thẻ — port từ Mst_PromotionProgram của hệ Loyalty.
// Mỗi chương trình cấp một số lượng ưu đãi (Qty) cho từng loại thẻ (CardType); phạm vi đại lý
// áp dụng lưu ở CardPromotionProgramSpec (hoặc tất cả đại lý khi FlagAllDL).
// Số lượng đã dùng theo từng chương trình được ghi nhận ở CardPromotionUsage.
public class CardPromotionProgram : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string Code { get; set; } = "";                 // PrProgramCode
    public string Name { get; set; } = "";                 // PrProgramName
    public DateTime EffDateStart { get; set; } = DateTime.Today;
    public DateTime EffDateEnd { get; set; } = DateTime.Today.AddMonths(1);
    public bool FlagAllDL { get; set; } = true;             // Áp dụng cho tất cả đại lý
    public CardPromotionProgramStatus Status { get; set; } = CardPromotionProgramStatus.Inactive;
    public string? Remark { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<CardPromotionProgramDtl> Details { get; set; } = new();
    public List<CardPromotionProgramSpec> Dealers { get; set; } = new();

    public bool IsLiveNow => Status == CardPromotionProgramStatus.Active && DateTime.Today >= EffDateStart.Date && DateTime.Today <= EffDateEnd.Date;
}

// Dòng chi tiết theo loại thẻ — port từ Mst_PromotionProgramDtl.
public class CardPromotionProgramDtl : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public int CardPromotionProgramId { get; set; }
    public CardPromotionProgram? CardPromotionProgram { get; set; }
    public string CardType { get; set; } = "";             // Loại thẻ áp dụng
    public int Qty { get; set; }                            // Số lượng ưu đãi cấp cho loại thẻ
    public string? Unit { get; set; }                       // Đơn vị tính
    public bool FlagActive { get; set; } = true;
    public string? Remark { get; set; }
}

// Phạm vi đại lý áp dụng — port từ Mst_PromotionProgramSpec.
public class CardPromotionProgramSpec : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public int CardPromotionProgramId { get; set; }
    public CardPromotionProgram? CardPromotionProgram { get; set; }
    public string DealerCode { get; set; } = "";           // DLCode — đại lý áp dụng
}

// Nhật ký sử dụng ưu đãi của một giao dịch — port từ Crd_DealUsePromotion + Crd_DealUsePromotionDtl.
// Mỗi lần dùng ghi nhận số lượng ưu đãi đã dùng (QtyUsed) cho một chương trình theo loại thẻ.
// MemberNo dùng cho luật "1 ngày + 1 chương trình + 1 hội viên + 1 loại thẻ chỉ ghi nhận 1 ưu đãi".
public class CardPromotionUsage : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string DealNo { get; set; } = "";               // Số giao dịch
    public string DealerCode { get; set; } = "";           // DLCPCode — đại lý thực hiện
    public string CardNo { get; set; } = "";               // Số thẻ
    public string MemberNo { get; set; } = "";             // Mã hội viên (suy ra từ thẻ) — dùng cho luật theo ngày
    public string CardType { get; set; } = "";             // Loại thẻ
    public int CardPromotionProgramId { get; set; }
    public CardPromotionProgram? CardPromotionProgram { get; set; }
    public int QtyUsed { get; set; }                        // Số lượng ưu đãi đã dùng trong lần này
    public DateTime UsedAt { get; set; } = DateTime.UtcNow;
    public string? Remark { get; set; }
}

// Trạng thái chương trình tặng điểm sinh nhật — theo nguồn Mst_BirthPolicy.FlagActive.
public enum BirthdayPolicyStatus { Inactive = 0, Active = 1 }

// Chương trình tặng điểm sinh nhật — port từ Mst_BirthPolicy của hệ Loyalty.
// Vòng đời: Tạm dừng ↔ Đang bật (FlagActive). Điều kiện áp dụng: trong khoảng EffDateStart..EffDateEnd,
// cờ FlagPoint (chỉ tặng điểm), và mỗi loại thẻ (CardType) có mức điểm riêng ở BirthdayPolicyDtl.
// Điểm được quy đổi ra tiền theo tỷ lệ ParamValue (nguồn Mst_ParamSys.UNITPOINTTOMONEY).
public class BirthdayPolicy : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string Code { get; set; } = "";                 // BirthPolicyNo
    public string Name { get; set; } = "";                 // BirthPolicyName
    public DateTime EffDateStart { get; set; } = DateTime.Today;
    public DateTime EffDateEnd { get; set; } = DateTime.Today.AddMonths(1);
    public bool FlagPoint { get; set; } = true;             // Có tặng điểm sinh nhật
    public decimal ParamValue { get; set; } = 1;            // Tỷ lệ quy đổi điểm → tiền (UNITPOINTTOMONEY)
    public BirthdayPolicyStatus Status { get; set; } = BirthdayPolicyStatus.Inactive;
    public string? Remark { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<BirthdayPolicyDtl> Details { get; set; } = new();

    public bool IsLiveNow => Status == BirthdayPolicyStatus.Active && DateTime.Today >= EffDateStart.Date && DateTime.Today <= EffDateEnd.Date;
}

// Dòng chi tiết theo loại thẻ — port từ Mst_BirthPolicyDtl.
public class BirthdayPolicyDtl : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public int BirthdayPolicyId { get; set; }
    public BirthdayPolicy? BirthdayPolicy { get; set; }
    public string CardType { get; set; } = "";             // Loại thẻ áp dụng
    public decimal Point { get; set; }                      // Điểm tặng cho loại thẻ này
    public string? Remark { get; set; }
}

// Nhật ký tặng điểm sinh nhật — port từ Crd_CardTransaction (DealPointType = 'BIRTHDAY').
// Mỗi lần tặng ghi nhận điểm đã tặng (Point) và số tiền quy đổi (Amount) cho một hội viên/thẻ.
// Ràng buộc: mỗi hội viên chỉ được tặng 1 lần trong một năm.
public class BirthdayGrant : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string MemberNo { get; set; } = "";             // Mã hội viên
    public string CardNo { get; set; } = "";               // Số thẻ
    public string CardType { get; set; } = "";             // Loại thẻ dùng để tính điểm
    public string DealerCode { get; set; } = "";           // Đại lý được tặng điểm
    public int BirthdayPolicyId { get; set; }
    public BirthdayPolicy? BirthdayPolicy { get; set; }
    public decimal Point { get; set; }                      // Điểm đã tặng
    public decimal Amount { get; set; }                     // Số tiền quy đổi (Point × ParamValue)
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public string? Remark { get; set; }
}

// Kiểu ưu đãi của đợt phát hành voucher — theo nguồn FavorType (Const.Main.cs).
public enum IssueFavorType { Discount = 0, Freeship = 1 }

// Hình thức phát hành voucher — theo nguồn IssueForm (Const.Main.cs).
public enum IssueFormType { Sell = 0, Give = 1 }

// Trạng thái một voucher trong đợt phát hành — theo nguồn IssueStatus (Const.Main.cs).
public enum IssueVoucherStatus { Pending = 0, Issued = 1, Evicted = 2, Cancelled = 3, Used = 4 }

// Loại đối tượng áp dụng của điều kiện hàng hoá — theo nguồn IssueVoucherInsPrd_RefType.
public enum IssueRefType { Product = 0, ProductGroup = 1 }

// Đợt phát hành voucher — port từ Mst_IssueVoucher của hệ Loyalty.
// Một đợt phát hành gồm: thông tin chung (mã, tên, hiệu lực, số lượng, thời hạn sử dụng),
// kiểu ưu đãi (giảm giá / miễn phí giao hàng), hình thức phát hành (bán / tặng),
// phạm vi áp dụng (chi nhánh / người tạo đơn / nhóm khách hàng) và điều kiện hàng hoá.
// Vòng đời đợt: Tạm dừng ↔ Đang áp dụng (FlagActive).
public class IssueVoucher : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string Code { get; set; } = "";                 // IssueCode — mã đợt phát hành (người dùng nhập)
    public string Name { get; set; } = "";                 // IssueName
    public DateTime EffDateStart { get; set; } = DateTime.Today;
    public DateTime EffDateEnd { get; set; } = DateTime.Today.AddMonths(1);
    public int QtyVoucher { get; set; }                     // Số lượng voucher của đợt
    public int QtyDateUse { get; set; }                     // Thời hạn sử dụng (số ngày kể từ ngày phát)
    public IssueFavorType FavorType { get; set; } = IssueFavorType.Discount;   // Kiểu ưu đãi
    public IssueFormType IssueForm { get; set; } = IssueFormType.Give;         // Hình thức phát hành
    public bool FlagConditionUsePrd { get; set; } = true;   // true: chọn bất kỳ hàng hoá; false: chọn hàng/nhóm hàng
    public bool FlagScopeBranch { get; set; } = true;       // true: tất cả chi nhánh
    public bool FlagScopeOrderCreate { get; set; } = true;  // true: tất cả người tạo đơn
    public bool FlagScopeCusType { get; set; } = true;      // true: tất cả nhóm khách hàng
    public bool FlagActive { get; set; }                    // Trạng thái áp dụng (đang bật)
    public string? Remark { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<IssueVoucherDtl> Details { get; set; } = new();
    public List<IssueVoucherScope> Scopes { get; set; } = new();
    public List<IssueVoucherProduct> Products { get; set; } = new();
    public List<IssueVoucherPrice> Prices { get; set; } = new();

    // Đợt đang thực sự áp dụng: đang bật và trong khoảng hiệu lực (nguồn FlagShow).
    public bool IsLiveNow => FlagActive && DateTime.Today >= EffDateStart.Date && DateTime.Today <= EffDateEnd.Date;
}

// Một voucher đã phát trong đợt — port từ Mst_IssueVoucherDtl.
// Mỗi voucher có người nhận, ngày phát/hết hạn/sử dụng và trạng thái riêng.
public class IssueVoucherDtl : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public int IssueVoucherId { get; set; }
    public IssueVoucher? IssueVoucher { get; set; }
    public string VoucherNo { get; set; } = "";            // VoucherID — mã voucher (duy nhất)
    public string? Receiver { get; set; }                   // Reciever — người nhận
    public IssueVoucherStatus Status { get; set; } = IssueVoucherStatus.Pending;
    public DateTime? IssueDate { get; set; }                // Ngày phát hành
    public DateTime? ExpDate { get; set; }                  // Ngày hết hạn
    public DateTime? UseDate { get; set; }                  // Ngày sử dụng
    public string? OrderNo { get; set; }                    // Đơn hàng sử dụng voucher
    public string? Remark { get; set; }
}

// Phạm vi áp dụng của đợt — gom Mst_IssueVoucherScopeBranch/ScopeUser/ScopeCusGroup về một bảng có phân loại.
public enum IssueScopeType { Branch = 0, OrderCreate = 1, CustomerGroup = 2 }

public class IssueVoucherScope : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public int IssueVoucherId { get; set; }
    public IssueVoucher? IssueVoucher { get; set; }
    public IssueScopeType ScopeType { get; set; }
    public string Value { get; set; } = "";               // Mã chi nhánh / UserCode / nhóm khách hàng
}

// Điều kiện hàng hoá áp dụng — port từ Mst_IssueVoucherInsPrd.
public class IssueVoucherProduct : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public int IssueVoucherId { get; set; }
    public IssueVoucher? IssueVoucher { get; set; }
    public IssueRefType RefType { get; set; } = IssueRefType.Product;
    public string RefCode { get; set; } = "";             // Mã hàng / nhóm hàng
    public string? RefName { get; set; }                    // Tên hàng / nhóm hàng
}

// Cấu hình giá trị ưu đãi của đợt — port từ Mst_IssueVoucherInsPrice.
// IssueType phân biệt dòng thuộc hình thức phát hành hay điều kiện áp dụng.
public enum IssuePriceType { Issue = 0, Condition = 1 }

public class IssueVoucherPrice : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public int IssueVoucherId { get; set; }
    public IssueVoucher? IssueVoucher { get; set; }
    public IssuePriceType IssueType { get; set; } = IssuePriceType.Issue;
    public string IssueTypeDtl { get; set; } = "";         // Giảm giá / Giá bán / Giá trị đơn hàng
    public decimal UPDc { get; set; }                       // Giảm giá theo tiền
    public decimal UPRateDc { get; set; }                   // Giảm giá theo %
    public decimal UPDcMax { get; set; }                    // Mức giảm tối đa
    public string? Remark { get; set; }
}
// Loại áp dụng của tham số khuyến mại — theo nguồn Mst_ParamPromotionType.ParamPrType.
// Mỗi loại gắn một tên hiển thị (ParamPrTypeName) và dùng để phân nhóm tham số theo nghiệp vụ.
public class ParamPromotionType : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string Code { get; set; } = "";                 // ParamPrType — mã loại áp dụng
    public string Name { get; set; } = "";                 // ParamPrTypeName — tên loại áp dụng
    public bool FlagActive { get; set; } = true;           // Trạng thái áp dụng
    public string? Remark { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<ParamPromotion> Params { get; set; } = new();
}

// Tham số khuyến mại — port từ Mst_ParamPromotion của hệ Loyalty.
// Mỗi tham số gắn một chương trình (PrProgramCode) với một loại áp dụng (ParamPrType) và một
// khoảng ngày tương đối: QtyDateBefore ngày trước và QtyDateAfter ngày sau một mốc ngày tham chiếu.
// Dùng để xác định khoảng ngày hiệu lực của chương trình quanh một mốc (ví dụ ngày sinh, ngày giao xe).
public class ParamPromotion : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string ProgramCode { get; set; } = "";          // PrProgramCode — mã chương trình
    public string ProgramName { get; set; } = "";          // PrProgramName — tên chương trình
    public int ParamPromotionTypeId { get; set; }          // Loại áp dụng (ParamPrType)
    public ParamPromotionType? ParamPromotionType { get; set; }
    public int QtyDateBefore { get; set; }                  // Số ngày trước mốc tham chiếu
    public int QtyDateAfter { get; set; }                   // Số ngày sau mốc tham chiếu
    public bool FlagActive { get; set; } = true;           // Trạng thái áp dụng
    public string? Remark { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Khoảng ngày hiệu lực quanh một mốc tham chiếu (mốc − QtyDateBefore .. mốc + QtyDateAfter).
    public (DateTime start, DateTime end) Window(DateTime anchor) =>
        (anchor.Date.AddDays(-QtyDateBefore), anchor.Date.AddDays(QtyDateAfter));
}// Trạng thái chính sách xếp hạng thẻ — theo nguồn Mst_RankPolicy.FlagActive.
public enum RankPolicyStatus { Inactive = 0, Active = 1 }

// Hành động xếp hạng của thẻ — theo nguồn RankActionType (Const.Main.cs).
public enum RankActionType { Up = 0, Keep = 1, Down = 2 }

// Chính sách xếp hạng thẻ — port từ Mst_RankPolicy của hệ Loyalty.
// Mỗi dòng gắn một hạng thẻ (CardType) với một "bậc" (Value) và các ngưỡng tích luỹ để
// NÂNG hạng (PointUpBegin/QtyVisitUpBegin) hoặc DUY TRÌ hạng (PointKeepBegin/QtyVisitKeepBegin)
// trong khoảng thời gian duy trì QtyMonth tháng. Bậc cao hơn = Value lớn hơn.
// Nguồn: Mst_RankPolicy + logic Crd_CardRankPolicy_PerformX / Mst_RankPolicy_GetUp trong CardRank.cs.
public class RankPolicy : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string Code { get; set; } = "";                 // RankPolicyCode — mã chính sách xếp hạng
    public string CardType { get; set; } = "";             // Mã hạng thẻ áp dụng
    public int Value { get; set; }                          // Bậc xếp hạng (số càng lớn hạng càng cao)
    public decimal PointUpBegin { get; set; }               // Tích luỹ điểm nâng hạng từ
    public decimal PointUpEnd { get; set; }                 // Tích luỹ điểm nâng hạng đến
    public int QtyVisitUpBegin { get; set; }                // Số lần ghé thăm để nâng hạng từ
    public int QtyVisitUpEnd { get; set; }                  // Số lần ghé thăm để nâng hạng đến
    public decimal PointKeepBegin { get; set; }             // Tích luỹ điểm duy trì từ
    public decimal PointKeepEnd { get; set; }               // Tích luỹ điểm duy trì đến
    public int QtyVisitKeepBegin { get; set; }              // Số lần ghé thăm để duy trì từ
    public int QtyVisitKeepEnd { get; set; }                // Số lần ghé thăm để duy trì đến
    public int QtyMonth { get; set; } = 12;                 // Thời gian duy trì (tháng)
    public RankPolicyStatus Status { get; set; } = RankPolicyStatus.Inactive;
    public string? Remark { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsActive => Status == RankPolicyStatus.Active;
}

// Trạng thái chính sách quy đổi tiền dịch vụ → điểm — theo nguồn Mst_PolicyMoneyToPointService.FlagActive.
public enum PolicyMoneyToPointStatus { Inactive = 0, Active = 1 }

// Chính sách quy đổi tiền dịch vụ → điểm — port từ Mst_PolicyMoneyToPointService của hệ Loyalty.
// Vòng đời: Tạm dừng ↔ Đang bật (FlagActive). Điều kiện áp dụng: trong khoảng EffDateStart..EffDateEnd.
// Mỗi hạng thẻ (CardType) có tỷ lệ quy đổi riêng ở PolicyMoneyToPointDtl: cứ ConvertValue tiền dịch vụ
// thì được ConvertPoint điểm; ValueRankCardType là mốc doanh thu để tính 1 lượt xét hạng; DiscountRate là
// % chiết khấu dịch vụ. Nguồn: Mst_PolicyMoneyToPointService + Mst_PolicyMoneyToPointServiceDtl.
public class PolicyMoneyToPoint : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string Code { get; set; } = "";                 // PolicyCode — mã chính sách
    public string Name { get; set; } = "";                 // Tên chính sách (bổ sung cho dễ nhìn)
    public DateTime EffDateStart { get; set; } = DateTime.Today;
    public DateTime EffDateEnd { get; set; } = DateTime.Today.AddMonths(1);
    public PolicyMoneyToPointStatus Status { get; set; } = PolicyMoneyToPointStatus.Inactive;
    public string? Remark { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<PolicyMoneyToPointDtl> Details { get; set; } = new();

    public bool IsLiveNow => Status == PolicyMoneyToPointStatus.Active && DateTime.Today >= EffDateStart.Date && DateTime.Today <= EffDateEnd.Date;
}

// Dòng chi tiết theo hạng thẻ — port từ Mst_PolicyMoneyToPointServiceDtl.
public class PolicyMoneyToPointDtl : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public int PolicyMoneyToPointId { get; set; }
    public PolicyMoneyToPoint? PolicyMoneyToPoint { get; set; }
    public string CardType { get; set; } = "";             // Hạng thẻ áp dụng
    public decimal ConvertValue { get; set; }               // Giá trị quy đổi (số tiền)
    public decimal ConvertPoint { get; set; }               // Điểm quy đổi tương ứng
    public decimal ValueRankCardType { get; set; }          // Mốc doanh thu để tính 1 lượt xét hạng
    public decimal DiscountRate { get; set; }               // % chiết khấu dịch vụ (0..100)
    public bool FlagActive { get; set; } = true;
    public string? Remark { get; set; }

    // Tỷ lệ quy đổi: cứ ConvertValue tiền thì được ConvertPoint điểm.
    public decimal Rate => ConvertValue > 0 ? ConvertPoint / ConvertValue : 0;
}// Loại điểm của giao dịch chiết khấu — theo nguồn DealPointType (Const.Main.cs).
// DISCOUNTRO: chiết khấu dịch vụ (đối tượng thanh toán × hạng thẻ).
public enum MemberDiscountPointType { DiscountRO = 0 }

// Nhật ký giao dịch chiết khấu hội viên — port từ Crd_MemberDiscountTransaction của hệ Loyalty.
// Mỗi giao dịch (RefNo) ghi nhận giá trị chiết khấu (PointChTotal) tính trên số tiền được chiết khấu
// (AmountForDC) theo tỷ lệ chiết khấu của đối tượng thanh toán (PolicyDiscountRate) và của hạng thẻ
// áp dụng (CardTypeApply). Chỉ ghi nhận khi giá trị chiết khấu > 0.
// Nguồn: Crd_MemberDiscountTransaction + logic tính trong Card.Deal.cs
// (AmountDiscount = AmountForDC × dttt_DiscountRate/100 × ht_DiscountRate/100).
public class MemberDiscountTransaction : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string RefNo { get; set; } = "";                // Số giao dịch (DealSerRONo)
    public string DealerCode { get; set; } = "";           // DLCode — đại lý thực hiện
    public string MemberNo { get; set; } = "";             // Mã hội viên
    public string CardNo { get; set; } = "";               // Số thẻ
    public string CardTypeUse { get; set; } = "";          // Hạng thẻ sử dụng (đặc cách)
    public string CardTypeInit { get; set; } = "";         // Hạng thẻ gốc của hội viên
    public string CardTypeApply { get; set; } = "";        // Hạng thẻ áp dụng tính chiết khấu
    public MemberDiscountPointType DealPointType { get; set; } = MemberDiscountPointType.DiscountRO;
    public string? PolicyCode { get; set; }                // Mã chính sách quy đổi (Mst_PolicyMoneyToPointService)
    public decimal PolicyDiscountRate { get; set; }        // Tỷ lệ chiết khấu theo hạng thẻ áp dụng (%)
    public decimal AmountForDC { get; set; }               // Số tiền được chiết khấu
    public decimal PointChTotal { get; set; }              // Giá trị chiết khấu (tiền)
    public DateTime CreateDate { get; set; } = DateTime.Today;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? Remark { get; set; }
}// Loại khuyến mại theo — port từ Mst_PromotionMainType của hệ Loyalty.
// Danh mục "Khuyến mại theo" (PRMMainType): Đơn hàng / Hàng hóa / Hàng hóa và đơn hàng.
// Mỗi loại gắn một tên hiển thị (PRMMainTypeName) và trạng thái áp dụng (FlagActive).
public class PromotionMainTypeDef : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string Code { get; set; } = "";                 // PRMMainType — mã loại khuyến mại theo
    public string Name { get; set; } = "";                 // PRMMainTypeName — tên loại khuyến mại theo
    public bool FlagActive { get; set; } = true;           // Trạng thái áp dụng
    public string? Remark { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<PromotionPrmInMain> PrmInMains { get; set; } = new();
}

// Hình thức khuyến mại — port từ Mst_PromotionPrmType của hệ Loyalty.
// Danh mục "Hình thức khuyến mại" (PRMPrmType): Tặng hàng / Giảm giá hàng / Giảm giá bán theo SL mua /
// Giảm giá đơn hàng / Tặng voucher. Mỗi hình thức gắn một tên hiển thị (PRMPrmTypeName) và trạng thái áp dụng.
public class PromotionPrmTypeDef : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string Code { get; set; } = "";                 // PRMPrmType — mã hình thức khuyến mại
    public string Name { get; set; } = "";                 // PRMPrmTypeName — tên hình thức khuyến mại
    public bool FlagActive { get; set; } = true;           // Trạng thái áp dụng
    public string? Remark { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<PromotionPrmInMain> PrmInMains { get; set; } = new();
}

// Gắn hình thức khuyến mại vào loại khuyến mại theo — port từ Prm_PrmInMain của hệ Loyalty.
// Cho biết một hình thức khuyến mại (PRMPrmType) được phép dùng cho loại khuyến mại theo (PRMMainType) nào.
public class PromotionPrmInMain : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public int MainTypeId { get; set; }                    // Loại khuyến mại theo (PRMMainType)
    public PromotionMainTypeDef? MainType { get; set; }
    public int PrmTypeId { get; set; }                     // Hình thức khuyến mại (PRMPrmType)
    public PromotionPrmTypeDef? PrmType { get; set; }
    public bool FlagActive { get; set; } = true;           // Trạng thái áp dụng
    public string? Remark { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}// Kiểu giảm giá của mã giảm giá — theo nguồn Inos_DiscountCodeTypes.
// Percent: giảm theo % giá trị đơn; Absolute: giảm số tiền cố định.
public enum DiscountCodeType { Percent = 1, Absolute = 2 }

// Mã giảm giá — port từ Inos_DiscountCode của hệ Loyalty.
// Mỗi mã có số lượt sử dụng còn lại (RemainQty), kiểu giảm giá (Percent/Absolute) và giá trị giảm
// (DiscountAmount), trạng thái bật/tắt (Enabled) và khoảng ngày hiệu lực (EffectDateFrom..EffectDateTo).
// Quy tắc dùng (nguồn Master.cs): mã phải tồn tại và đang bật (Enabled) mới hợp lệ cho đơn hàng.
public class DiscountCode : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string Code { get; set; } = "";                 // Mã giảm giá (người dùng nhập)
    public string? Description { get; set; }               // Mô tả
    public DiscountCodeType DiscountType { get; set; } = DiscountCodeType.Percent;  // Kiểu giảm giá
    public decimal DiscountAmount { get; set; }            // Giá trị giảm (% hoặc số tiền)
    public int RemainQty { get; set; }                     // Số lượt sử dụng còn lại
    public bool Enabled { get; set; } = true;              // Trạng thái bật/tắt
    public DateTime EffectDateFrom { get; set; } = DateTime.Today;
    public DateTime EffectDateTo { get; set; } = DateTime.Today.AddMonths(1);
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Mã còn hiệu lực: đang bật, còn lượt và trong khoảng ngày hiệu lực.
    public bool IsLiveNow => Enabled && RemainQty > 0
        && DateTime.Today >= EffectDateFrom.Date && DateTime.Today <= EffectDateTo.Date;
}

// Ánh xạ đại lý ↔ mã giảm giá — port từ Map_DealerDiscount của hệ Loyalty.
// Cho biết một đại lý (DLCode) được phép dùng một mã giảm giá (DiscountCode) nào.
public class DealerDiscountMap : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string DealerCode { get; set; } = "";           // DLCode — đại lý áp dụng
    public string DiscountCode { get; set; } = "";         // Mã giảm giá được gán
    public bool FlagActive { get; set; } = true;           // Trạng thái áp dụng
    public string? Remark { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}// Loại giao dịch điểm của nghiệp vụ tặng điểm mua xe mới — theo nguồn DealPointType (Const.Main.cs).
// SALES: tặng điểm khi hội viên mua xe mới (điểm bán hàng, không phải điểm dịch vụ).
public enum CarPurchasePointType { Sales = 0 }

// Nhật ký tặng điểm mua xe mới — port từ Crd_Member_PerformBuyNewCar (Transaction.AddPoint.cs)
// + bảng Crd_CardTransaction (DealPointType = 'SALES').
// Khi hội viên mua xe mới, hệ thống cộng số điểm mua xe (PointBuyCar trên Crd_Member) vào thẻ
// đang APPROVE của hội viên, quy đổi ra tiền theo tỷ lệ UNITPOINTTOMONEY (Mst_ParamSys).
// Điểm có hạn dùng tới cuối tháng 12 năm kế tiếp (PointExpiryDTime).
public class CarPurchasePointGrant : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string RefNo { get; set; } = "";                // Số giao dịch (NEW.yyyyMMdd.HHmmss)
    public string MemberNo { get; set; } = "";             // Mã hội viên
    public string CardNo { get; set; } = "";               // Số thẻ (thẻ APPROVE của hội viên)
    public string CardTypeUse { get; set; } = "";          // Hạng thẻ sử dụng
    public string CardTypeInit { get; set; } = "";         // Hạng thẻ gốc của hội viên
    public string DealerCode { get; set; } = "";           // DLCode — đại lý ghi nhận
    public CarPurchasePointType DealPointType { get; set; } = CarPurchasePointType.Sales;
    public string? PrProgramCode { get; set; }             // Mã chương trình bán xe (nếu có)
    public decimal PointChTotal { get; set; }              // Điểm mua xe đã tặng (PointBuyCar)
    public decimal AmountChTotal { get; set; }             // Số tiền quy đổi (PointChTotal × ParamValue)
    public decimal ParamValue { get; set; } = 1;           // Tỷ lệ quy đổi điểm → tiền (UNITPOINTTOMONEY)
    public DateTime PointExpiryDTime { get; set; }         // Hạn dùng điểm (cuối tháng 12 năm kế tiếp)
    public DateTime CreateDate { get; set; } = DateTime.Today;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? Remark { get; set; }
}

// Loại giao dịch điểm của nghiệp vụ tặng điểm giới thiệu — theo nguồn DealPointType (Const.Main.cs).
// INTRODUCTION: tặng điểm cho hội viên đã giới thiệu một hội viên mới mua xe.
public enum IntroductionPointType { Introduction = 0 }

// Nhật ký tặng điểm giới thiệu — port từ Crd_Member_PerformIntroX (Transaction.AddPoint.cs)
// + bảng Crd_CardTransaction (DealPointType = 'INTRODUCTION').
// Khi một hội viên mới hoàn tất đăng ký và có khai báo người giới thiệu (MemberNoIntro) kèm số điểm
// thưởng (PointIntro), hệ thống cộng PointIntro điểm cho NGƯỜI GIỚI THIỆU (không phải hội viên mới),
// quy đổi ra tiền theo tỷ lệ UNITPOINTTOMONEY (Mst_ParamSys). Điểm có hạn dùng tới cuối tháng 12
// năm kế tiếp (PointExpiryDTime). Mỗi hội viên mới chỉ được thưởng 1 lần (chống trùng theo RefNo).
public class IntroductionGrant : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string RefNo { get; set; } = "";                // Số giao dịch (INT.yyyyMMdd.HHmmss)
    public string MemberNo { get; set; } = "";             // Mã hội viên ĐƯỢC thưởng (người giới thiệu)
    public string CardNo { get; set; } = "";               // Số thẻ (thẻ APPROVE của người giới thiệu)
    public string CardTypeUse { get; set; } = "";          // Hạng thẻ sử dụng
    public string CardTypeInit { get; set; } = "";         // Hạng thẻ gốc của hội viên
    public string DealerCode { get; set; } = "";           // DLCode — đại lý ghi nhận
    public string NewMemberNo { get; set; } = "";          // Mã hội viên MỚI (người được giới thiệu)
    public IntroductionPointType DealPointType { get; set; } = IntroductionPointType.Introduction;
    public decimal PointChTotal { get; set; }              // Điểm giới thiệu đã tặng (PointIntro)
    public decimal AmountChTotal { get; set; }             // Số tiền quy đổi (PointChTotal × ParamValue)
    public decimal ParamValue { get; set; } = 1;           // Tỷ lệ quy đổi điểm → tiền (UNITPOINTTOMONEY)
    public DateTime PointExpiryDTime { get; set; }         // Hạn dùng điểm (cuối tháng 12 năm kế tiếp)
    public DateTime CreateDate { get; set; } = DateTime.Today;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? Remark { get; set; }
}
// Nhật ký sinh mã voucher — port từ Seq_VoucherID + Mst_VoucherID của hệ Loyalty.
// Mỗi lần sinh mã, hệ thống cấp một số thứ tự tăng dần (Seq) rồi mã hoá thành mã voucher
// theo hệ cơ số 36 (0-9, A-Z): {VerGen}{Năm36}{Tháng36}{Ngày36}{NgẫuNhiên36}{Checksum36} (12 ký tự).
// Checksum = tổng giá trị các ký tự base36 của 11 ký tự đầu, lấy dư 36, mã hoá base36 1 ký tự.
// Nguồn: Seq.cs (Seq_VoucherID_GetByAmount) + Utils.cs (CMyBase36).
public class VoucherIdSequence : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public long Seq { get; set; }                          // Số thứ tự tăng dần (nguồn Seq_VoucherID.AutoID)
    public string VoucherNo { get; set; } = "";            // Mã voucher đã sinh (duy nhất)
    public string VerGen { get; set; } = "01";             // Phiên bản sinh mã
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public string? Remark { get; set; }
}