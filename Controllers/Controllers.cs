using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniPromo.Data;
using MiniPromo.Models;
using MiniPromo.Services;

namespace MiniPromo.Controllers;

public class HomeController : Controller
{
    // SPA React (admin) ở "/". Trang chơi công khai /Play (Razor) giữ nguyên.
    public IActionResult Index() => Redirect("/index.html");
}

public class LegacyController(IPromoService svc) : Controller
{
    public async Task<IActionResult> Index() { ViewBag.Dash = await svc.DashboardAsync(); return View("~/Views/Home/Index.cshtml"); }
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

// Chương trình voucher theo model + điều kiện áp dụng (port từ Prm_VoucherNewCar).
public class VoucherProgramController(IVoucherProgramService svc) : Controller
{
    public async Task<IActionResult> Index() => View(await svc.ProgramsAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, string? code, DateTime effDateStart, DateTime effDateEnd,
        int validityPeriod, int qtyDayLimitFDlvDate, bool flagAllModel, decimal pointVoucherAllModel, decimal pointUseLimitAllModel, string? remark)
    {
        var (ok, msg, id) = await svc.CreateProgramAsync(new VoucherProgram
        {
            Name = name ?? "", Code = (code ?? "").Trim().ToUpper(),
            EffDateStart = effDateStart == default ? DateTime.Today : effDateStart,
            EffDateEnd = effDateEnd == default ? DateTime.Today.AddMonths(1) : effDateEnd,
            ValidityPeriod = validityPeriod, QtyDayLimitFDlvDate = qtyDayLimitFDlvDate,
            FlagAllModel = flagAllModel, PointVoucherAllModel = pointVoucherAllModel,
            PointUseLimitAllModel = pointUseLimitAllModel, Remark = remark
        });
        TempData[ok ? "Success" : "Error"] = msg;
        return ok ? RedirectToAction(nameof(Detail), new { id }) : RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Detail(int id)
    {
        var p = await svc.GetProgramAsync(id);
        if (p == null) return NotFound();
        return View(p);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddDetail(int id, string modelCode, decimal pointVoucher, decimal pointUseLimit, string? remark)
    {
        var (ok, msg) = await svc.AddDetailAsync(new VoucherProgramDtl { VoucherProgramId = id, ModelCode = modelCode ?? "", PointVoucher = pointVoucher, PointUseLimit = pointUseLimit, Remark = remark });
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int id, VoucherProgramStatus status)
    {
        var (ok, msg) = await svc.SetStatusAsync(id, status);
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    // Hoàn tất chương trình (port từ Prm_VoucherNewCar_Finish).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Finish(int id, string? remark)
    {
        var (ok, msg) = await svc.FinishAsync(id, remark);
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    // Huỷ chương trình (port từ Prm_VoucherNewCar_Cancel).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, string? remark)
    {
        var (ok, msg) = await svc.CancelAsync(id, remark);
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    // Đối soát số voucher đã phát theo chương trình + model.
    public async Task<IActionResult> Reconciliation(int? programId)
    {
        ViewBag.ProgramId = programId;
        ViewBag.Programs = await svc.ProgramsAsync();
        return View(await svc.ReconciliationAsync(programId));
    }

    // Phát voucher cho một xe theo chương trình (tạo Voucher gắn chương trình).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Issue(int id, string modelCode, DateTime? deliveryDate, DateTime? registrationDate, string? memberNo)
    {
        var o = await svc.IssueAsync(modelCode ?? "", deliveryDate, registrationDate, memberNo);
        TempData[o.ok ? "Success" : "Error"] = o.msg;
        return RedirectToAction(nameof(Detail), new { id });
    }
}

// Chương trình khuyến mại mua xe mới (port từ Prm_CarNew).
public class CarPromotionController(ICarPromotionService svc) : Controller
{
    public async Task<IActionResult> Index() => View(await svc.PromotionsAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, string? code, string dealerCode, DateTime effDateStart, DateTime effDateEnd,
        bool flagAllModel, decimal pointValAllModel, string? remark)
    {
        var (ok, msg, id) = await svc.CreatePromotionAsync(new CarPromotion
        {
            Name = name ?? "", Code = (code ?? "").Trim().ToUpper(), DealerCode = (dealerCode ?? "").Trim().ToUpper(),
            EffDateStart = effDateStart == default ? DateTime.Today : effDateStart,
            EffDateEnd = effDateEnd == default ? DateTime.Today.AddMonths(1) : effDateEnd,
            FlagAllModel = flagAllModel, PointValAllModel = pointValAllModel, Remark = remark
        });
        TempData[ok ? "Success" : "Error"] = msg;
        return ok ? RedirectToAction(nameof(Detail), new { id }) : RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Detail(int id)
    {
        var p = await svc.GetPromotionAsync(id);
        if (p == null) return NotFound();
        return View(p);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddDetail(int id, string modelCode, decimal pointVal, string? remark)
    {
        var (ok, msg) = await svc.AddDetailAsync(new CarPromotionDtl { CarPromotionId = id, ModelCode = modelCode ?? "", PointVal = pointVal, Remark = remark });
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int id, CarPromotionStatus status)
    {
        var (ok, msg) = await svc.SetStatusAsync(id, status);
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    // Duyệt chương trình (port từ Prm_CarNew_Appr).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, string? remark)
    {
        var (ok, msg) = await svc.ApproveAsync(id, remark);
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    // Hoàn tất chương trình (port từ Prm_CarNew_Finish).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Finish(int id, string? remark)
    {
        var (ok, msg) = await svc.FinishAsync(id, remark);
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    // Huỷ chương trình (port từ Prm_CarNew_Cancel).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, string? remark)
    {
        var (ok, msg) = await svc.CancelAsync(id, remark);
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }
}

// Chương trình khuyến mại chung (port từ Prm_Promotion).
public class PromotionProgramController(IPromotionProgramService svc) : Controller
{
    public async Task<IActionResult> Index() => View(await svc.ProgramsAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, string? code, PromotionMainType mainType, PromotionPrmType prmType,
        decimal budgetVal, DateTime effDTimeStart, DateTime effDTimeEnd, bool flagParallel, bool flagMulti, string? remark)
    {
        var (ok, msg, id) = await svc.CreateProgramAsync(new PromotionProgram
        {
            Name = name ?? "", Code = (code ?? "").Trim().ToUpper(), MainType = mainType, PrmType = prmType,
            BudgetVal = budgetVal,
            EffDTimeStart = effDTimeStart == default ? DateTime.Today : effDTimeStart,
            EffDTimeEnd = effDTimeEnd == default ? DateTime.Today.AddMonths(1) : effDTimeEnd,
            FlagParallel = flagParallel, FlagMulti = flagMulti, Remark = remark
        });
        TempData[ok ? "Success" : "Error"] = msg;
        return ok ? RedirectToAction(nameof(Detail), new { id }) : RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Detail(int id)
    {
        var p = await svc.GetProgramAsync(id);
        if (p == null) return NotFound();
        return View(p);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddScope(int id, PromotionScopeType scopeType, string value, string? valueEnd)
    {
        var (ok, msg) = await svc.AddScopeAsync(new PromotionScope { PromotionProgramId = id, ScopeType = scopeType, Value = value ?? "", ValueEnd = valueEnd });
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPrm(int id, int idx, int qty, decimal upDc, decimal upRateDc, decimal upDcMax,
        decimal valOrdDc, decimal valOrdRateDc, decimal valOrdDcMax, string? remark)
    {
        var (ok, msg) = await svc.AddPrmAsync(new PromotionPrm
        {
            PromotionProgramId = id, Idx = idx, Qty = qty, UPDc = upDc, UPRateDc = upRateDc, UPDcMax = upDcMax,
            ValOrdDc = valOrdDc, ValOrdRateDc = valOrdRateDc, ValOrdDcMax = valOrdDcMax, Remark = remark
        });
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddMain(int id, int idx, int qty, decimal amount, decimal totalValOrd)
    {
        var (ok, msg) = await svc.AddMainAsync(new PromotionMain { PromotionProgramId = id, Idx = idx, Qty = qty, Amount = amount, TotalValOrd = totalValOrd });
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    // Duyệt chương trình.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, string? remark)
    {
        var (ok, msg) = await svc.ApproveAsync(id, remark);
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    // Hoàn tất chương trình.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Finish(int id, string? remark)
    {
        var (ok, msg) = await svc.FinishAsync(id, remark);
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    // Huỷ chương trình.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, string? remark)
    {
        var (ok, msg) = await svc.CancelAsync(id, remark);
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }
}
