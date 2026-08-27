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
}
