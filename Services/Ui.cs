using MiniPromo.Models;
namespace MiniPromo.Services;

public static class Ui
{
    public static string Money(decimal v) => v.ToString("N0") + "đ";

    public static (string text, string css) Camp(CampaignStatus s) => s switch
    {
        CampaignStatus.Draft   => ("Nháp", "secondary"),
        CampaignStatus.Running => ("Đang chạy", "success"),
        CampaignStatus.Ended   => ("Kết thúc", "dark"),
        _ => (s.ToString(), "secondary")
    };

    public static (string text, string css) Claim(ClaimStatus s) => s switch
    {
        ClaimStatus.Pending => ("Chờ trao", "warning"),
        ClaimStatus.Claimed => ("Đã trao", "success"),
        _ => ("—", "light")
    };

    public static (string text, string css) Voucher(VoucherStatus s) => s switch
    {
        VoucherStatus.Active   => ("Khả dụng", "success"),
        VoucherStatus.Inactive => ("Tạm dừng", "secondary"),
        VoucherStatus.Expired  => ("Hết hạn", "dark"),
        VoucherStatus.UsedUp   => ("Hết lượt/điểm", "warning"),
        _ => (s.ToString(), "secondary")
    };

    public static (string text, string css) VoucherProgram(VoucherProgramStatus s) => s switch
    {
        VoucherProgramStatus.Pending   => ("Chờ duyệt", "secondary"),
        VoucherProgramStatus.Approved  => ("Đã duyệt", "info"),
        VoucherProgramStatus.Finished  => ("Hoàn tất", "success"),
        VoucherProgramStatus.Cancelled => ("Đã huỷ", "dark"),
        _ => (s.ToString(), "secondary")
    };

    public static (string text, string css) CarPromotion(CarPromotionStatus s) => s switch
    {
        CarPromotionStatus.Pending   => ("Chờ duyệt", "secondary"),
        CarPromotionStatus.Approved  => ("Đã duyệt", "info"),
        CarPromotionStatus.Finished  => ("Hoàn tất", "success"),
        CarPromotionStatus.Cancelled => ("Đã huỷ", "dark"),
        _ => (s.ToString(), "secondary")
    };

    public static (string text, string css) Promotion(PromotionStatus s) => s switch
    {
        PromotionStatus.Pending   => ("Chờ duyệt", "secondary"),
        PromotionStatus.Approved  => ("Đã duyệt", "info"),
        PromotionStatus.Finished  => ("Hoàn tất", "success"),
        PromotionStatus.Cancelled => ("Đã huỷ", "dark"),
        _ => (s.ToString(), "secondary")
    };

    public static (string text, string css) CarRecommend(CarRecommendStatus s) => s switch
    {
        CarRecommendStatus.Pending   => ("Chờ duyệt", "secondary"),
        CarRecommendStatus.Approved  => ("Đã duyệt", "info"),
        CarRecommendStatus.Finished  => ("Hoàn tất", "success"),
        CarRecommendStatus.Cancelled => ("Đã huỷ", "dark"),
        _ => (s.ToString(), "secondary")
    };

    public static (string text, string css) CardPromotionProgram(CardPromotionProgramStatus s) => s switch
    {
        CardPromotionProgramStatus.Active   => ("Đang bật", "success"),
        CardPromotionProgramStatus.Inactive => ("Tạm dừng", "secondary"),
        _ => (s.ToString(), "secondary")
    };

    public static (string text, string css) BirthdayPolicy(BirthdayPolicyStatus s) => s switch
    {
        BirthdayPolicyStatus.Active   => ("Đang bật", "success"),
        BirthdayPolicyStatus.Inactive => ("Tạm dừng", "secondary"),
        _ => (s.ToString(), "secondary")
    };

    public static string MainTypeText(PromotionMainType t) => t switch
    {
        PromotionMainType.Order           => "Đơn hàng",
        PromotionMainType.Product         => "Sản phẩm",
        PromotionMainType.ProductAndOrder => "Sản phẩm & đơn hàng",
        _ => t.ToString()
    };

    public static string PrmTypeText(PromotionPrmType t) => t switch
    {
        PromotionPrmType.Order            => "Giảm giá đơn hàng",
        PromotionPrmType.Product          => "Giảm giá sản phẩm",
        PromotionPrmType.ProductUPDc      => "Giảm giá sản phẩm theo tiền",
        PromotionPrmType.ProductUPDcByQty => "Giảm giá sản phẩm theo số lượng",
        PromotionPrmType.Voucher          => "Tặng voucher",
        _ => t.ToString()
    };

    public static string ScopeTypeText(PromotionScopeType t) => t switch
    {
        PromotionScopeType.Date          => "Ngày",
        PromotionScopeType.DayOfWeek     => "Thứ",
        PromotionScopeType.Time          => "Giờ",
        PromotionScopeType.Month         => "Tháng",
        PromotionScopeType.Day           => "Ngày trong tháng",
        PromotionScopeType.Org           => "Chi nhánh",
        PromotionScopeType.User          => "Người tạo",
        PromotionScopeType.CustomerGroup => "Nhóm khách hàng",
        _ => t.ToString()
    };

    public static string RefTypeText(PromotionRefType t) => t switch
    {
        PromotionRefType.Product      => "Sản phẩm",
        PromotionRefType.ProductGroup => "Nhóm sản phẩm",
        PromotionRefType.VoucherIssue => "Phát voucher",
        PromotionRefType.Voucher      => "Voucher",
        _ => t.ToString()
    };

    public static string ProductScopeKindText(PromotionProductScopeKind k) => k switch
    {
        PromotionProductScopeKind.Main => "Điều kiện",
        PromotionProductScopeKind.Prm  => "Hình thức",
        _ => k.ToString()
    };
}
