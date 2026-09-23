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
}
