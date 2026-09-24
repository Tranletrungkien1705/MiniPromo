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
    public async Task<IActionResult> Use(string dealNo, string dealerCode, string cardNo, string? memberNo, string cardType, int qty)
    {
        var o = await svc.UseAsync(dealNo ?? "", dealerCode ?? "", cardNo ?? "", cardType ?? "", qty, memberNo);
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

// Tra cứu chương trình ưu đãi khả dụng cho một hội viên/thẻ (port từ Crd_Card_GetForPromotion).
public class AvailablePromotionController(ICardPromotionProgramService svc) : Controller
{
    public IActionResult Index() => View(new List<AvailablePromotionRow>());

    // Tra cứu ưu đãi khả dụng theo loại thẻ + đại lý của thẻ.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Lookup(string cardType, string dealerCode, DateTime? at)
    {
        ViewBag.CardType = cardType;
        ViewBag.DealerCode = dealerCode;
        ViewBag.At = at;
        return View(nameof(Index), await svc.AvailableForCardAsync(cardType ?? "", dealerCode ?? "", at));
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

// Voucher sinh nhật (port từ Crd_MemberVoucher, nâng cấp 20260518).
public class BirthdayVoucherController(IBirthdayVoucherService svc) : Controller
{
    public async Task<IActionResult> Index(string? memberNo)
    {
        ViewBag.MemberNo = memberNo;
        return View(await svc.VouchersAsync(memberNo));
    }

    public async Task<IActionResult> Detail(int id)
    {
        var v = await svc.GetVoucherAsync(id);
        if (v == null) return NotFound();
        return View(v);
    }

    // Phát voucher sinh nhật cho một hội viên (port từ Crd_Member_PerformVCBirhday).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Issue(string memberNo, string? cardNo, string cardType, DateTime? dateOfBirth, DateTime? at)
    {
        var o = await svc.IssueAsync(memberNo ?? "", cardNo ?? "", cardType ?? "", dateOfBirth, at);
        TempData[o.ok ? "Success" : "Error"] = o.msg;
        return RedirectToAction(nameof(Index));
    }

    // Đối soát voucher sinh nhật đã phát theo chương trình + loại thẻ.
    public async Task<IActionResult> Reconciliation(int? policyId)
    {
        ViewBag.PolicyId = policyId;
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
// Chính sách xếp hạng thẻ (port từ Mst_RankPolicy).
public class RankPolicyController(IRankPolicyService svc) : Controller
{
    public async Task<IActionResult> Index() => View(await svc.PoliciesAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string cardType, string? code, int value,
        decimal pointUpBegin, decimal pointUpEnd, int qtyVisitUpBegin, int qtyVisitUpEnd,
        decimal pointKeepBegin, decimal pointKeepEnd, int qtyVisitKeepBegin, int qtyVisitKeepEnd,
        int qtyMonth, string? remark)
    {
        var (ok, msg, id) = await svc.CreatePolicyAsync(new RankPolicy
        {
            CardType = cardType ?? "", Code = (code ?? "").Trim().ToUpper(), Value = value,
            PointUpBegin = pointUpBegin, PointUpEnd = pointUpEnd, QtyVisitUpBegin = qtyVisitUpBegin, QtyVisitUpEnd = qtyVisitUpEnd,
            PointKeepBegin = pointKeepBegin, PointKeepEnd = pointKeepEnd, QtyVisitKeepBegin = qtyVisitKeepBegin, QtyVisitKeepEnd = qtyVisitKeepEnd,
            QtyMonth = qtyMonth <= 0 ? 12 : qtyMonth, Remark = remark
        });
        TempData[ok ? "Success" : "Error"] = msg;
        return ok ? RedirectToAction(nameof(Detail), new { id }) : RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Detail(int id)
    {
        var p = await svc.GetPolicyAsync(id);
        if (p == null) return NotFound();
        return View(p);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int id, RankPolicyStatus status)
    {
        var (ok, msg) = await svc.SetStatusAsync(id, status);
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    // Đánh giá xếp hạng thẻ theo chính sách đang bật (port từ Crd_CardRankPolicy_PerformX).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Evaluate(string cardType, decimal point, int qtyVisit)
    {
        var o = await svc.EvaluateAsync(cardType ?? "", point, qtyVisit);
        TempData[o.ok ? "Success" : "Error"] = o.msg;
        return RedirectToAction(nameof(Index));
    }
}

// Chính sách quy đổi tiền dịch vụ → điểm (port từ Mst_PolicyMoneyToPointService).
public class PolicyMoneyToPointController(IPolicyMoneyToPointService svc) : Controller
{
    public async Task<IActionResult> Index() => View(await svc.PoliciesAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, string? code, DateTime effDateStart, DateTime effDateEnd, string? remark)
    {
        var (ok, msg, id) = await svc.CreatePolicyAsync(new PolicyMoneyToPoint
        {
            Name = name ?? "", Code = (code ?? "").Trim().ToUpper(),
            EffDateStart = effDateStart == default ? DateTime.Today : effDateStart,
            EffDateEnd = effDateEnd == default ? DateTime.Today.AddMonths(1) : effDateEnd,
            Remark = remark
        });
        TempData[ok ? "Success" : "Error"] = msg;
        return ok ? RedirectToAction(nameof(Detail), new { id }) : RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Detail(int id)
    {
        var p = await svc.GetPolicyAsync(id);
        if (p == null) return NotFound();
        return View(p);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddDetail(int id, string cardType, decimal convertValue, decimal convertPoint, decimal valueRankCardType, decimal discountRate, string? remark)
    {
        var (ok, msg) = await svc.AddDetailAsync(new PolicyMoneyToPointDtl
        {
            PolicyMoneyToPointId = id, CardType = cardType ?? "", ConvertValue = convertValue,
            ConvertPoint = convertPoint, ValueRankCardType = valueRankCardType, DiscountRate = discountRate, Remark = remark
        });
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int id, PolicyMoneyToPointStatus status)
    {
        var (ok, msg) = await svc.SetStatusAsync(id, status);
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    // Quy đổi tiền dịch vụ → điểm cho một hạng thẻ theo chính sách đang hiệu lực.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Calc(string cardType, decimal amount, DateTime? at)
    {
        var o = await svc.CalcAsync(cardType ?? "", amount, at);
        TempData[o.ok ? "Success" : "Error"] = o.ok
            ? $"Hạng {o.cardType}: {o.amount.ToString("N0")}đ → {o.point.ToString("N0")} điểm (chiết khấu {o.discountRate}%, lượt xét hạng {o.qtyVisit})."
            : o.msg;
        return RedirectToAction(nameof(Index));
    }
}

// Chiết khấu hội viên (port từ Crd_MemberDiscountTransaction).
public class MemberDiscountController(IMemberDiscountService svc) : Controller
{
    public async Task<IActionResult> Index(string? refNo)
    {
        ViewBag.RefNo = refNo;
        return View(await svc.TransactionsAsync(refNo));
    }

    public async Task<IActionResult> Detail(int id)
    {
        var t = await svc.GetTransactionAsync(id);
        if (t == null) return NotFound();
        return View(t);
    }

    // Tính chiết khấu thử cho một giao dịch (một dòng hàng).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Calc(string refNo, string cardTypeApply, decimal amountForDC, decimal paymentDiscountRate, DateTime? at)
    {
        var lines = new[] { new MemberDiscountLine(amountForDC, paymentDiscountRate, true) };
        var o = await svc.CalcAsync(refNo ?? "", cardTypeApply ?? "", lines, at);
        TempData[o.ok ? "Success" : "Error"] = o.ok
            ? $"Giao dịch {o.refNo} · hạng {o.cardTypeApply}: tiền chiết khấu {o.amountForDC.ToString("N0")}đ → chiết khấu {o.discount.ToString("N0")}đ (tỷ lệ hạng thẻ {o.policyDiscountRate}%)."
            : o.msg;
        return RedirectToAction(nameof(Index));
    }

    // Ghi nhận giao dịch chiết khấu (một dòng hàng).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Record(string refNo, string dealerCode, string memberNo, string cardNo,
        string cardTypeUse, string cardTypeInit, string cardTypeApply, decimal amountForDC, decimal paymentDiscountRate, DateTime? at)
    {
        var lines = new[] { new MemberDiscountLine(amountForDC, paymentDiscountRate, true) };
        var o = await svc.RecordAsync(refNo ?? "", dealerCode ?? "", memberNo ?? "", cardNo ?? "",
            cardTypeUse ?? "", cardTypeInit ?? "", cardTypeApply ?? "", lines, at);
        TempData[o.ok ? "Success" : "Error"] = o.msg;
        return RedirectToAction(nameof(Index));
    }

    // Đối soát chiết khấu theo hạng thẻ áp dụng.
    public async Task<IActionResult> Reconciliation(string? cardTypeApply)
    {
        ViewBag.CardTypeApply = cardTypeApply;
        return View(await svc.ReconciliationAsync(cardTypeApply));
    }
}// Danh mục loại khuyến mại (port từ Mst_PromotionMainType + Mst_PromotionPrmType + Prm_PrmInMain).
public class PromotionTypeController(IPromotionTypeService svc) : Controller
{
    public async Task<IActionResult> Index()
    {
        ViewBag.MainTypes = await svc.MainTypesAsync();
        ViewBag.PrmTypes = await svc.PrmTypesAsync();
        return View(await svc.MappingsAsync());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateMainType(string name, string? code, string? remark)
    {
        var (ok, msg, _) = await svc.CreateMainTypeAsync(new PromotionMainTypeDef { Name = name ?? "", Code = (code ?? "").Trim().ToUpper(), Remark = remark });
        TempData[ok ? "Success" : "Error"] = msg;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetMainTypeActive(int id, bool active)
    {
        var (ok, msg) = await svc.SetMainTypeActiveAsync(id, active);
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePrmType(string name, string? code, string? remark)
    {
        var (ok, msg, _) = await svc.CreatePrmTypeAsync(new PromotionPrmTypeDef { Name = name ?? "", Code = (code ?? "").Trim().ToUpper(), Remark = remark });
        TempData[ok ? "Success" : "Error"] = msg;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPrmTypeActive(int id, bool active)
    {
        var (ok, msg) = await svc.SetPrmTypeActiveAsync(id, active);
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddMapping(int mainTypeId, int prmTypeId, string? remark)
    {
        var (ok, msg, _) = await svc.AddMappingAsync(mainTypeId, prmTypeId, remark);
        TempData[ok ? "Success" : "Error"] = msg;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetMappingActive(int id, bool active)
    {
        var (ok, msg) = await svc.SetMappingActiveAsync(id, active);
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Index));
    }

    // Kiểm tra một hình thức khuyến mại có được phép dùng cho một loại khuyến mại theo hay không.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Check(string mainTypeCode, string prmTypeCode)
    {
        var o = await svc.CheckPrmInMainAsync(mainTypeCode ?? "", prmTypeCode ?? "");
        TempData[o.ok ? "Success" : "Error"] = o.msg;
        return RedirectToAction(nameof(Index));
    }
}

// Mã giảm giá + ánh xạ đại lý (port từ Inos_DiscountCode + Map_DealerDiscount).
public class DiscountCodeController(IDiscountCodeService svc) : Controller
{
    public async Task<IActionResult> Index() => View(await svc.CodesAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string code, string? description, DiscountCodeType discountType,
        decimal discountAmount, int remainQty, DateTime effectDateFrom, DateTime effectDateTo)
    {
        var (ok, msg, id) = await svc.CreateCodeAsync(new DiscountCode
        {
            Code = (code ?? "").Trim().ToUpper(), Description = description, DiscountType = discountType,
            DiscountAmount = discountAmount, RemainQty = remainQty,
            EffectDateFrom = effectDateFrom == default ? DateTime.Today : effectDateFrom,
            EffectDateTo = effectDateTo == default ? DateTime.Today.AddMonths(1) : effectDateTo
        });
        TempData[ok ? "Success" : "Error"] = msg;
        return ok ? RedirectToAction(nameof(Detail), new { id }) : RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Detail(int id)
    {
        var c = await svc.GetCodeAsync(id);
        if (c == null) return NotFound();
        ViewBag.Maps = await svc.MapsAsync(null);
        return View(c);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetEnabled(int id, bool enabled)
    {
        var (ok, msg) = await svc.SetEnabledAsync(id, enabled);
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    // Gán mã giảm giá cho một đại lý (port từ Map_DealerDiscount).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddMap(int id, string dealerCode, string? remark)
    {
        var c = await svc.GetCodeAsync(id);
        if (c == null) return NotFound();
        var (ok, msg, _) = await svc.AddMapAsync(new DealerDiscountMap { DealerCode = dealerCode ?? "", DiscountCode = c.Code, Remark = remark });
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Detail), new { id });
    }

    // Kiểm tra một mã giảm giá có hợp lệ cho một đơn hàng.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Check(string code, decimal orderAmount, DateTime? at)
    {
        var o = await svc.CheckAsync(code ?? "", orderAmount, at);
        TempData[o.ok ? "Success" : "Error"] = o.ok
            ? $"Mã {o.code} hợp lệ, giảm {Ui.Money(o.discountAmount)}."
            : o.msg;
        return RedirectToAction(nameof(Index));
    }
}

// Sinh mã voucher theo hệ cơ số 36 + checksum (port từ Seq_VoucherID + Mst_VoucherID).
public class VoucherIdController(IVoucherIdService svc) : Controller
{
    public async Task<IActionResult> Index() => View(await svc.SequencesAsync());

    // Sinh một hoặc nhiều mã voucher mới.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(int amount, DateTime? at)
    {
        var o = await svc.GenerateAsync(amount <= 0 ? 1 : amount, at);
        TempData[o.ok ? "Success" : "Error"] = o.ok
            ? $"Đã sinh {o.codes.Count} mã: {string.Join(", ", o.codes)}."
            : o.msg;
        return RedirectToAction(nameof(Index));
    }

    // Kiểm tra định dạng + checksum của một mã voucher.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Validate(string voucherNo)
    {
        var o = svc.Validate(voucherNo ?? "");
        TempData[o.ok ? "Success" : "Error"] = o.msg;
        return RedirectToAction(nameof(Index));
    }
}

// Tặng điểm giới thiệu (port từ Crd_Member_PerformIntroX).
public class IntroductionGrantController(IIntroductionGrantService svc) : Controller
{
    public async Task<IActionResult> Index(string? memberNo)
    {
        ViewBag.MemberNo = memberNo;
        return View(await svc.GrantsAsync(memberNo));
    }

    // Tặng điểm giới thiệu cho người giới thiệu của một hội viên mới.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Grant(string newMemberNo, string referrerMemberNo, string? cardNo,
        string? cardTypeUse, string? cardTypeInit, string? dealerCode, decimal pointIntro, decimal paramValue, DateTime? at)
    {
        var o = await svc.GrantAsync(newMemberNo ?? "", referrerMemberNo ?? "", cardNo ?? "",
            cardTypeUse ?? "", cardTypeInit ?? "", dealerCode ?? "", pointIntro, paramValue, at);
        TempData[o.ok ? "Success" : "Error"] = o.msg;
        return RedirectToAction(nameof(Index));
    }

    // Đối soát điểm giới thiệu đã tặng theo hội viên được thưởng.
    public async Task<IActionResult> Reconciliation(string? memberNo)
    {
        ViewBag.MemberNo = memberNo;
        return View(await svc.ReconciliationAsync(memberNo));
    }
}

// Tặng điểm mua xe mới (port từ Crd_Member_PerformBuyNewCarX).
public class CarPurchasePointController(ICarPurchasePointService svc) : Controller
{
    public async Task<IActionResult> Index(string? memberNo)
    {
        ViewBag.MemberNo = memberNo;
        return View(await svc.GrantsAsync(memberNo));
    }

    // Tặng điểm mua xe mới cho một hội viên.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Grant(string memberNo, string? cardNo, string? cardTypeUse, string? cardTypeInit,
        string? dealerCode, string? prProgramCode, decimal pointBuyCar, decimal paramValue, DateTime? at)
    {
        var o = await svc.GrantAsync(memberNo ?? "", cardNo ?? "", cardTypeUse ?? "", cardTypeInit ?? "",
            dealerCode ?? "", prProgramCode, pointBuyCar, paramValue, at);
        TempData[o.ok ? "Success" : "Error"] = o.msg;
        return RedirectToAction(nameof(Index));
    }

    // Đối soát điểm mua xe mới đã tặng theo hội viên.
    public async Task<IActionResult> Reconciliation(string? memberNo)
    {
        ViewBag.MemberNo = memberNo;
        return View(await svc.ReconciliationAsync(memberNo));
    }
}

// Tặng điểm khuyến mại bán hàng (HTV) (port từ Crd_Member_PerformBuyCretaX).
public class KmbhGrantController(IKmbhGrantService svc) : Controller
{
    public async Task<IActionResult> Index(string? memberNo)
    {
        ViewBag.MemberNo = memberNo;
        return View(await svc.GrantsAsync(memberNo));
    }

    // Tặng điểm khuyến mại bán hàng (HTV) cho một hội viên.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Grant(string memberNo, string? cardNo, string? cardTypeUse, string? cardTypeInit,
        decimal pointBuyCreta, decimal paramValue, DateTime? at)
    {
        var o = await svc.GrantAsync(memberNo ?? "", cardNo ?? "", cardTypeUse ?? "", cardTypeInit ?? "",
            pointBuyCreta, paramValue, at);
        TempData[o.ok ? "Success" : "Error"] = o.msg;
        return RedirectToAction(nameof(Index));
    }

    // Đối soát điểm khuyến mại bán hàng đã tặng theo hội viên.
    public async Task<IActionResult> Reconciliation(string? memberNo)
    {
        ViewBag.MemberNo = memberNo;
        return View(await svc.ReconciliationAsync(memberNo));
    }
}

// Chính sách đối tượng tích điểm dịch vụ (port từ Mst_PolicyExpenseType + Mst_ExpenseType).
public class PolicyExpenseTypeController(IPolicyExpenseTypeService svc) : Controller
{
    public async Task<IActionResult> Index(string? policyNo)
    {
        ViewBag.PolicyNo = policyNo;
        ViewBag.ExpenseTypes = await svc.ExpenseTypesAsync();
        return View(await svc.PoliciesAsync(policyNo));
    }

    // Tạo loại chi phí (Mst_ExpenseType).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateExpenseType(string name, string? code, string? remark)
    {
        var (ok, msg, _) = await svc.CreateExpenseTypeAsync(new ExpenseType { Name = name ?? "", Code = (code ?? "").Trim().ToUpper(), Remark = remark });
        TempData[ok ? "Success" : "Error"] = msg;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetExpenseTypeActive(int id, bool active)
    {
        var (ok, msg) = await svc.SetExpenseTypeActiveAsync(id, active);
        TempData[ok ? "Success" : "Error"] = msg; return RedirectToAction(nameof(Index));
    }

    // Lưu toàn bộ dòng của một chính sách (xoá sạch rồi ghi lại) — port từ Mst_PolicyExpenseType_SaveX.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(string policyExpenseTypeNo, string expenseType, string? expenseTypeNameActual,
        bool flagPoint, bool flagPointRank, decimal amountRate, decimal maxRankReviewPoint, decimal maxAccumulationPoint,
        bool flagCountService, bool flagDiscount, decimal discountRate, string? remark)
    {
        var rows = new List<PolicyExpenseType>
        {
            new()
            {
                ExpenseType = expenseType ?? "", ExpenseTypeNameActual = expenseTypeNameActual ?? "",
                FlagPoint = flagPoint, FlagPointRank = flagPointRank, AmountRate = amountRate,
                MaxRankReviewPoint = maxRankReviewPoint, MaxAccumulationPoint = maxAccumulationPoint,
                FlagCountService = flagCountService, FlagDiscount = flagDiscount, DiscountRate = discountRate, Remark = remark
            }
        };
        var (ok, msg, _) = await svc.SavePolicyAsync(policyExpenseTypeNo ?? "", rows);
        TempData[ok ? "Success" : "Error"] = msg;
        return RedirectToAction(nameof(Index), new { policyNo = policyExpenseTypeNo });
    }

    // Tra cứu quy tắc tích điểm dịch vụ cho một loại chi phí.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Calc(string expenseType, decimal amount)
    {
        var o = await svc.CalcAsync(expenseType ?? "", amount);
        TempData[o.ok ? "Success" : "Error"] = o.ok
            ? $"Loại chi phí {o.expenseType}: {o.amount.ToString("N0")}đ → {o.point.ToString("N0")} điểm (chiết khấu {o.discountRate}%, tính lượt DV: {(o.countService ? "có" : "không")})."
            : o.msg;
        return RedirectToAction(nameof(Index));
    }
}