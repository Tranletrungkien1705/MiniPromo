using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniPromo.Data;
using MiniPromo.Models;
using MiniPromo.Services;

namespace MiniPromo.Controllers;

public class HomeController(IPromoService svc) : Controller
{
    public async Task<IActionResult> Index() { ViewBag.Dash = await svc.DashboardAsync(); return View(); }
}

public class CampaignController(IPromoService svc) : Controller
{
    public async Task<IActionResult> Index() => View(await svc.CampaignsAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, string? code, string? description, DateTime fromDate, DateTime toDate, int loseWeight)
    {
        var (ok, msg, id) = await svc.CreateCampaignAsync(new Campaign
        {
            Name = name ?? "", Code = (code ?? "").Trim().ToUpper(), Description = description,
            FromDate = fromDate == default ? DateTime.Today : fromDate,
            ToDate = toDate == default ? DateTime.Today.AddMonths(1) : toDate,
            LoseWeight = loseWeight <= 0 ? 100 : loseWeight
        });
        TempData[ok ? "Success" : "Error"] = msg;
        return ok ? RedirectToAction(nameof(Detail), new { id }) : RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Detail(int id)
    {
        var c = await svc.GetCampaignAsync(id);
        if (c == null) return NotFound();
        ViewBag.Stat = await svc.StatAsync(id);
        return View(c);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPrize(int id, string tier, string name, decimal value, int quantity, int weight)
    {
        var (ok, msg) = await svc.AddPrizeAsync(new Prize { CampaignId = id, Tier = tier ?? "", Name = name ?? "", Value = value, Quantity = quantity, Weight = weight });
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int id, CampaignStatus status)
    {
        var (ok, msg) = await svc.SetStatusAsync(id, status);
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }
}

public class EntryController(IPromoService svc) : Controller
{
    public async Task<IActionResult> Index(int? campaignId, PlayResult? result)
    {
        ViewBag.CampaignId = campaignId; ViewBag.Result = result;
        ViewBag.Campaigns = await svc.CampaignsAsync();
        return View(await svc.EntriesAsync(campaignId, result));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Claim(int id, ClaimStatus status, int? campaignId)
    {
        var (ok, msg) = await svc.SetClaimAsync(id, status);
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Index), new { campaignId });
    }
}

// Trang chơi công khai — người tiêu dùng mở /Play/CODE, nhập mã tem, quay số.
public class PlayController(IPromoService svc) : Controller
{
    [Route("Play/{code?}")]
    public async Task<IActionResult> Index(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return View("Pick", await svc.CampaignsAsync());
        var c = await svc.GetByCodeAsync(code);
        if (c == null) return NotFound();
        return View(c);
    }
}

public class OrgController(AppDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        Request.Cookies.TryGetValue(TenantContext.CookieName, out var curKey);
        ViewBag.CurrentKey = curKey ?? TenantContext.DefaultApiKey;
        return View(await db.Orgs.IgnoreQueryFilters().OrderBy(o => o.CreatedAt).ToListAsync());
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) { TempData["Error"] = "Cần tên tổ chức."; return RedirectToAction(nameof(Index)); }
        var org = new Org { Name = name.Trim(), ApiKey = "promo_" + Guid.NewGuid().ToString("N") };
        db.Orgs.Add(org); await db.SaveChangesAsync();
        SetCookies(org.ApiKey, org.Name);
        TempData["Success"] = $"Đã tạo & chuyển sang \"{org.Name}\"."; return RedirectToAction("Index", "Home");
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Switch(string apiKey)
    {
        var org = await db.Orgs.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.ApiKey == apiKey);
        if (org == null) { TempData["Error"] = "Không tìm thấy."; return RedirectToAction(nameof(Index)); }
        SetCookies(org.ApiKey, org.Name); return RedirectToAction("Index", "Home");
    }
    private void SetCookies(string k, string n)
    {
        var o = new CookieOptions { IsEssential = true, Expires = DateTimeOffset.UtcNow.AddDays(30) };
        Response.Cookies.Append(TenantContext.CookieName, k, o); Response.Cookies.Append("org_name", n, o);
    }
}
