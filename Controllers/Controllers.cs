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

    // Thêm phạm vi sản phẩm/nhóm sản phẩm áp dụng (port từ Prm_PromotionMainSpec/Prm_PromotionPrmSpec).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddProductScope(int id, PromotionProductScopeKind kind, int idx, PromotionRefType refType, string refCode, string? refName, int? mapIdx, string? remark)
    {
        var (ok, msg) = await svc.AddProductScopeAsync(new PromotionProductScope
        {
            PromotionProgramId = id, Kind = kind, Idx = idx, RefType = refType,
            RefCode = refCode ?? "", RefName = refName, MapIdx = mapIdx, Remark = remark
        });
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

// Chương trình giới thiệu xe (port từ Prm_CarRecommend).
public class CarRecommendController(ICarRecommendService svc) : Controller
{
    public async Task<IActionResult> Index() => View(await svc.RecommendsAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, string? code, string dealerCode, DateTime effDateStart, DateTime effDateEnd,
        bool flagAllModel, decimal pointValAllModel, string? remark)
    {
        var (ok, msg, id) = await svc.CreateRecommendAsync(new CarRecommend
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
        var p = await svc.GetRecommendAsync(id);
        if (p == null) return NotFound();
        return View(p);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddDetail(int id, string modelCode, decimal pointVal, string? remark)
    {
        var (ok, msg) = await svc.AddDetailAsync(new CarRecommendDtl { CarRecommendId = id, ModelCode = modelCode ?? "", PointVal = pointVal, Remark = remark });
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int id, CarRecommendStatus status)
    {
        var (ok, msg) = await svc.SetStatusAsync(id, status);
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    // Duyệt chương trình (port từ Prm_CarRecommend_Appr).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, string? remark)
    {
        var (ok, msg) = await svc.ApproveAsync(id, remark);
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    // Hoàn tất chương trình (port từ Prm_CarRecommend_Finish).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Finish(int id, string? remark)
    {
        var (ok, msg) = await svc.FinishAsync(id, remark);
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    // Huỷ chương trình (port từ Prm_CarRecommend_Cancel).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, string? remark)
    {
        var (ok, msg) = await svc.CancelAsync(id, remark);
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }
}

// Chương trình khuyến mại theo loại thẻ (port từ Mst_PromotionProgram).
public class CardPromotionProgramController(ICardPromotionProgramService svc) : Controller
{
    public async Task<IActionResult> Index() => View(await svc.ProgramsAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, string? code, DateTime effDateStart, DateTime effDateEnd,
        bool flagAllDL, string? remark)
    {
        var (ok, msg, id) = await svc.CreateProgramAsync(new CardPromotionProgram
        {
            Name = name ?? "", Code = (code ?? "").Trim().ToUpper(),
            EffDateStart = effDateStart == default ? DateTime.Today : effDateStart,
            EffDateEnd = effDateEnd == default ? DateTime.Today.AddMonths(1) : effDateEnd,
            FlagAllDL = flagAllDL, Remark = remark
        });
        TempData[ok ? "Success" : "Error"] = msg;
        return ok ? RedirectToAction(nameof(Detail), new { id }) : RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Detail(int id)
    {
        var p = await svc.GetProgramAsync(id);
        if (p == null) return NotFound();
        ViewBag.Recon = await svc.ReconciliationAsync(id);
        return View(p);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddDetail(int id, string cardType, int qty, string? unit, string? remark)
    {
        var (ok, msg) = await svc.AddDetailAsync(new CardPromotionProgramDtl { CardPromotionProgramId = id, CardType = cardType ?? "", Qty = qty, Unit = unit, Remark = remark });
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddDealer(int id, string dealerCode)
    {
        var (ok, msg) = await svc.AddDealerAsync(new CardPromotionProgramSpec { CardPromotionProgramId = id, DealerCode = dealerCode ?? "" });
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int id, CardPromotionProgramStatus status)
    {
        var (ok, msg) = await svc.SetStatusAsync(id, status);
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    // Ghi nhận sử dụng ưu đãi cho một giao dịch (port từ Crd_DealUsePromotion_Save).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Use(string dealNo, string dealerCode, string cardNo, string cardType, int qty)
    {
        var o = await svc.UseAsync(dealNo ?? "", dealerCode ?? "", cardNo ?? "", cardType ?? "", qty);
        TempData[o.ok ? "Success" : "Error"] = o.msg;
        return RedirectToAction(nameof(Index));
    }

    // Đối soát số lượng ưu đãi đã dùng theo chương trình + loại thẻ.
    public async Task<IActionResult> Reconciliation(int? programId)
    {
        ViewBag.ProgramId = programId;
        ViewBag.Programs = await svc.ProgramsAsync();
        return View(await svc.ReconciliationAsync(programId));
    }
}

// Chương trình tặng điểm sinh nhật (port từ Mst_BirthPolicy).
public class BirthdayPolicyController(IBirthdayPolicyService svc) : Controller
{
    public async Task<IActionResult> Index() => View(await svc.PoliciesAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, string? code, DateTime effDateStart, DateTime effDateEnd,
        bool flagPoint, decimal paramValue, string? remark)
    {
        var (ok, msg, id) = await svc.CreatePolicyAsync(new BirthdayPolicy
        {
            Name = name ?? "", Code = (code ?? "").Trim().ToUpper(),
            EffDateStart = effDateStart == default ? DateTime.Today : effDateStart,
            EffDateEnd = effDateEnd == default ? DateTime.Today.AddMonths(1) : effDateEnd,
            FlagPoint = flagPoint, ParamValue = paramValue <= 0 ? 1 : paramValue, Remark = remark
        });
        TempData[ok ? "Success" : "Error"] = msg;
        return ok ? RedirectToAction(nameof(Detail), new { id }) : RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Detail(int id)
    {
        var p = await svc.GetPolicyAsync(id);
        if (p == null) return NotFound();
        ViewBag.Recon = await svc.ReconciliationAsync(id);
        return View(p);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddDetail(int id, string cardType, decimal point, string? remark)
    {
        var (ok, msg) = await svc.AddDetailAsync(new BirthdayPolicyDtl { BirthdayPolicyId = id, CardType = cardType ?? "", Point = point, Remark = remark });
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int id, BirthdayPolicyStatus status)
    {
        var (ok, msg) = await svc.SetStatusAsync(id, status);
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    // Tặng điểm sinh nhật cho một hội viên (port từ Crd_Member_PerformBirhday).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Grant(string memberNo, string cardNo, string cardType, string dealerCode, DateTime? dateOfBirth, DateTime? at)
    {
        var o = await svc.GrantAsync(memberNo ?? "", cardNo ?? "", cardType ?? "", dealerCode ?? "", dateOfBirth, at);
        TempData[o.ok ? "Success" : "Error"] = o.msg;
        return RedirectToAction(nameof(Index));
    }

    // Đối soát điểm sinh nhật đã tặng theo chương trình + loại thẻ.
    public async Task<IActionResult> Reconciliation(int? policyId)
    {
        ViewBag.PolicyId = policyId;
        ViewBag.Policies = await svc.PoliciesAsync();
        return View(await svc.ReconciliationAsync(policyId));
    }
}

// Đợt phát hành voucher (port từ Mst_IssueVoucher).
public class IssueVoucherController(IIssueVoucherService svc) : Controller
{
    public async Task<IActionResult> Index() => View(await svc.BatchesAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, string? code, DateTime effDateStart, DateTime effDateEnd,
        int qtyVoucher, int qtyDateUse, IssueFavorType favorType, IssueFormType issueForm, string? remark)
    {
        var (ok, msg, id) = await svc.CreateBatchAsync(new IssueVoucher
        {
            Name = name ?? "", Code = (code ?? "").Trim().ToUpper(),
            EffDateStart = effDateStart == default ? DateTime.Today : effDateStart,
            EffDateEnd = effDateEnd == default ? DateTime.Today.AddMonths(1) : effDateEnd,
            QtyVoucher = qtyVoucher, QtyDateUse = qtyDateUse,
            FavorType = favorType, IssueForm = issueForm, Remark = remark
        });
        TempData[ok ? "Success" : "Error"] = msg;
        return ok ? RedirectToAction(nameof(Detail), new { id }) : RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Detail(int id)
    {
        var v = await svc.GetBatchAsync(id);
        if (v == null) return NotFound();
        return View(v);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddScope(int id, IssueScopeType scopeType, string value)
    {
        var (ok, msg) = await svc.AddScopeAsync(new IssueVoucherScope { IssueVoucherId = id, ScopeType = scopeType, Value = value ?? "" });
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddProduct(int id, IssueRefType refType, string refCode, string? refName)
    {
        var (ok, msg) = await svc.AddProductAsync(new IssueVoucherProduct { IssueVoucherId = id, RefType = refType, RefCode = refCode ?? "", RefName = refName });
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPrice(int id, IssuePriceType issueType, string issueTypeDtl, decimal upDc, decimal upRateDc, decimal upDcMax, string? remark)
    {
        var (ok, msg) = await svc.AddPriceAsync(new IssueVoucherPrice
        {
            IssueVoucherId = id, IssueType = issueType, IssueTypeDtl = issueTypeDtl ?? "",
            UPDc = upDc, UPRateDc = upRateDc, UPDcMax = upDcMax, Remark = remark
        });
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(int id, bool active)
    {
        var (ok, msg) = await svc.SetActiveAsync(id, active);
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    // Phát hành một voucher trong đợt (port từ Mst_IssueVoucherDtl).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Issue(int id, string voucherNo, string? receiver, DateTime? at)
    {
        var o = await svc.IssueAsync(id, voucherNo ?? "", receiver, at);
        TempData[o.ok ? "Success" : "Error"] = o.msg;
        return RedirectToAction(nameof(Detail), new { id });
    }

    // Thu hồi voucher đã phát.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Evict(int id, int voucherId)
    {
        var (ok, msg) = await svc.EvictAsync(voucherId);
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    // Huỷ voucher đã phát.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelVoucher(int id, int voucherId)
    {
        var (ok, msg) = await svc.CancelVoucherAsync(voucherId);
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    // Đối soát đợt phát hành theo trạng thái voucher.
    public async Task<IActionResult> Reconciliation(int? batchId)
    {
        ViewBag.BatchId = batchId;
        ViewBag.Batches = await svc.BatchesAsync();
        return View(await svc.ReconciliationAsync(batchId));
    }
}
