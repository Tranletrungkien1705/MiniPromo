using Microsoft.AspNetCore.Mvc;
using MiniPromo.Data;
using MiniPromo.Models;
using MiniPromo.Services;

namespace MiniPromo.Controllers;

/// <summary>
/// API JSON cho SPA React. DTO phẳng. Dashboard cache Redis 30s theo tenant (X-Cache).
/// Chiến dịch quay thưởng: Draft → Running → Ended. Chơi (Play) xuyên tenant theo mã chiến dịch, 1 mã tem chơi 1 lần.
/// </summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public class ApiV1Controller(IPromoService svc, IVoucherService vouchers, IVoucherProgramService programs, ICarPromotionService carPromos, IPromotionProgramService promotions, ICarRecommendService carRecommends, ICardPromotionProgramService cardPrograms, IBirthdayPolicyService birthdayPolicies, IBirthdayVoucherService birthdayVouchers, IIssueVoucherService issueVouchers, IParamPromotionService paramPromotions, IRankPolicyService rankPolicies, IPolicyMoneyToPointService moneyToPoints, IMemberDiscountService memberDiscounts, IPromotionTypeService promotionTypes, IDiscountCodeService discountCodes, IVoucherIdService voucherIds, IIntroductionGrantService introductionGrants, ICarPurchasePointService carPurchasePoints, IPolicyExpenseTypeService policyExpenseTypes, ICache cache, ITenantContext tenant) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var key = $"promo:dash:{tenant.OrgId}";
        var hit = await cache.GetAsync<DashDto>(key);
        if (hit != null) { Response.Headers["X-Cache"] = "HIT"; return Ok(hit); }
        var d = await svc.DashboardAsync();
        var dto = new DashDto(d.Campaigns, d.Running, d.TotalPlays, d.TotalWins, d.ValueAwarded,
            d.Top.Select(t => new TopDto(t.Campaign.Name, t.Plays, t.Wins, t.ValueAwarded)).ToList());
        await cache.SetAsync(key, dto, TimeSpan.FromSeconds(30));
        Response.Headers["X-Cache"] = "MISS";
        return Ok(dto);
    }

    [HttpGet("campaigns")]
    public async Task<IActionResult> Campaigns()
        => Ok((await svc.CampaignsAsync()).Select(c => new
        {
            c.Id, c.Code, c.Name, c.FromDate, c.ToDate,
            status = (int)c.Status, statusText = Ui.Camp(c.Status).text, statusCss = Ui.Camp(c.Status).css,
            live = c.IsLiveNow, prizes = c.Prizes.Count
        }));

    [HttpGet("campaigns/{id:int}")]
    public async Task<IActionResult> Campaign(int id)
    {
        var c = await svc.GetCampaignAsync(id);
        if (c == null) return NotFound(new { error = "Không tìm thấy chiến dịch." });
        var stat = await svc.StatAsync(id);
        return Ok(new
        {
            c.Id, c.Code, c.Name, c.Description, c.FromDate, c.ToDate, c.LoseWeight,
            status = (int)c.Status, statusText = Ui.Camp(c.Status).text, live = c.IsLiveNow,
            plays = stat.Plays, wins = stat.Wins, valueAwarded = stat.ValueAwarded,
            prizes = c.Prizes.Select(p => new { p.Id, p.Name, p.Tier, p.Value, p.Quantity, p.Awarded, remaining = p.Remaining, p.Weight })
        });
    }

    [HttpPost("campaigns")]
    public async Task<IActionResult> CreateCampaign([FromBody] CampaignReq r)
    {
        var (ok, msg, id) = await svc.CreateCampaignAsync(new Campaign
        {
            Code = r.Code ?? "", Name = r.Name, Description = r.Description,
            FromDate = r.FromDate == default ? DateTime.Today : r.FromDate,
            ToDate = r.ToDate == default ? DateTime.Today.AddMonths(1) : r.ToDate,
            LoseWeight = r.LoseWeight <= 0 ? 100 : r.LoseWeight
        });
        return ok ? Ok(new { id }) : BadRequest(new { error = msg });
    }

    [HttpPost("campaigns/{id:int}/status")]
    public async Task<IActionResult> SetStatus(int id, [FromBody] StatusReq r)
    {
        var (ok, msg) = await svc.SetStatusAsync(id, (CampaignStatus)r.Status);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    [HttpPost("campaigns/{id:int}/prizes")]
    public async Task<IActionResult> AddPrize(int id, [FromBody] PrizeReq r)
    {
        var (ok, msg) = await svc.AddPrizeAsync(new Prize { CampaignId = id, Name = r.Name, Tier = r.Tier ?? "", Value = r.Value, Quantity = r.Quantity, Weight = r.Weight <= 0 ? 1 : r.Weight });
        return ok ? Ok(new { ok }) : BadRequest(new { error = msg });
    }

    [HttpGet("entries")]
    public async Task<IActionResult> Entries([FromQuery] int? campaignId, [FromQuery] PlayResult? result)
        => Ok((await svc.EntriesAsync(campaignId, result)).Select(e => new
        {
            e.Id, campaign = e.Campaign?.Name, e.Code, e.CustomerName, e.Phone,
            result = (int)e.Result, win = e.Result == PlayResult.Win, e.PrizeName,
            claim = (int)e.Claim, claimText = Ui.Claim(e.Claim).text, e.CreatedAt
        }));

    [HttpPost("entries/{id:int}/claim")]
    public async Task<IActionResult> SetClaim(int id, [FromBody] ClaimReq r)
    {
        var (ok, msg) = await svc.SetClaimAsync(id, (ClaimStatus)r.Status);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    // Chơi công khai theo mã chiến dịch (xuyên tenant).
    [HttpPost("play")]
    public async Task<IActionResult> Play([FromBody] PlayReq r)
    {
        var o = await svc.PlayAsync(r.CampaignCode ?? "", r.Code ?? "", r.Name, r.Phone);
        return o.ok ? Ok(new { ok = o.ok, msg = o.msg, win = o.win, prize = o.prizeName, value = o.prizeValue })
                    : BadRequest(new { ok = o.ok, error = o.msg });
    }

    // ---- Mã giảm giá / điểm voucher (port từ Crd_MemberVoucher) ----
    [HttpGet("vouchers")]
    public async Task<IActionResult> Vouchers()
        => Ok((await vouchers.VouchersAsync()).Select(v => new
        {
            v.Id, v.Code, v.Name, v.MemberNo, v.PointTotal, v.PointRemain, v.PointLimit,
            v.QtyUseLimit, v.QtyUseRemain, v.ExpireDate, v.Active,
            status = (int)v.Status, statusText = Ui.Voucher(v.Status).text, statusCss = Ui.Voucher(v.Status).css,
            usable = v.IsUsable
        }));

    [HttpGet("vouchers/{id:int}")]
    public async Task<IActionResult> Voucher(int id)
    {
        var v = await vouchers.GetVoucherAsync(id);
        if (v == null) return NotFound(new { error = "Không tìm thấy voucher." });
        var stat = await vouchers.StatAsync(id);
        return Ok(new
        {
            v.Id, v.Code, v.Name, v.MemberNo, v.PointTotal, v.PointRemain, v.PointLimit,
            v.QtyUseLimit, v.QtyUseRemain, v.ExpireDate, v.Active,
            status = (int)v.Status, statusText = Ui.Voucher(v.Status).text, usable = v.IsUsable,
            redemptions = stat.Redemptions, pointUsed = stat.PointUsed
        });
    }

    [HttpPost("vouchers")]
    public async Task<IActionResult> CreateVoucher([FromBody] VoucherReq r)
    {
        var (ok, msg, id) = await vouchers.CreateVoucherAsync(new Voucher
        {
            Code = r.Code ?? "", Name = r.Name, MemberNo = r.MemberNo,
            PointTotal = r.PointTotal, PointLimit = r.PointLimit,
            QtyUseLimit = r.QtyUseLimit <= 0 ? 1 : r.QtyUseLimit,
            ExpireDate = r.ExpireDate == default ? DateTime.Today.AddMonths(1) : r.ExpireDate
        });
        return ok ? Ok(new { id }) : BadRequest(new { error = msg });
    }

    [HttpPost("vouchers/{id:int}/active")]
    public async Task<IActionResult> SetVoucherActive(int id, [FromBody] ActiveReq r)
    {
        var (ok, msg) = await vouchers.SetActiveAsync(id, r.Active);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    [HttpGet("voucher-redemptions")]
    public async Task<IActionResult> VoucherRedemptions([FromQuery] int? voucherId)
        => Ok((await vouchers.RedemptionsAsync(voucherId)).Select(r => new
        {
            r.Id, voucher = r.Voucher?.Name, r.VoucherId, r.MemberNo,
            r.PointUsed, r.PointRemainAfter, r.QtyUseRemainAfter, r.CreatedAt
        }));

    // Dùng voucher công khai theo mã (xuyên tenant).
    [HttpPost("voucher/redeem")]
    public async Task<IActionResult> Redeem([FromBody] RedeemReq r)
    {
        var o = await vouchers.RedeemAsync(r.Code ?? "", r.Amount, r.MemberNo);
        return o.ok ? Ok(new { ok = o.ok, msg = o.msg, pointUsed = o.pointUsed, pointRemain = o.pointRemain, qtyUseRemain = o.qtyUseRemain })
                    : BadRequest(new { ok = o.ok, error = o.msg });
    }

    // ---- Chương trình voucher theo model + điều kiện áp dụng (port từ Prm_VoucherNewCar) ----
    [HttpGet("voucher-programs")]
    public async Task<IActionResult> VoucherPrograms()
        => Ok((await programs.ProgramsAsync()).Select(p => new
        {
            p.Id, p.Code, p.Name, p.EffDateStart, p.EffDateEnd, p.ValidityPeriod, p.QtyDayLimitFDlvDate,
            p.FlagAllModel, p.PointVoucherAllModel, p.PointUseLimitAllModel,
            status = (int)p.Status, statusText = Ui.VoucherProgram(p.Status).text, statusCss = Ui.VoucherProgram(p.Status).css,
            live = p.IsLiveNow, details = p.Details.Count
        }));

    [HttpGet("voucher-programs/{id:int}")]
    public async Task<IActionResult> VoucherProgram(int id)
    {
        var p = await programs.GetProgramAsync(id);
        if (p == null) return NotFound(new { error = "Không tìm thấy chương trình." });
        return Ok(new
        {
            p.Id, p.Code, p.Name, p.EffDateStart, p.EffDateEnd, p.ValidityPeriod, p.QtyDayLimitFDlvDate,
            p.FlagAllModel, p.PointVoucherAllModel, p.PointUseLimitAllModel, p.Remark,
            status = (int)p.Status, statusText = Ui.VoucherProgram(p.Status).text, live = p.IsLiveNow,
            details = p.Details.Select(d => new { d.Id, d.ModelCode, d.PointVoucher, d.PointUseLimit, d.Remark })
        });
    }

    [HttpPost("voucher-programs")]
    public async Task<IActionResult> CreateVoucherProgram([FromBody] VoucherProgramReq r)
    {
        var (ok, msg, id) = await programs.CreateProgramAsync(new VoucherProgram
        {
            Code = r.Code ?? "", Name = r.Name,
            EffDateStart = r.EffDateStart == default ? DateTime.Today : r.EffDateStart,
            EffDateEnd = r.EffDateEnd == default ? DateTime.Today.AddMonths(1) : r.EffDateEnd,
            ValidityPeriod = r.ValidityPeriod, QtyDayLimitFDlvDate = r.QtyDayLimitFDlvDate,
            FlagAllModel = r.FlagAllModel, PointVoucherAllModel = r.PointVoucherAllModel,
            PointUseLimitAllModel = r.PointUseLimitAllModel, Remark = r.Remark
        });
        return ok ? Ok(new { id }) : BadRequest(new { error = msg });
    }

    [HttpPost("voucher-programs/{id:int}/details")]
    public async Task<IActionResult> AddVoucherProgramDetail(int id, [FromBody] VoucherProgramDtlReq r)
    {
        var (ok, msg) = await programs.AddDetailAsync(new VoucherProgramDtl
        {
            VoucherProgramId = id, ModelCode = r.ModelCode ?? "",
            PointVoucher = r.PointVoucher, PointUseLimit = r.PointUseLimit, Remark = r.Remark
        });
        return ok ? Ok(new { ok }) : BadRequest(new { error = msg });
    }

    [HttpPost("voucher-programs/{id:int}/status")]
    public async Task<IActionResult> SetVoucherProgramStatus(int id, [FromBody] StatusReq r)
    {
        var (ok, msg) = await programs.SetStatusAsync(id, (VoucherProgramStatus)r.Status);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    // Hoàn tất chương trình (port từ Prm_VoucherNewCar_Finish).
    [HttpPost("voucher-programs/{id:int}/finish")]
    public async Task<IActionResult> FinishVoucherProgram(int id, [FromBody] RemarkReq? r)
    {
        var (ok, msg) = await programs.FinishAsync(id, r?.Remark);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    // Huỷ chương trình (port từ Prm_VoucherNewCar_Cancel).
    [HttpPost("voucher-programs/{id:int}/cancel")]
    public async Task<IActionResult> CancelVoucherProgram(int id, [FromBody] RemarkReq? r)
    {
        var (ok, msg) = await programs.CancelAsync(id, r?.Remark);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    // Đối soát số voucher đã phát theo chương trình + model.
    [HttpGet("voucher-programs/reconciliation")]
    public async Task<IActionResult> VoucherReconciliation([FromQuery] int? programId)
        => Ok((await programs.ReconciliationAsync(programId)).Select(r => new
        {
            r.VoucherProgramId, r.ProgramCode, r.ProgramName, r.ModelCode,
            r.Issued, r.Used, r.Unused, r.PointIssued, r.PointUsed, r.PointRemain
        }));

    // Tính giá trị voucher cho một xe theo chương trình đang hiệu lực (công khai).
    [HttpPost("voucher-program/calc")]
    public async Task<IActionResult> CalcVoucher([FromBody] VoucherCalcReq r)
    {
        var o = await programs.CalcAsync(r.ModelCode ?? "", r.DeliveryDate, r.RegistrationDate);
        return o.ok ? Ok(new { ok = o.ok, msg = o.msg, pointVoucher = o.pointVoucher, pointUseLimit = o.pointUseLimit, modelCode = o.modelCode })
                    : BadRequest(new { ok = o.ok, error = o.msg });
    }

    // Phát voucher cho một xe theo chương trình đang hiệu lực (công khai) — tạo Voucher gắn chương trình.
    [HttpPost("voucher-program/issue")]
    public async Task<IActionResult> IssueVoucher([FromBody] VoucherIssueReq r)
    {
        var o = await programs.IssueAsync(r.ModelCode ?? "", r.DeliveryDate, r.RegistrationDate, r.MemberNo);
        return o.ok ? Ok(new { ok = o.ok, msg = o.msg, voucherId = o.voucherId, voucherCode = o.voucherCode, expireDate = o.expireDate })
                    : BadRequest(new { ok = o.ok, error = o.msg });
    }

    // ---- Chương trình khuyến mại mua xe mới (port từ Prm_CarNew) ----
    [HttpGet("car-promotions")]
    public async Task<IActionResult> CarPromotions()
        => Ok((await carPromos.PromotionsAsync()).Select(p => new
        {
            p.Id, p.Code, p.Name, p.DealerCode, p.EffDateStart, p.EffDateEnd,
            p.FlagAllModel, p.PointValAllModel,
            status = (int)p.Status, statusText = Ui.CarPromotion(p.Status).text, statusCss = Ui.CarPromotion(p.Status).css,
            live = p.IsLiveNow, details = p.Details.Count
        }));

    [HttpGet("car-promotions/{id:int}")]
    public async Task<IActionResult> CarPromotion(int id)
    {
        var p = await carPromos.GetPromotionAsync(id);
        if (p == null) return NotFound(new { error = "Không tìm thấy chương trình." });
        return Ok(new
        {
            p.Id, p.Code, p.Name, p.DealerCode, p.EffDateStart, p.EffDateEnd,
            p.FlagAllModel, p.PointValAllModel, p.Remark,
            status = (int)p.Status, statusText = Ui.CarPromotion(p.Status).text, live = p.IsLiveNow,
            details = p.Details.Select(d => new { d.Id, d.ModelCode, d.PointVal, d.Remark })
        });
    }

    [HttpPost("car-promotions")]
    public async Task<IActionResult> CreateCarPromotion([FromBody] CarPromotionReq r)
    {
        var (ok, msg, id) = await carPromos.CreatePromotionAsync(new CarPromotion
        {
            Code = r.Code ?? "", Name = r.Name, DealerCode = r.DealerCode ?? "",
            EffDateStart = r.EffDateStart == default ? DateTime.Today : r.EffDateStart,
            EffDateEnd = r.EffDateEnd == default ? DateTime.Today.AddMonths(1) : r.EffDateEnd,
            FlagAllModel = r.FlagAllModel, PointValAllModel = r.PointValAllModel, Remark = r.Remark
        });
        return ok ? Ok(new { id }) : BadRequest(new { error = msg });
    }

    [HttpPost("car-promotions/{id:int}/details")]
    public async Task<IActionResult> AddCarPromotionDetail(int id, [FromBody] CarPromotionDtlReq r)
    {
        var (ok, msg) = await carPromos.AddDetailAsync(new CarPromotionDtl
        {
            CarPromotionId = id, ModelCode = r.ModelCode ?? "", PointVal = r.PointVal, Remark = r.Remark
        });
        return ok ? Ok(new { ok }) : BadRequest(new { error = msg });
    }

    [HttpPost("car-promotions/{id:int}/status")]
    public async Task<IActionResult> SetCarPromotionStatus(int id, [FromBody] StatusReq r)
    {
        var (ok, msg) = await carPromos.SetStatusAsync(id, (CarPromotionStatus)r.Status);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    // Duyệt chương trình (port từ Prm_CarNew_Appr).
    [HttpPost("car-promotions/{id:int}/approve")]
    public async Task<IActionResult> ApproveCarPromotion(int id, [FromBody] RemarkReq? r)
    {
        var (ok, msg) = await carPromos.ApproveAsync(id, r?.Remark);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    // Hoàn tất chương trình (port từ Prm_CarNew_Finish).
    [HttpPost("car-promotions/{id:int}/finish")]
    public async Task<IActionResult> FinishCarPromotion(int id, [FromBody] RemarkReq? r)
    {
        var (ok, msg) = await carPromos.FinishAsync(id, r?.Remark);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    // Huỷ chương trình (port từ Prm_CarNew_Cancel).
    [HttpPost("car-promotions/{id:int}/cancel")]
    public async Task<IActionResult> CancelCarPromotion(int id, [FromBody] RemarkReq? r)
    {
        var (ok, msg) = await carPromos.CancelAsync(id, r?.Remark);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    // Tính giá trị khuyến mại cho một model theo chương trình đang hiệu lực (công khai).
    [HttpPost("car-promotion/calc")]
    public async Task<IActionResult> CalcCarPromotion([FromBody] CarPromoCalcReq r)
    {
        var o = await carPromos.CalcAsync(r.DealerCode ?? "", r.ModelCode ?? "");
        return o.ok ? Ok(new { ok = o.ok, msg = o.msg, pointVal = o.pointVal, modelCode = o.modelCode })
                    : BadRequest(new { ok = o.ok, error = o.msg });
    }

    // ---- Chương trình khuyến mại chung (port từ Prm_Promotion) ----
    [HttpGet("promotion-programs")]
    public async Task<IActionResult> PromotionPrograms()
        => Ok((await promotions.ProgramsAsync()).Select(p => new
        {
            p.Id, p.Code, p.Name, p.BudgetVal, p.EffDTimeStart, p.EffDTimeEnd,
            mainType = (int)p.MainType, mainTypeText = Ui.MainTypeText(p.MainType),
            prmType = (int)p.PrmType, prmTypeText = Ui.PrmTypeText(p.PrmType),
            p.FlagParallel, p.FlagMulti,
            status = (int)p.Status, statusText = Ui.Promotion(p.Status).text, statusCss = Ui.Promotion(p.Status).css,
            live = p.IsLiveNow, scopes = p.Scopes.Count, prms = p.Prms.Count, mains = p.Mains.Count
        }));

    [HttpGet("promotion-programs/{id:int}")]
    public async Task<IActionResult> PromotionProgram(int id)
    {
        var p = await promotions.GetProgramAsync(id);
        if (p == null) return NotFound(new { error = "Không tìm thấy chương trình." });
        return Ok(new
        {
            p.Id, p.Code, p.Name, p.BudgetVal, p.EffDTimeStart, p.EffDTimeEnd,
            mainType = (int)p.MainType, mainTypeText = Ui.MainTypeText(p.MainType),
            prmType = (int)p.PrmType, prmTypeText = Ui.PrmTypeText(p.PrmType),
            p.FlagParallel, p.FlagMulti, p.FlagAllMonth, p.FlagAllDay, p.FlagAllDayOfWeek, p.FlagAllTime, p.Remark,
            status = (int)p.Status, statusText = Ui.Promotion(p.Status).text, live = p.IsLiveNow,
            scopes = p.Scopes.Select(s => new { s.Id, scopeType = (int)s.ScopeType, scopeTypeText = Ui.ScopeTypeText(s.ScopeType), s.Value, s.ValueEnd, s.Active }),
            prms = p.Prms.Select(x => new { x.Id, x.Idx, x.Qty, x.UPDc, x.UPRateDc, x.UPDcMax, x.ValOrdDc, x.ValOrdRateDc, x.ValOrdDcMax, x.Remark }),
            mains = p.Mains.Select(m => new { m.Id, m.Idx, m.Qty, m.Amount, m.TotalValOrd }),
            productScopes = p.ProductScopes.Select(s => new { s.Id, kind = (int)s.Kind, kindText = Ui.ProductScopeKindText(s.Kind), refType = (int)s.RefType, refTypeText = Ui.RefTypeText(s.RefType), s.RefCode, s.RefName, s.Idx, s.MapIdx, s.FlagActive, s.Remark })
        });
    }

    [HttpPost("promotion-programs")]
    public async Task<IActionResult> CreatePromotionProgram([FromBody] PromotionProgramReq r)
    {
        var (ok, msg, id) = await promotions.CreateProgramAsync(new PromotionProgram
        {
            Code = r.Code ?? "", Name = r.Name, MainType = (PromotionMainType)r.MainType, PrmType = (PromotionPrmType)r.PrmType,
            BudgetVal = r.BudgetVal,
            EffDTimeStart = r.EffDTimeStart == default ? DateTime.Today : r.EffDTimeStart,
            EffDTimeEnd = r.EffDTimeEnd == default ? DateTime.Today.AddMonths(1) : r.EffDTimeEnd,
            FlagParallel = r.FlagParallel, FlagMulti = r.FlagMulti, Remark = r.Remark
        });
        return ok ? Ok(new { id }) : BadRequest(new { error = msg });
    }

    [HttpPost("promotion-programs/{id:int}/scopes")]
    public async Task<IActionResult> AddPromotionScope(int id, [FromBody] PromotionScopeReq r)
    {
        var (ok, msg) = await promotions.AddScopeAsync(new PromotionScope
        {
            PromotionProgramId = id, ScopeType = (PromotionScopeType)r.ScopeType, Value = r.Value ?? "", ValueEnd = r.ValueEnd
        });
        return ok ? Ok(new { ok }) : BadRequest(new { error = msg });
    }

    [HttpPost("promotion-programs/{id:int}/prms")]
    public async Task<IActionResult> AddPromotionPrm(int id, [FromBody] PromotionPrmReq r)
    {
        var (ok, msg) = await promotions.AddPrmAsync(new PromotionPrm
        {
            PromotionProgramId = id, Idx = r.Idx, Qty = r.Qty, UPDc = r.UPDc, UPRateDc = r.UPRateDc, UPDcMax = r.UPDcMax,
            ValOrdDc = r.ValOrdDc, ValOrdRateDc = r.ValOrdRateDc, ValOrdDcMax = r.ValOrdDcMax, Remark = r.Remark
        });
        return ok ? Ok(new { ok }) : BadRequest(new { error = msg });
    }

    [HttpPost("promotion-programs/{id:int}/mains")]
    public async Task<IActionResult> AddPromotionMain(int id, [FromBody] PromotionMainReq r)
    {
        var (ok, msg) = await promotions.AddMainAsync(new PromotionMain
        {
            PromotionProgramId = id, Idx = r.Idx, Qty = r.Qty, Amount = r.Amount, TotalValOrd = r.TotalValOrd
        });
        return ok ? Ok(new { ok }) : BadRequest(new { error = msg });
    }

    // Thêm phạm vi sản phẩm/nhóm sản phẩm áp dụng (port từ Prm_PromotionMainSpec/Prm_PromotionPrmSpec).
    [HttpPost("promotion-programs/{id:int}/product-scopes")]
    public async Task<IActionResult> AddPromotionProductScope(int id, [FromBody] PromotionProductScopeReq r)
    {
        var (ok, msg) = await promotions.AddProductScopeAsync(new PromotionProductScope
        {
            PromotionProgramId = id, Kind = (PromotionProductScopeKind)r.Kind, Idx = r.Idx,
            RefType = (PromotionRefType)r.RefType, RefCode = r.RefCode ?? "", RefName = r.RefName,
            MapIdx = r.MapIdx, FlagActive = r.FlagActive, Remark = r.Remark
        });
        return ok ? Ok(new { ok }) : BadRequest(new { error = msg });
    }

    [HttpPost("promotion-programs/{id:int}/status")]
    public async Task<IActionResult> SetPromotionProgramStatus(int id, [FromBody] StatusReq r)
    {
        var (ok, msg) = await promotions.SetStatusAsync(id, (PromotionStatus)r.Status);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    // Duyệt chương trình.
    [HttpPost("promotion-programs/{id:int}/approve")]
    public async Task<IActionResult> ApprovePromotionProgram(int id, [FromBody] RemarkReq? r)
    {
        var (ok, msg) = await promotions.ApproveAsync(id, r?.Remark);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    // Hoàn tất chương trình.
    [HttpPost("promotion-programs/{id:int}/finish")]
    public async Task<IActionResult> FinishPromotionProgram(int id, [FromBody] RemarkReq? r)
    {
        var (ok, msg) = await promotions.FinishAsync(id, r?.Remark);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    // Huỷ chương trình.
    [HttpPost("promotion-programs/{id:int}/cancel")]
    public async Task<IActionResult> CancelPromotionProgram(int id, [FromBody] RemarkReq? r)
    {
        var (ok, msg) = await promotions.CancelAsync(id, r?.Remark);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    // Tính khuyến mại cho một đơn hàng theo chương trình đang hiệu lực (công khai).
    [HttpPost("promotion/calc")]
    public async Task<IActionResult> CalcPromotion([FromBody] PromotionCalcReq r)
    {
        var lines = r.Lines?.Select(l => new PromotionOrderLine(l.RefCode ?? "", (PromotionRefType)l.RefType, l.Qty, l.Amount)).ToList();
        var o = await promotions.CalcAsync(r.OrderAmount, r.Qty, r.At, lines);
        return o.ok ? Ok(new { ok = o.ok, msg = o.msg, productDiscount = o.productDiscount, orderDiscount = o.orderDiscount, totalDiscount = o.totalDiscount, programCode = o.programCode })
                    : BadRequest(new { ok = o.ok, error = o.msg });
    }

    // ---- Chương trình giới thiệu xe (port từ Prm_CarRecommend) ----
    [HttpGet("car-recommends")]
    public async Task<IActionResult> CarRecommends()
        => Ok((await carRecommends.RecommendsAsync()).Select(p => new
        {
            p.Id, p.Code, p.Name, p.DealerCode, p.EffDateStart, p.EffDateEnd,
            p.FlagAllModel, p.PointValAllModel,
            status = (int)p.Status, statusText = Ui.CarRecommend(p.Status).text, statusCss = Ui.CarRecommend(p.Status).css,
            live = p.IsLiveNow, details = p.Details.Count
        }));

    [HttpGet("car-recommends/{id:int}")]
    public async Task<IActionResult> CarRecommend(int id)
    {
        var p = await carRecommends.GetRecommendAsync(id);
        if (p == null) return NotFound(new { error = "Không tìm thấy chương trình." });
        return Ok(new
        {
            p.Id, p.Code, p.Name, p.DealerCode, p.EffDateStart, p.EffDateEnd,
            p.FlagAllModel, p.PointValAllModel, p.Remark,
            status = (int)p.Status, statusText = Ui.CarRecommend(p.Status).text, live = p.IsLiveNow,
            details = p.Details.Select(d => new { d.Id, d.ModelCode, d.PointVal, d.Remark })
        });
    }

    [HttpPost("car-recommends")]
    public async Task<IActionResult> CreateCarRecommend([FromBody] CarRecommendReq r)
    {
        var (ok, msg, id) = await carRecommends.CreateRecommendAsync(new CarRecommend
        {
            Code = r.Code ?? "", Name = r.Name, DealerCode = r.DealerCode ?? "",
            EffDateStart = r.EffDateStart == default ? DateTime.Today : r.EffDateStart,
            EffDateEnd = r.EffDateEnd == default ? DateTime.Today.AddMonths(1) : r.EffDateEnd,
            FlagAllModel = r.FlagAllModel, PointValAllModel = r.PointValAllModel, Remark = r.Remark
        });
        return ok ? Ok(new { id }) : BadRequest(new { error = msg });
    }

    [HttpPost("car-recommends/{id:int}/details")]
    public async Task<IActionResult> AddCarRecommendDetail(int id, [FromBody] CarRecommendDtlReq r)
    {
        var (ok, msg) = await carRecommends.AddDetailAsync(new CarRecommendDtl
        {
            CarRecommendId = id, ModelCode = r.ModelCode ?? "", PointVal = r.PointVal, Remark = r.Remark
        });
        return ok ? Ok(new { ok }) : BadRequest(new { error = msg });
    }

    [HttpPost("car-recommends/{id:int}/status")]
    public async Task<IActionResult> SetCarRecommendStatus(int id, [FromBody] StatusReq r)
    {
        var (ok, msg) = await carRecommends.SetStatusAsync(id, (CarRecommendStatus)r.Status);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    // Duyệt chương trình (port từ Prm_CarRecommend_Appr).
    [HttpPost("car-recommends/{id:int}/approve")]
    public async Task<IActionResult> ApproveCarRecommend(int id, [FromBody] RemarkReq? r)
    {
        var (ok, msg) = await carRecommends.ApproveAsync(id, r?.Remark);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    // Hoàn tất chương trình (port từ Prm_CarRecommend_Finish).
    [HttpPost("car-recommends/{id:int}/finish")]
    public async Task<IActionResult> FinishCarRecommend(int id, [FromBody] RemarkReq? r)
    {
        var (ok, msg) = await carRecommends.FinishAsync(id, r?.Remark);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    // Huỷ chương trình (port từ Prm_CarRecommend_Cancel).
    [HttpPost("car-recommends/{id:int}/cancel")]
    public async Task<IActionResult> CancelCarRecommend(int id, [FromBody] RemarkReq? r)
    {
        var (ok, msg) = await carRecommends.CancelAsync(id, r?.Remark);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    // Tính giá trị thưởng giới thiệu cho một model theo chương trình đang hiệu lực (công khai).
    [HttpPost("car-recommend/calc")]
    public async Task<IActionResult> CalcCarRecommend([FromBody] CarRecommendCalcReq r)
    {
        var o = await carRecommends.CalcAsync(r.DealerCode ?? "", r.ModelCode ?? "");
        return o.ok ? Ok(new { ok = o.ok, msg = o.msg, pointVal = o.pointVal, modelCode = o.modelCode })
                    : BadRequest(new { ok = o.ok, error = o.msg });
    }

    // ---- Chương trình khuyến mại theo loại thẻ (port từ Mst_PromotionProgram) ----
    [HttpGet("card-promotion-programs")]
    public async Task<IActionResult> CardPromotionPrograms()
        => Ok((await cardPrograms.ProgramsAsync()).Select(p => new
        {
            p.Id, p.Code, p.Name, p.EffDateStart, p.EffDateEnd, p.FlagAllDL,
            status = (int)p.Status, statusText = Ui.CardPromotionProgram(p.Status).text, statusCss = Ui.CardPromotionProgram(p.Status).css,
            live = p.IsLiveNow, details = p.Details.Count, dealers = p.Dealers.Count
        }));

    [HttpGet("card-promotion-programs/{id:int}")]
    public async Task<IActionResult> CardPromotionProgram(int id)
    {
        var p = await cardPrograms.GetProgramAsync(id);
        if (p == null) return NotFound(new { error = "Không tìm thấy chương trình." });
        return Ok(new
        {
            p.Id, p.Code, p.Name, p.EffDateStart, p.EffDateEnd, p.FlagAllDL, p.Remark,
            status = (int)p.Status, statusText = Ui.CardPromotionProgram(p.Status).text, live = p.IsLiveNow,
            details = p.Details.Select(d => new { d.Id, d.CardType, d.Qty, d.Unit, d.FlagActive, d.Remark }),
            dealers = p.Dealers.Select(s => new { s.Id, s.DealerCode })
        });
    }

    [HttpPost("card-promotion-programs")]
    public async Task<IActionResult> CreateCardPromotionProgram([FromBody] CardPromotionProgramReq r)
    {
        var (ok, msg, id) = await cardPrograms.CreateProgramAsync(new CardPromotionProgram
        {
            Code = r.Code ?? "", Name = r.Name,
            EffDateStart = r.EffDateStart == default ? DateTime.Today : r.EffDateStart,
            EffDateEnd = r.EffDateEnd == default ? DateTime.Today.AddMonths(1) : r.EffDateEnd,
            FlagAllDL = r.FlagAllDL, Remark = r.Remark
        });
        return ok ? Ok(new { id }) : BadRequest(new { error = msg });
    }

    [HttpPost("card-promotion-programs/{id:int}/details")]
    public async Task<IActionResult> AddCardPromotionProgramDetail(int id, [FromBody] CardPromotionProgramDtlReq r)
    {
        var (ok, msg) = await cardPrograms.AddDetailAsync(new CardPromotionProgramDtl
        {
            CardPromotionProgramId = id, CardType = r.CardType ?? "", Qty = r.Qty, Unit = r.Unit, Remark = r.Remark
        });
        return ok ? Ok(new { ok }) : BadRequest(new { error = msg });
    }

    [HttpPost("card-promotion-programs/{id:int}/dealers")]
    public async Task<IActionResult> AddCardPromotionProgramDealer(int id, [FromBody] CardPromotionProgramSpecReq r)
    {
        var (ok, msg) = await cardPrograms.AddDealerAsync(new CardPromotionProgramSpec { CardPromotionProgramId = id, DealerCode = r.DealerCode ?? "" });
        return ok ? Ok(new { ok }) : BadRequest(new { error = msg });
    }

    [HttpPost("card-promotion-programs/{id:int}/status")]
    public async Task<IActionResult> SetCardPromotionProgramStatus(int id, [FromBody] StatusReq r)
    {
        var (ok, msg) = await cardPrograms.SetStatusAsync(id, (CardPromotionProgramStatus)r.Status);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    // Đối soát số lượng ưu đãi đã dùng theo chương trình + loại thẻ.
    [HttpGet("card-promotion-programs/reconciliation")]
    public async Task<IActionResult> CardPromotionReconciliation([FromQuery] int? programId)
        => Ok((await cardPrograms.ReconciliationAsync(programId)).Select(r => new
        {
            r.CardPromotionProgramId, r.ProgramCode, r.ProgramName, r.CardType, r.Qty, r.QtyUsed, r.QtyRemain
        }));

    // Kiểm tra một giao dịch có được dùng ưu đãi của chương trình theo loại thẻ (công khai).
    [HttpPost("card-promotion/check")]
    public async Task<IActionResult> CheckCardPromotion([FromBody] CardPromotionUseReq r)
    {
        var o = await cardPrograms.CheckUseAsync(r.DealerCode ?? "", r.CardType ?? "", r.Qty);
        return o.ok ? Ok(new { ok = o.ok, msg = o.msg, qtyRemain = o.qtyRemain })
                    : BadRequest(new { ok = o.ok, error = o.msg });
    }

    // Ghi nhận sử dụng ưu đãi của chương trình theo loại thẻ cho một giao dịch (công khai).
    [HttpPost("card-promotion/use")]
    public async Task<IActionResult> UseCardPromotion([FromBody] CardPromotionUseReq r)
    {
        var o = await cardPrograms.UseAsync(r.DealNo ?? "", r.DealerCode ?? "", r.CardNo ?? "", r.CardType ?? "", r.Qty, r.MemberNo, r.At);
        return o.ok ? Ok(new { ok = o.ok, msg = o.msg, qtyRemain = o.qtyRemain, qtyUsed = o.qtyUsed })
                    : BadRequest(new { ok = o.ok, error = o.msg });
    }

    // Danh sách chương trình ưu đãi khả dụng cho một hội viên/thẻ (port từ Crd_Card_GetForPromotion).
    [HttpGet("card-promotion-programs/available")]
    public async Task<IActionResult> AvailableCardPromotions([FromQuery] string? cardType, [FromQuery] string? dealerCode, [FromQuery] DateTime? at)
        => Ok((await cardPrograms.AvailableForCardAsync(cardType ?? "", dealerCode ?? "", at)).Select(r => new
        {
            r.CardPromotionProgramId, r.ProgramCode, r.ProgramName, r.EffDateStart,
            r.QtyPr, r.QtyPrUsed, r.QtyRemain, r.FlagShow
        }));

    // ---- Chương trình tặng điểm sinh nhật (port từ Mst_BirthPolicy) ----
    [HttpGet("birthday-policies")]
    public async Task<IActionResult> BirthdayPolicies()
        => Ok((await birthdayPolicies.PoliciesAsync()).Select(p => new
        {
            p.Id, p.Code, p.Name, p.EffDateStart, p.EffDateEnd, p.FlagPoint, p.ParamValue,
            status = (int)p.Status, statusText = Ui.BirthdayPolicy(p.Status).text, statusCss = Ui.BirthdayPolicy(p.Status).css,
            live = p.IsLiveNow, details = p.Details.Count
        }));

    [HttpGet("birthday-policies/{id:int}")]
    public async Task<IActionResult> BirthdayPolicy(int id)
    {
        var p = await birthdayPolicies.GetPolicyAsync(id);
        if (p == null) return NotFound(new { error = "Không tìm thấy chương trình." });
        return Ok(new
        {
            p.Id, p.Code, p.Name, p.EffDateStart, p.EffDateEnd, p.FlagPoint, p.ParamValue, p.Remark,
            status = (int)p.Status, statusText = Ui.BirthdayPolicy(p.Status).text, live = p.IsLiveNow,
            details = p.Details.Select(d => new { d.Id, d.CardType, d.Point, d.Remark })
        });
    }

    [HttpPost("birthday-policies")]
    public async Task<IActionResult> CreateBirthdayPolicy([FromBody] BirthdayPolicyReq r)
    {
        var (ok, msg, id) = await birthdayPolicies.CreatePolicyAsync(new BirthdayPolicy
        {
            Code = r.Code ?? "", Name = r.Name,
            EffDateStart = r.EffDateStart == default ? DateTime.Today : r.EffDateStart,
            EffDateEnd = r.EffDateEnd == default ? DateTime.Today.AddMonths(1) : r.EffDateEnd,
            FlagPoint = r.FlagPoint, ParamValue = r.ParamValue <= 0 ? 1 : r.ParamValue, Remark = r.Remark
        });
        return ok ? Ok(new { id }) : BadRequest(new { error = msg });
    }

    [HttpPost("birthday-policies/{id:int}/details")]
    public async Task<IActionResult> AddBirthdayPolicyDetail(int id, [FromBody] BirthdayPolicyDtlReq r)
    {
        var (ok, msg) = await birthdayPolicies.AddDetailAsync(new BirthdayPolicyDtl
        {
            BirthdayPolicyId = id, CardType = r.CardType ?? "", Point = r.Point, Remark = r.Remark
        });
        return ok ? Ok(new { ok }) : BadRequest(new { error = msg });
    }

    [HttpPost("birthday-policies/{id:int}/status")]
    public async Task<IActionResult> SetBirthdayPolicyStatus(int id, [FromBody] StatusReq r)
    {
        var (ok, msg) = await birthdayPolicies.SetStatusAsync(id, (BirthdayPolicyStatus)r.Status);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    // Đối soát điểm sinh nhật đã tặng theo chương trình + loại thẻ.
    [HttpGet("birthday-policies/reconciliation")]
    public async Task<IActionResult> BirthdayReconciliation([FromQuery] int? policyId)
        => Ok((await birthdayPolicies.ReconciliationAsync(policyId)).Select(r => new
        {
            r.BirthdayPolicyId, r.PolicyCode, r.PolicyName, r.CardType, r.Granted, r.PointGranted, r.AmountGranted
        }));

    // Kiểm tra một hội viên có đủ điều kiện nhận điểm sinh nhật (công khai).
    [HttpPost("birthday-policy/check")]
    public async Task<IActionResult> CheckBirthday([FromBody] BirthdayCheckReq r)
    {
        var o = await birthdayPolicies.CheckEligibilityAsync(r.MemberNo ?? "", r.CardType ?? "", r.DateOfBirth, r.At);
        return o.ok ? Ok(new { ok = o.ok, msg = o.msg, point = o.point, amount = o.amount, cardType = o.cardType })
                    : BadRequest(new { ok = o.ok, error = o.msg });
    }

    // Tặng điểm sinh nhật cho một hội viên theo chương trình đang hiệu lực (công khai).
    [HttpPost("birthday-policy/grant")]
    public async Task<IActionResult> GrantBirthday([FromBody] BirthdayGrantReq r)
    {
        var o = await birthdayPolicies.GrantAsync(r.MemberNo ?? "", r.CardNo ?? "", r.CardType ?? "", r.DealerCode ?? "", r.DateOfBirth, r.At);
        return o.ok ? Ok(new { ok = o.ok, msg = o.msg, point = o.point, amount = o.amount, cardType = o.cardType })
                    : BadRequest(new { ok = o.ok, error = o.msg });
    }

    // ---- Voucher sinh nhật (port từ Crd_MemberVoucher, nâng cấp 20260518) ----
    [HttpGet("birthday-vouchers")]
    public async Task<IActionResult> BirthdayVouchers([FromQuery] string? memberNo)
        => Ok((await birthdayVouchers.VouchersAsync(memberNo)).Select(v => new
        {
            v.Id, v.VoucherNo, v.RefNo, v.MemberNo, v.CardNo, v.CardTypeUse, v.CardTypeInit, v.DealerCode,
            v.PointVCTotal, v.PointVCRemain, v.PointVCLimit, v.QtyUseVCLimit, v.QtyUseVCRemain,
            v.PointExpiryDate, v.CreateDate, expired = v.IsExpired, usable = v.IsUsable
        }));

    [HttpGet("birthday-vouchers/{id:int}")]
    public async Task<IActionResult> BirthdayVoucher(int id)
    {
        var v = await birthdayVouchers.GetVoucherAsync(id);
        if (v == null) return NotFound(new { error = "Không tìm thấy voucher sinh nhật." });
        return Ok(new
        {
            v.Id, v.VoucherNo, v.RefNo, v.MemberNo, v.CardNo, v.CardTypeUse, v.CardTypeInit, v.DealerCode,
            v.PointVCTotal, v.PointVCRemain, v.PointVCLimit, v.QtyUseVCLimit, v.QtyUseVCRemain,
            v.PointExpiryDate, v.CreateDate, v.Remark, expired = v.IsExpired, usable = v.IsUsable
        });
    }

    // Đối soát voucher sinh nhật đã phát theo chương trình + loại thẻ.
    [HttpGet("birthday-vouchers/reconciliation")]
    public async Task<IActionResult> BirthdayVoucherReconciliation([FromQuery] int? policyId)
        => Ok((await birthdayVouchers.ReconciliationAsync(policyId)).Select(r => new
        {
            r.BirthdayPolicyId, r.PolicyCode, r.PolicyName, r.CardType, r.Issued, r.PointIssued, r.PointRemain
        }));

    // Kiểm tra một hội viên có đủ điều kiện nhận voucher sinh nhật (công khai).
    [HttpPost("birthday-voucher/check")]
    public async Task<IActionResult> CheckBirthdayVoucher([FromBody] BirthdayVoucherCheckReq r)
    {
        var o = await birthdayVouchers.CheckEligibilityAsync(r.MemberNo ?? "", r.CardType ?? "", r.DateOfBirth, r.At);
        return o.ok ? Ok(new { ok = o.ok, msg = o.msg, point = o.point, expireDays = o.expireDays, cardType = o.cardType })
                    : BadRequest(new { ok = o.ok, error = o.msg });
    }

    // Phát voucher sinh nhật cho một hội viên theo chương trình đang hiệu lực (công khai).
    [HttpPost("birthday-voucher/issue")]
    public async Task<IActionResult> IssueBirthdayVoucher([FromBody] BirthdayVoucherIssueReq r)
    {
        var o = await birthdayVouchers.IssueAsync(r.MemberNo ?? "", r.CardNo ?? "", r.CardType ?? "", r.DateOfBirth, r.At);
        return o.ok ? Ok(new { ok = o.ok, msg = o.msg, voucherNo = o.voucherNo, point = o.point, expireDate = o.expireDate, cardType = o.cardType })
                    : BadRequest(new { ok = o.ok, error = o.msg });
    }

    // ---- Đợt phát hành voucher (port từ Mst_IssueVoucher) ----
    [HttpGet("issue-vouchers")]
    public async Task<IActionResult> IssueVouchers()
        => Ok((await issueVouchers.BatchesAsync()).Select(v => new
        {
            v.Id, v.Code, v.Name, v.EffDateStart, v.EffDateEnd, v.QtyVoucher, v.QtyDateUse,
            favorType = (int)v.FavorType, favorTypeText = Ui.FavorTypeText(v.FavorType),
            issueForm = (int)v.IssueForm, issueFormText = Ui.IssueFormText(v.IssueForm),
            v.FlagActive, activeText = v.FlagActive ? "Đang bật" : "Tạm dừng",
            live = v.IsLiveNow, details = v.Details.Count, scopes = v.Scopes.Count, products = v.Products.Count, prices = v.Prices.Count
        }));

    [HttpGet("issue-vouchers/{id:int}")]
    public async Task<IActionResult> IssueVoucher(int id)
    {
        var v = await issueVouchers.GetBatchAsync(id);
        if (v == null) return NotFound(new { error = "Không tìm thấy đợt phát hành." });
        return Ok(new
        {
            v.Id, v.Code, v.Name, v.EffDateStart, v.EffDateEnd, v.QtyVoucher, v.QtyDateUse,
            favorType = (int)v.FavorType, favorTypeText = Ui.FavorTypeText(v.FavorType),
            issueForm = (int)v.IssueForm, issueFormText = Ui.IssueFormText(v.IssueForm),
            v.FlagConditionUsePrd, v.FlagScopeBranch, v.FlagScopeOrderCreate, v.FlagScopeCusType,
            v.FlagActive, live = v.IsLiveNow, v.Remark,
            details = v.Details.Select(d => new { d.Id, d.VoucherNo, d.Receiver, status = (int)d.Status, statusText = Ui.IssueVoucherStatusText(d.Status), d.IssueDate, d.ExpDate, d.UseDate, d.OrderNo }),
            scopes = v.Scopes.Select(s => new { s.Id, scopeType = (int)s.ScopeType, scopeTypeText = Ui.IssueScopeTypeText(s.ScopeType), s.Value }),
            products = v.Products.Select(p => new { p.Id, refType = (int)p.RefType, refTypeText = Ui.IssueRefTypeText(p.RefType), p.RefCode, p.RefName }),
            prices = v.Prices.Select(p => new { p.Id, issueType = (int)p.IssueType, issueTypeText = Ui.IssuePriceTypeText(p.IssueType), p.IssueTypeDtl, p.UPDc, p.UPRateDc, p.UPDcMax, p.Remark })
        });
    }

    [HttpPost("issue-vouchers")]
    public async Task<IActionResult> CreateIssueVoucher([FromBody] IssueVoucherReq r)
    {
        var (ok, msg, id) = await issueVouchers.CreateBatchAsync(new IssueVoucher
        {
            Code = r.Code ?? "", Name = r.Name,
            EffDateStart = r.EffDateStart == default ? DateTime.Today : r.EffDateStart,
            EffDateEnd = r.EffDateEnd == default ? DateTime.Today.AddMonths(1) : r.EffDateEnd,
            QtyVoucher = r.QtyVoucher, QtyDateUse = r.QtyDateUse,
            FavorType = (IssueFavorType)r.FavorType, IssueForm = (IssueFormType)r.IssueForm, Remark = r.Remark
        });
        return ok ? Ok(new { id }) : BadRequest(new { error = msg });
    }

    [HttpPost("issue-vouchers/{id:int}/scopes")]
    public async Task<IActionResult> AddIssueVoucherScope(int id, [FromBody] IssueVoucherScopeReq r)
    {
        var (ok, msg) = await issueVouchers.AddScopeAsync(new IssueVoucherScope { IssueVoucherId = id, ScopeType = (IssueScopeType)r.ScopeType, Value = r.Value ?? "" });
        return ok ? Ok(new { ok }) : BadRequest(new { error = msg });
    }

    [HttpPost("issue-vouchers/{id:int}/products")]
    public async Task<IActionResult> AddIssueVoucherProduct(int id, [FromBody] IssueVoucherProductReq r)
    {
        var (ok, msg) = await issueVouchers.AddProductAsync(new IssueVoucherProduct { IssueVoucherId = id, RefType = (IssueRefType)r.RefType, RefCode = r.RefCode ?? "", RefName = r.RefName });
        return ok ? Ok(new { ok }) : BadRequest(new { error = msg });
    }

    [HttpPost("issue-vouchers/{id:int}/prices")]
    public async Task<IActionResult> AddIssueVoucherPrice(int id, [FromBody] IssueVoucherPriceReq r)
    {
        var (ok, msg) = await issueVouchers.AddPriceAsync(new IssueVoucherPrice
        {
            IssueVoucherId = id, IssueType = (IssuePriceType)r.IssueType, IssueTypeDtl = r.IssueTypeDtl ?? "",
            UPDc = r.UPDc, UPRateDc = r.UPRateDc, UPDcMax = r.UPDcMax, Remark = r.Remark
        });
        return ok ? Ok(new { ok }) : BadRequest(new { error = msg });
    }

    [HttpPost("issue-vouchers/{id:int}/active")]
    public async Task<IActionResult> SetIssueVoucherActive(int id, [FromBody] ActiveReq r)
    {
        var (ok, msg) = await issueVouchers.SetActiveAsync(id, r.Active);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    // Phát hành một voucher trong đợt (port từ Mst_IssueVoucherDtl).
    [HttpPost("issue-vouchers/{id:int}/issue")]
    public async Task<IActionResult> IssueVoucherDtl(int id, [FromBody] IssueVoucherIssueReq r)
    {
        var o = await issueVouchers.IssueAsync(id, r.VoucherNo ?? "", r.Receiver, r.At);
        return o.ok ? Ok(new { ok = o.ok, msg = o.msg, voucherId = o.voucherId, voucherNo = o.voucherNo, expDate = o.expDate })
                    : BadRequest(new { ok = o.ok, error = o.msg });
    }

    // Thu hồi voucher đã phát.
    [HttpPost("issue-vouchers/{id:int}/vouchers/{voucherId:int}/evict")]
    public async Task<IActionResult> EvictIssueVoucher(int id, int voucherId)
    {
        var (ok, msg) = await issueVouchers.EvictAsync(voucherId);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    // Huỷ voucher đã phát.
    [HttpPost("issue-vouchers/{id:int}/vouchers/{voucherId:int}/cancel")]
    public async Task<IActionResult> CancelIssueVoucher(int id, int voucherId)
    {
        var (ok, msg) = await issueVouchers.CancelVoucherAsync(voucherId);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    // Đối soát đợt phát hành theo trạng thái voucher.
    [HttpGet("issue-vouchers/reconciliation")]
    public async Task<IActionResult> IssueVoucherReconciliation([FromQuery] int? batchId)
        => Ok((await issueVouchers.ReconciliationAsync(batchId)).Select(r => new
        {
            r.IssueVoucherId, r.IssueCode, r.IssueName, r.QtyVoucher, r.Issued, r.Used, r.Pending, r.Evicted, r.Cancelled
        }));

    // Kiểm tra một voucher của đợt phát hành có được dùng hay không (công khai).
    [HttpPost("issue-voucher/check")]
    public async Task<IActionResult> CheckIssueVoucher([FromBody] IssueUseReq r)
    {
        var o = await issueVouchers.CheckUseAsync(r.VoucherNo ?? "", r.At);
        return o.ok ? Ok(new { ok = o.ok, msg = o.msg, voucherNo = o.voucherNo, favorType = o.favorType })
                    : BadRequest(new { ok = o.ok, error = o.msg });
    }

    // Ghi nhận sử dụng voucher của đợt phát hành (công khai).
    [HttpPost("issue-voucher/use")]
    public async Task<IActionResult> UseIssueVoucher([FromBody] IssueUseReq r)
    {
        var o = await issueVouchers.UseAsync(r.VoucherNo ?? "", r.OrderNo, r.At);
        return o.ok ? Ok(new { ok = o.ok, msg = o.msg, voucherNo = o.voucherNo, favorType = o.favorType })
                    : BadRequest(new { ok = o.ok, error = o.msg });
    }

    // ---- Tham số khuyến mại (port từ Mst_ParamPromotion + Mst_ParamPromotionType) ----
    [HttpGet("param-promotion-types")]
    public async Task<IActionResult> ParamPromotionTypes()
        => Ok((await paramPromotions.TypesAsync()).Select(t => new
        {
            t.Id, t.Code, t.Name, t.FlagActive, activeText = t.FlagActive ? "Đang bật" : "Tạm dừng", t.Remark, paramCount = t.Params.Count
        }));

    [HttpPost("param-promotion-types")]
    public async Task<IActionResult> CreateParamPromotionType([FromBody] ParamPromotionTypeReq r)
    {
        var (ok, msg, id) = await paramPromotions.CreateTypeAsync(new ParamPromotionType { Code = r.Code ?? "", Name = r.Name, Remark = r.Remark });
        return ok ? Ok(new { id }) : BadRequest(new { error = msg });
    }

    [HttpPost("param-promotion-types/{id:int}/active")]
    public async Task<IActionResult> SetParamPromotionTypeActive(int id, [FromBody] ActiveReq r)
    {
        var (ok, msg) = await paramPromotions.SetTypeActiveAsync(id, r.Active);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    [HttpGet("param-promotions")]
    public async Task<IActionResult> ParamPromotions()
        => Ok((await paramPromotions.ParamsAsync()).Select(p => new
        {
            p.Id, p.ProgramCode, p.ProgramName, p.ParamPromotionTypeId,
            typeCode = p.ParamPromotionType?.Code, typeName = p.ParamPromotionType?.Name,
            p.QtyDateBefore, p.QtyDateAfter, p.FlagActive, activeText = p.FlagActive ? "Đang bật" : "Tạm dừng", p.Remark
        }));

    [HttpGet("param-promotions/{id:int}")]
    public async Task<IActionResult> ParamPromotion(int id)
    {
        var p = await paramPromotions.GetParamAsync(id);
        if (p == null) return NotFound(new { error = "Không tìm thấy tham số khuyến mại." });
        return Ok(new
        {
            p.Id, p.ProgramCode, p.ProgramName, p.ParamPromotionTypeId,
            typeCode = p.ParamPromotionType?.Code, typeName = p.ParamPromotionType?.Name,
            p.QtyDateBefore, p.QtyDateAfter, p.FlagActive, p.Remark
        });
    }

    [HttpPost("param-promotions")]
    public async Task<IActionResult> CreateParamPromotion([FromBody] ParamPromotionReq r)
    {
        var (ok, msg, id) = await paramPromotions.CreateParamAsync(new ParamPromotion
        {
            ProgramCode = r.ProgramCode ?? "", ProgramName = r.ProgramName ?? "",
            ParamPromotionTypeId = r.ParamPromotionTypeId, QtyDateBefore = r.QtyDateBefore, QtyDateAfter = r.QtyDateAfter, Remark = r.Remark
        });
        return ok ? Ok(new { id }) : BadRequest(new { error = msg });
    }

    [HttpPost("param-promotions/{id:int}/active")]
    public async Task<IActionResult> SetParamPromotionActive(int id, [FromBody] ActiveReq r)
    {
        var (ok, msg) = await paramPromotions.SetParamActiveAsync(id, r.Active);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    // Kiểm tra một mốc ngày có nằm trong khoảng áp dụng của tham số khuyến mại (công khai).
    [HttpPost("param-promotion/check")]
    public async Task<IActionResult> CheckParamPromotion([FromBody] ParamWindowReq r)
    {
        var o = await paramPromotions.CheckWindowAsync(r.ProgramCode ?? "", r.TypeCode ?? "", r.Anchor ?? DateTime.Today);
        return o.ok ? Ok(new { ok = o.ok, msg = o.msg, programCode = o.programCode, start = o.start, end = o.end })
                    : BadRequest(new { ok = o.ok, error = o.msg });
    }

    // ---- Chính sách xếp hạng thẻ (port từ Mst_RankPolicy) ----
    [HttpGet("rank-policies")]
    public async Task<IActionResult> RankPolicies()
        => Ok((await rankPolicies.PoliciesAsync()).Select(p => new
        {
            p.Id, p.Code, p.CardType, p.Value, p.PointUpBegin, p.PointUpEnd, p.QtyVisitUpBegin, p.QtyVisitUpEnd,
            p.PointKeepBegin, p.PointKeepEnd, p.QtyVisitKeepBegin, p.QtyVisitKeepEnd, p.QtyMonth,
            status = (int)p.Status, statusText = Ui.RankPolicy(p.Status).text, statusCss = Ui.RankPolicy(p.Status).css
        }));

    [HttpGet("rank-policies/{id:int}")]
    public async Task<IActionResult> RankPolicy(int id)
    {
        var p = await rankPolicies.GetPolicyAsync(id);
        if (p == null) return NotFound(new { error = "Không tìm thấy chính sách." });
        return Ok(new
        {
            p.Id, p.Code, p.CardType, p.Value, p.PointUpBegin, p.PointUpEnd, p.QtyVisitUpBegin, p.QtyVisitUpEnd,
            p.PointKeepBegin, p.PointKeepEnd, p.QtyVisitKeepBegin, p.QtyVisitKeepEnd, p.QtyMonth, p.Remark,
            status = (int)p.Status, statusText = Ui.RankPolicy(p.Status).text
        });
    }

    [HttpPost("rank-policies")]
    public async Task<IActionResult> CreateRankPolicy([FromBody] RankPolicyReq r)
    {
        var (ok, msg, id) = await rankPolicies.CreatePolicyAsync(new RankPolicy
        {
            Code = r.Code ?? "", CardType = r.CardType ?? "", Value = r.Value,
            PointUpBegin = r.PointUpBegin, PointUpEnd = r.PointUpEnd, QtyVisitUpBegin = r.QtyVisitUpBegin, QtyVisitUpEnd = r.QtyVisitUpEnd,
            PointKeepBegin = r.PointKeepBegin, PointKeepEnd = r.PointKeepEnd, QtyVisitKeepBegin = r.QtyVisitKeepBegin, QtyVisitKeepEnd = r.QtyVisitKeepEnd,
            QtyMonth = r.QtyMonth, Remark = r.Remark
        });
        return ok ? Ok(new { id }) : BadRequest(new { error = msg });
    }

    [HttpPost("rank-policies/{id:int}/status")]
    public async Task<IActionResult> SetRankPolicyStatus(int id, [FromBody] StatusReq r)
    {
        var (ok, msg) = await rankPolicies.SetStatusAsync(id, (RankPolicyStatus)r.Status);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    // Đánh giá xếp hạng thẻ theo chính sách đang bật (công khai).
    [HttpPost("rank-policy/evaluate")]
    public async Task<IActionResult> EvaluateRankPolicy([FromBody] RankEvalReq r)
    {
        var o = await rankPolicies.EvaluateAsync(r.CardType ?? "", r.Point, r.QtyVisit);
        return o.ok ? Ok(new { ok = o.ok, msg = o.msg, action = (int)o.action, actionText = Ui.RankActionText(o.action), cardType = o.cardType, value = o.value })
                    : BadRequest(new { ok = o.ok, error = o.msg });
    }

    // ---- Chính sách quy đổi tiền dịch vụ → điểm (port từ Mst_PolicyMoneyToPointService) ----
    [HttpGet("policy-money-to-points")]
    public async Task<IActionResult> PolicyMoneyToPoints()
        => Ok((await moneyToPoints.PoliciesAsync()).Select(p => new
        {
            p.Id, p.Code, p.Name, p.EffDateStart, p.EffDateEnd,
            status = (int)p.Status, statusText = Ui.PolicyMoneyToPoint(p.Status).text, statusCss = Ui.PolicyMoneyToPoint(p.Status).css,
            live = p.IsLiveNow, details = p.Details.Count
        }));

    [HttpGet("policy-money-to-points/{id:int}")]
    public async Task<IActionResult> PolicyMoneyToPoint(int id)
    {
        var p = await moneyToPoints.GetPolicyAsync(id);
        if (p == null) return NotFound(new { error = "Không tìm thấy chính sách." });
        return Ok(new
        {
            p.Id, p.Code, p.Name, p.EffDateStart, p.EffDateEnd, p.Remark,
            status = (int)p.Status, statusText = Ui.PolicyMoneyToPoint(p.Status).text, live = p.IsLiveNow,
            details = p.Details.Select(d => new { d.Id, d.CardType, d.ConvertValue, d.ConvertPoint, d.ValueRankCardType, d.DiscountRate, d.FlagActive, d.Remark })
        });
    }

    [HttpPost("policy-money-to-points")]
    public async Task<IActionResult> CreatePolicyMoneyToPoint([FromBody] PolicyMoneyToPointReq r)
    {
        var (ok, msg, id) = await moneyToPoints.CreatePolicyAsync(new PolicyMoneyToPoint
        {
            Code = r.Code ?? "", Name = r.Name,
            EffDateStart = r.EffDateStart == default ? DateTime.Today : r.EffDateStart,
            EffDateEnd = r.EffDateEnd == default ? DateTime.Today.AddMonths(1) : r.EffDateEnd,
            Remark = r.Remark
        });
        return ok ? Ok(new { id }) : BadRequest(new { error = msg });
    }

    [HttpPost("policy-money-to-points/{id:int}/details")]
    public async Task<IActionResult> AddPolicyMoneyToPointDetail(int id, [FromBody] PolicyMoneyToPointDtlReq r)
    {
        var (ok, msg) = await moneyToPoints.AddDetailAsync(new PolicyMoneyToPointDtl
        {
            PolicyMoneyToPointId = id, CardType = r.CardType ?? "", ConvertValue = r.ConvertValue,
            ConvertPoint = r.ConvertPoint, ValueRankCardType = r.ValueRankCardType, DiscountRate = r.DiscountRate, Remark = r.Remark
        });
        return ok ? Ok(new { ok }) : BadRequest(new { error = msg });
    }

    [HttpPost("policy-money-to-points/{id:int}/status")]
    public async Task<IActionResult> SetPolicyMoneyToPointStatus(int id, [FromBody] StatusReq r)
    {
        var (ok, msg) = await moneyToPoints.SetStatusAsync(id, (PolicyMoneyToPointStatus)r.Status);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    // Quy đổi tiền dịch vụ → điểm cho một hạng thẻ theo chính sách đang hiệu lực (công khai).
    [HttpPost("policy-money-to-point/calc")]
    public async Task<IActionResult> CalcPolicyMoneyToPoint([FromBody] MoneyToPointCalcReq r)
    {
        var o = await moneyToPoints.CalcAsync(r.CardType ?? "", r.Amount, r.At);
        return o.ok ? Ok(new { ok = o.ok, msg = o.msg, policyCode = o.policyCode, cardType = o.cardType, amount = o.amount, point = o.point, discountRate = o.discountRate, qtyVisit = o.qtyVisit, valueRankCardType = o.valueRankCardType })
                    : BadRequest(new { ok = o.ok, error = o.msg });
    }

    // ---- Chiết khấu hội viên (port từ Crd_MemberDiscountTransaction) ----
    [HttpGet("member-discounts")]
    public async Task<IActionResult> MemberDiscounts([FromQuery] string? refNo)
        => Ok((await memberDiscounts.TransactionsAsync(refNo)).Select(t => new
        {
            t.Id, t.RefNo, t.DealerCode, t.MemberNo, t.CardNo, t.CardTypeUse, t.CardTypeInit, t.CardTypeApply,
            dealPointType = (int)t.DealPointType, t.PolicyCode, t.PolicyDiscountRate, t.AmountForDC, t.PointChTotal, t.CreateDate
        }));

    [HttpGet("member-discounts/{id:int}")]
    public async Task<IActionResult> MemberDiscount(int id)
    {
        var t = await memberDiscounts.GetTransactionAsync(id);
        if (t == null) return NotFound(new { error = "Không tìm thấy giao dịch chiết khấu." });
        return Ok(new
        {
            t.Id, t.RefNo, t.DealerCode, t.MemberNo, t.CardNo, t.CardTypeUse, t.CardTypeInit, t.CardTypeApply,
            dealPointType = (int)t.DealPointType, t.PolicyCode, t.PolicyDiscountRate, t.AmountForDC, t.PointChTotal, t.CreateDate, t.Remark
        });
    }

    // Đối soát chiết khấu theo hạng thẻ áp dụng.
    [HttpGet("member-discounts/reconciliation")]
    public async Task<IActionResult> MemberDiscountReconciliation([FromQuery] string? cardTypeApply)
        => Ok((await memberDiscounts.ReconciliationAsync(cardTypeApply)).Select(r => new
        {
            r.CardTypeApply, r.Deals, r.AmountForDC, r.Discount
        }));

    // Tính chiết khấu hội viên cho một giao dịch theo hạng thẻ áp dụng (công khai).
    [HttpPost("member-discount/calc")]
    public async Task<IActionResult> CalcMemberDiscount([FromBody] MemberDiscountCalcReq r)
    {
        var lines = r.Lines?.Select(l => new MemberDiscountLine(l.AmountForDC, l.PaymentDiscountRate, l.FlagDiscount));
        var o = await memberDiscounts.CalcAsync(r.RefNo ?? "", r.CardTypeApply ?? "", lines ?? Enumerable.Empty<MemberDiscountLine>(), r.At);
        return o.ok ? Ok(new { ok = o.ok, msg = o.msg, refNo = o.refNo, cardTypeApply = o.cardTypeApply, amountForDC = o.amountForDC, discount = o.discount, policyDiscountRate = o.policyDiscountRate })
                    : BadRequest(new { ok = o.ok, error = o.msg });
    }

    // Ghi nhận giao dịch chiết khấu hội viên (công khai).
    [HttpPost("member-discount/record")]
    public async Task<IActionResult> RecordMemberDiscount([FromBody] MemberDiscountRecordReq r)
    {
        var lines = r.Lines?.Select(l => new MemberDiscountLine(l.AmountForDC, l.PaymentDiscountRate, l.FlagDiscount));
        var o = await memberDiscounts.RecordAsync(r.RefNo ?? "", r.DealerCode ?? "", r.MemberNo ?? "", r.CardNo ?? "",
            r.CardTypeUse ?? "", r.CardTypeInit ?? "", r.CardTypeApply ?? "", lines ?? Enumerable.Empty<MemberDiscountLine>(), r.At);
        return o.ok ? Ok(new { ok = o.ok, msg = o.msg, id = o.id, amountForDC = o.amountForDC, discount = o.discount })
                    : BadRequest(new { ok = o.ok, error = o.msg });
    }

    // ---- Danh mục loại khuyến mại (port từ Mst_PromotionMainType + Mst_PromotionPrmType + Prm_PrmInMain) ----
    [HttpGet("promotion-main-types")]
    public async Task<IActionResult> PromotionMainTypes()
        => Ok((await promotionTypes.MainTypesAsync()).Select(t => new
        {
            t.Id, t.Code, t.Name, t.FlagActive, activeText = t.FlagActive ? "Đang bật" : "Tạm dừng", t.Remark, prmCount = t.PrmInMains.Count
        }));

    [HttpPost("promotion-main-types")]
    public async Task<IActionResult> CreatePromotionMainType([FromBody] PromotionTypeReq r)
    {
        var (ok, msg, id) = await promotionTypes.CreateMainTypeAsync(new PromotionMainTypeDef { Code = r.Code ?? "", Name = r.Name, Remark = r.Remark });
        return ok ? Ok(new { id }) : BadRequest(new { error = msg });
    }

    [HttpPost("promotion-main-types/{id:int}/active")]
    public async Task<IActionResult> SetPromotionMainTypeActive(int id, [FromBody] ActiveReq r)
    {
        var (ok, msg) = await promotionTypes.SetMainTypeActiveAsync(id, r.Active);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    [HttpGet("promotion-prm-types")]
    public async Task<IActionResult> PromotionPrmTypes()
        => Ok((await promotionTypes.PrmTypesAsync()).Select(t => new
        {
            t.Id, t.Code, t.Name, t.FlagActive, activeText = t.FlagActive ? "Đang bật" : "Tạm dừng", t.Remark, mainCount = t.PrmInMains.Count
        }));

    [HttpPost("promotion-prm-types")]
    public async Task<IActionResult> CreatePromotionPrmType([FromBody] PromotionTypeReq r)
    {
        var (ok, msg, id) = await promotionTypes.CreatePrmTypeAsync(new PromotionPrmTypeDef { Code = r.Code ?? "", Name = r.Name, Remark = r.Remark });
        return ok ? Ok(new { id }) : BadRequest(new { error = msg });
    }

    [HttpPost("promotion-prm-types/{id:int}/active")]
    public async Task<IActionResult> SetPromotionPrmTypeActive(int id, [FromBody] ActiveReq r)
    {
        var (ok, msg) = await promotionTypes.SetPrmTypeActiveAsync(id, r.Active);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    [HttpGet("promotion-prm-in-mains")]
    public async Task<IActionResult> PromotionPrmInMains()
        => Ok((await promotionTypes.MappingsAsync()).Select(m => new
        {
            m.Id, m.MainTypeId, mainTypeCode = m.MainType?.Code, mainTypeName = m.MainType?.Name,
            m.PrmTypeId, prmTypeCode = m.PrmType?.Code, prmTypeName = m.PrmType?.Name,
            m.FlagActive, activeText = m.FlagActive ? "Đang bật" : "Tạm dừng", m.Remark
        }));

    [HttpPost("promotion-prm-in-mains")]
    public async Task<IActionResult> AddPromotionPrmInMain([FromBody] PromotionPrmInMainReq r)
    {
        var (ok, msg, id) = await promotionTypes.AddMappingAsync(r.MainTypeId, r.PrmTypeId, r.Remark);
        return ok ? Ok(new { id }) : BadRequest(new { error = msg });
    }

    [HttpPost("promotion-prm-in-mains/{id:int}/active")]
    public async Task<IActionResult> SetPromotionPrmInMainActive(int id, [FromBody] ActiveReq r)
    {
        var (ok, msg) = await promotionTypes.SetMappingActiveAsync(id, r.Active);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    // Kiểm tra một hình thức khuyến mại có được phép dùng cho một loại khuyến mại theo hay không (công khai).
    [HttpPost("promotion-type/check")]
    public async Task<IActionResult> CheckPromotionType([FromBody] PrmInMainReq r)
    {
        var o = await promotionTypes.CheckPrmInMainAsync(r.MainTypeCode ?? "", r.PrmTypeCode ?? "");
        return o.ok ? Ok(new { ok = o.ok, msg = o.msg, mainTypeCode = o.mainTypeCode, prmTypeCode = o.prmTypeCode })
                    : BadRequest(new { ok = o.ok, error = o.msg });
    }

    // ---- Mã giảm giá + ánh xạ đại lý (port từ Inos_DiscountCode + Map_DealerDiscount) ----
    [HttpGet("discount-codes")]
    public async Task<IActionResult> DiscountCodes()
        => Ok((await discountCodes.CodesAsync()).Select(c => new
        {
            c.Id, c.Code, c.Description,
            discountType = (int)c.DiscountType, discountTypeText = Ui.DiscountCodeTypeText(c.DiscountType),
            c.DiscountAmount, c.RemainQty, c.Enabled, enabledText = c.Enabled ? "Đang bật" : "Đang tắt",
            c.EffectDateFrom, c.EffectDateTo, live = c.IsLiveNow
        }));

    [HttpGet("discount-codes/{id:int}")]
    public async Task<IActionResult> DiscountCode(int id)
    {
        var c = await discountCodes.GetCodeAsync(id);
        if (c == null) return NotFound(new { error = "Không tìm thấy mã giảm giá." });
        return Ok(new
        {
            c.Id, c.Code, c.Description,
            discountType = (int)c.DiscountType, discountTypeText = Ui.DiscountCodeTypeText(c.DiscountType),
            c.DiscountAmount, c.RemainQty, c.Enabled, c.EffectDateFrom, c.EffectDateTo, live = c.IsLiveNow
        });
    }

    [HttpPost("discount-codes")]
    public async Task<IActionResult> CreateDiscountCode([FromBody] DiscountCodeReq r)
    {
        var (ok, msg, id) = await discountCodes.CreateCodeAsync(new DiscountCode
        {
            Code = r.Code ?? "", Description = r.Description, DiscountType = (DiscountCodeType)r.DiscountType,
            DiscountAmount = r.DiscountAmount, RemainQty = r.RemainQty,
            EffectDateFrom = r.EffectDateFrom == default ? DateTime.Today : r.EffectDateFrom,
            EffectDateTo = r.EffectDateTo == default ? DateTime.Today.AddMonths(1) : r.EffectDateTo
        });
        return ok ? Ok(new { id }) : BadRequest(new { error = msg });
    }

    [HttpPost("discount-codes/{id:int}/enabled")]
    public async Task<IActionResult> SetDiscountCodeEnabled(int id, [FromBody] ActiveReq r)
    {
        var (ok, msg) = await discountCodes.SetEnabledAsync(id, r.Active);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    [HttpGet("dealer-discount-maps")]
    public async Task<IActionResult> DealerDiscountMaps([FromQuery] string? dealerCode)
        => Ok((await discountCodes.MapsAsync(dealerCode)).Select(m => new
        {
            m.Id, m.DealerCode, m.DiscountCode, m.FlagActive, activeText = m.FlagActive ? "Đang bật" : "Tạm dừng", m.Remark
        }));

    [HttpPost("dealer-discount-maps")]
    public async Task<IActionResult> AddDealerDiscountMap([FromBody] DealerDiscountMapReq r)
    {
        var (ok, msg, id) = await discountCodes.AddMapAsync(new DealerDiscountMap { DealerCode = r.DealerCode ?? "", DiscountCode = r.DiscountCode ?? "", Remark = r.Remark });
        return ok ? Ok(new { id }) : BadRequest(new { error = msg });
    }

    [HttpPost("dealer-discount-maps/{id:int}/active")]
    public async Task<IActionResult> SetDealerDiscountMapActive(int id, [FromBody] ActiveReq r)
    {
        var (ok, msg) = await discountCodes.SetMapActiveAsync(id, r.Active);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    // Kiểm tra một mã giảm giá có hợp lệ cho một đơn hàng (công khai).
    [HttpPost("discount-code/check")]
    public async Task<IActionResult> CheckDiscountCode([FromBody] DiscountCheckReq r)
    {
        var o = await discountCodes.CheckAsync(r.Code ?? "", r.OrderAmount, r.At);
        return o.ok ? Ok(new { ok = o.ok, msg = o.msg, code = o.code, discountAmount = o.discountAmount, discountType = (int)o.discountType })
                    : BadRequest(new { ok = o.ok, error = o.msg });
    }

    // Áp dụng mã giảm giá cho một đơn hàng (công khai).
    [HttpPost("discount-code/apply")]
    public async Task<IActionResult> ApplyDiscountCode([FromBody] DiscountApplyReq r)
    {
        var o = await discountCodes.ApplyAsync(r.Code ?? "", r.OrderAmount, r.At);
        return o.ok ? Ok(new { ok = o.ok, msg = o.msg, code = o.code, discount = o.discount, orderAmount = o.orderAmount, payable = o.payable, remainQty = o.remainQty })
                    : BadRequest(new { ok = o.ok, error = o.msg });
    }

    // ---- Sinh mã voucher theo hệ cơ số 36 + checksum (port từ Seq_VoucherID + Mst_VoucherID) ----
    [HttpGet("voucher-id-sequences")]
    public async Task<IActionResult> VoucherIdSequences()
        => Ok((await voucherIds.SequencesAsync()).Select(s => new
        {
            s.Id, s.Seq, s.VoucherNo, s.VerGen, s.GeneratedAt, s.Remark
        }));

    // Sinh một hoặc nhiều mã voucher mới (công khai).
    [HttpPost("voucher-id/generate")]
    public async Task<IActionResult> GenerateVoucherId([FromBody] VoucherIdGenReq r)
    {
        var o = await voucherIds.GenerateAsync(r.Amount, r.At);
        return o.ok ? Ok(new { ok = o.ok, msg = o.msg, codes = o.codes, lastSeq = o.lastSeq })
                    : BadRequest(new { ok = o.ok, error = o.msg });
    }

    // Kiểm tra định dạng + checksum của một mã voucher (công khai).
    [HttpPost("voucher-id/validate")]
    public IActionResult ValidateVoucherId([FromBody] VoucherIdValidateReq r)
    {
        var o = voucherIds.Validate(r.VoucherNo ?? "");
        return o.ok ? Ok(new { ok = o.ok, msg = o.msg }) : BadRequest(new { ok = o.ok, error = o.msg });
    }

    // ---- Tặng điểm giới thiệu (port từ Crd_Member_PerformIntroX) ----
    [HttpGet("introduction-grants")]
    public async Task<IActionResult> IntroductionGrants([FromQuery] string? memberNo)
        => Ok((await introductionGrants.GrantsAsync(memberNo)).Select(g => new
        {
            g.Id, g.RefNo, g.MemberNo, g.NewMemberNo, g.CardNo, g.CardTypeUse, g.CardTypeInit, g.DealerCode,
            dealPointType = (int)g.DealPointType, g.PointChTotal, g.AmountChTotal, g.ParamValue, g.PointExpiryDTime, g.CreateDate, g.Remark
        }));

    // Đối soát điểm giới thiệu đã tặng theo hội viên được thưởng.
    [HttpGet("introduction-grants/reconciliation")]
    public async Task<IActionResult> IntroductionReconciliation([FromQuery] string? memberNo)
        => Ok((await introductionGrants.ReconciliationAsync(memberNo)).Select(r => new
        {
            r.MemberNo, r.Granted, r.PointGranted, r.AmountGranted
        }));

    // Tặng điểm giới thiệu cho người giới thiệu của một hội viên mới (công khai).
    [HttpPost("introduction/grant")]
    public async Task<IActionResult> GrantIntroduction([FromBody] IntroductionGrantReq r)
    {
        var o = await introductionGrants.GrantAsync(r.NewMemberNo ?? "", r.ReferrerMemberNo ?? "", r.CardNo ?? "",
            r.CardTypeUse ?? "", r.CardTypeInit ?? "", r.DealerCode ?? "", r.PointIntro, r.ParamValue, r.At);
        return o.ok ? Ok(new { ok = o.ok, msg = o.msg, memberNo = o.memberNo, newMemberNo = o.newMemberNo, point = o.point, amount = o.amount, pointExpiryDTime = o.pointExpiryDTime })
                    : BadRequest(new { ok = o.ok, error = o.msg });
    }

    // ---- Tặng điểm mua xe mới (port từ Crd_Member_PerformBuyNewCarX) ----
    [HttpGet("car-purchase-points")]
    public async Task<IActionResult> CarPurchasePoints([FromQuery] string? memberNo)
        => Ok((await carPurchasePoints.GrantsAsync(memberNo)).Select(g => new
        {
            g.Id, g.RefNo, g.MemberNo, g.CardNo, g.CardTypeUse, g.CardTypeInit, g.DealerCode, g.PrProgramCode,
            dealPointType = (int)g.DealPointType, g.PointChTotal, g.AmountChTotal, g.ParamValue, g.PointExpiryDTime, g.CreateDate, g.Remark
        }));

    // Đối soát điểm mua xe mới đã tặng theo hội viên.
    [HttpGet("car-purchase-points/reconciliation")]
    public async Task<IActionResult> CarPurchasePointReconciliation([FromQuery] string? memberNo)
        => Ok((await carPurchasePoints.ReconciliationAsync(memberNo)).Select(r => new
        {
            r.MemberNo, r.Granted, r.PointGranted, r.AmountGranted
        }));

    // Tặng điểm mua xe mới cho một hội viên (công khai).
    [HttpPost("car-purchase-point/grant")]
    public async Task<IActionResult> GrantCarPurchasePoint([FromBody] CarPurchasePointReq r)
    {
        var o = await carPurchasePoints.GrantAsync(r.MemberNo ?? "", r.CardNo ?? "", r.CardTypeUse ?? "", r.CardTypeInit ?? "",
            r.DealerCode ?? "", r.PrProgramCode, r.PointBuyCar, r.ParamValue, r.At);
        return o.ok ? Ok(new { ok = o.ok, msg = o.msg, memberNo = o.memberNo, cardNo = o.cardNo, point = o.point, amount = o.amount, pointExpiryDTime = o.pointExpiryDTime })
                    : BadRequest(new { ok = o.ok, error = o.msg });
    }

    // ---- Chính sách đối tượng tích điểm dịch vụ (port từ Mst_PolicyExpenseType + Mst_ExpenseType) ----
    [HttpGet("expense-types")]
    public async Task<IActionResult> ExpenseTypes()
        => Ok((await policyExpenseTypes.ExpenseTypesAsync()).Select(t => new
        {
            t.Id, t.Code, t.Name, t.FlagActive, activeText = t.FlagActive ? "Đang bật" : "Tạm dừng", t.Remark
        }));

    [HttpPost("expense-types")]
    public async Task<IActionResult> CreateExpenseType([FromBody] ExpenseTypeReq r)
    {
        var (ok, msg, id) = await policyExpenseTypes.CreateExpenseTypeAsync(new ExpenseType { Code = r.Code ?? "", Name = r.Name, Remark = r.Remark });
        return ok ? Ok(new { id }) : BadRequest(new { error = msg });
    }

    [HttpPost("expense-types/{id:int}/active")]
    public async Task<IActionResult> SetExpenseTypeActive(int id, [FromBody] ActiveReq r)
    {
        var (ok, msg) = await policyExpenseTypes.SetExpenseTypeActiveAsync(id, r.Active);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    [HttpGet("policy-expense-types")]
    public async Task<IActionResult> PolicyExpenseTypes([FromQuery] string? policyNo)
        => Ok((await policyExpenseTypes.PoliciesAsync(policyNo)).Select(p => new
        {
            p.Id, p.PolicyExpenseTypeNo, p.ExpenseType, p.ExpenseTypeNameActual,
            p.FlagPoint, p.FlagPointRank, p.AmountRate, p.MaxRankReviewPoint, p.MaxAccumulationPoint,
            p.FlagCountService, p.FlagDiscount, p.DiscountRate, p.FlagActive, p.Remark
        }));

    // Lưu toàn bộ dòng của một chính sách theo cơ chế "xoá sạch rồi ghi lại" (Mst_PolicyExpenseType_SaveX).
    [HttpPost("policy-expense-types/save")]
    public async Task<IActionResult> SavePolicyExpenseTypes([FromBody] PolicyExpenseTypeSaveReq r)
    {
        var rows = (r.Rows ?? new()).Select(x => new PolicyExpenseType
        {
            ExpenseType = x.ExpenseType ?? "", ExpenseTypeNameActual = x.ExpenseTypeNameActual ?? "",
            FlagPoint = x.FlagPoint, FlagPointRank = x.FlagPointRank, AmountRate = x.AmountRate,
            MaxRankReviewPoint = x.MaxRankReviewPoint, MaxAccumulationPoint = x.MaxAccumulationPoint,
            FlagCountService = x.FlagCountService, FlagDiscount = x.FlagDiscount, DiscountRate = x.DiscountRate, Remark = x.Remark
        }).ToList();
        var (ok, msg, count) = await policyExpenseTypes.SavePolicyAsync(r.PolicyExpenseTypeNo ?? "", rows);
        return ok ? Ok(new { ok, msg, count }) : BadRequest(new { ok, error = msg });
    }

    // Tra cứu quy tắc tích điểm dịch vụ cho một loại chi phí (công khai).
    [HttpPost("policy-expense-type/calc")]
    public async Task<IActionResult> CalcPolicyExpenseType([FromBody] ExpensePointCalcReq r)
    {
        var o = await policyExpenseTypes.CalcAsync(r.ExpenseType ?? "", r.Amount);
        return o.ok ? Ok(new { ok = o.ok, msg = o.msg, expenseType = o.expenseType, expenseTypeName = o.expenseTypeName, amount = o.amount, point = o.point, discountRate = o.discountRate, countService = o.countService, pointRank = o.pointRank })
                    : BadRequest(new { ok = o.ok, error = o.msg });
    }
}

public record DashDto(int Campaigns, int Running, int TotalPlays, int TotalWins, decimal ValueAwarded, List<TopDto> Top);
public record TopDto(string Campaign, int Plays, int Wins, decimal ValueAwarded);

public class CampaignReq { public string Name { get; set; } = ""; public string? Code { get; set; } public string? Description { get; set; } public DateTime FromDate { get; set; } public DateTime ToDate { get; set; } public int LoseWeight { get; set; } }
public class StatusReq { public int Status { get; set; } }
public class PrizeReq { public string Name { get; set; } = ""; public string? Tier { get; set; } public decimal Value { get; set; } public int Quantity { get; set; } public int Weight { get; set; } }
public class ClaimReq { public int Status { get; set; } }
public class PlayReq { public string? CampaignCode { get; set; } public string? Code { get; set; } public string? Name { get; set; } public string? Phone { get; set; } }
public class VoucherReq { public string? Code { get; set; } public string Name { get; set; } = ""; public string? MemberNo { get; set; } public decimal PointTotal { get; set; } public decimal PointLimit { get; set; } public int QtyUseLimit { get; set; } public DateTime ExpireDate { get; set; } }
public class ActiveReq { public bool Active { get; set; } }
public class RedeemReq { public string? Code { get; set; } public decimal? Amount { get; set; } public string? MemberNo { get; set; } }
public class VoucherProgramReq { public string? Code { get; set; } public string Name { get; set; } = ""; public DateTime EffDateStart { get; set; } public DateTime EffDateEnd { get; set; } public int ValidityPeriod { get; set; } public int QtyDayLimitFDlvDate { get; set; } public bool FlagAllModel { get; set; } = true; public decimal PointVoucherAllModel { get; set; } public decimal PointUseLimitAllModel { get; set; } public string? Remark { get; set; } }
public class VoucherProgramDtlReq { public string? ModelCode { get; set; } public decimal PointVoucher { get; set; } public decimal PointUseLimit { get; set; } public string? Remark { get; set; } }
public class VoucherCalcReq { public string? ModelCode { get; set; } public DateTime? DeliveryDate { get; set; } public DateTime? RegistrationDate { get; set; } }
public class VoucherIssueReq { public string? ModelCode { get; set; } public DateTime? DeliveryDate { get; set; } public DateTime? RegistrationDate { get; set; } public string? MemberNo { get; set; } }
public class RemarkReq { public string? Remark { get; set; } }
public class CarPromotionReq { public string? Code { get; set; } public string Name { get; set; } = ""; public string? DealerCode { get; set; } public DateTime EffDateStart { get; set; } public DateTime EffDateEnd { get; set; } public bool FlagAllModel { get; set; } = true; public decimal PointValAllModel { get; set; } public string? Remark { get; set; } }
public class CarPromotionDtlReq { public string? ModelCode { get; set; } public decimal PointVal { get; set; } public string? Remark { get; set; } }
public class CarPromoCalcReq { public string? DealerCode { get; set; } public string? ModelCode { get; set; } }
public class PromotionProgramReq { public string? Code { get; set; } public string Name { get; set; } = ""; public int MainType { get; set; } public int PrmType { get; set; } public decimal BudgetVal { get; set; } public DateTime EffDTimeStart { get; set; } public DateTime EffDTimeEnd { get; set; } public bool FlagParallel { get; set; } public bool FlagMulti { get; set; } public string? Remark { get; set; } }
public class PromotionScopeReq { public int ScopeType { get; set; } public string? Value { get; set; } public string? ValueEnd { get; set; } }
public class PromotionPrmReq { public int Idx { get; set; } public int Qty { get; set; } public decimal UPDc { get; set; } public decimal UPRateDc { get; set; } public decimal UPDcMax { get; set; } public decimal ValOrdDc { get; set; } public decimal ValOrdRateDc { get; set; } public decimal ValOrdDcMax { get; set; } public string? Remark { get; set; } }
public class PromotionMainReq { public int Idx { get; set; } public int Qty { get; set; } public decimal Amount { get; set; } public decimal TotalValOrd { get; set; } }
public class PromotionProductScopeReq { public int Kind { get; set; } public int Idx { get; set; } public int RefType { get; set; } public string? RefCode { get; set; } public string? RefName { get; set; } public int? MapIdx { get; set; } public bool FlagActive { get; set; } = true; public string? Remark { get; set; } }
public class PromotionCalcReq { public decimal OrderAmount { get; set; } public int Qty { get; set; } public DateTime? At { get; set; } public List<PromotionOrderLineReq>? Lines { get; set; } }
public class PromotionOrderLineReq { public string? RefCode { get; set; } public int RefType { get; set; } public int Qty { get; set; } public decimal Amount { get; set; } }
public class CarRecommendReq { public string? Code { get; set; } public string Name { get; set; } = ""; public string? DealerCode { get; set; } public DateTime EffDateStart { get; set; } public DateTime EffDateEnd { get; set; } public bool FlagAllModel { get; set; } = true; public decimal PointValAllModel { get; set; } public string? Remark { get; set; } }
public class CarRecommendDtlReq { public string? ModelCode { get; set; } public decimal PointVal { get; set; } public string? Remark { get; set; } }
public class CarRecommendCalcReq { public string? DealerCode { get; set; } public string? ModelCode { get; set; } }
public class CardPromotionProgramReq { public string? Code { get; set; } public string Name { get; set; } = ""; public DateTime EffDateStart { get; set; } public DateTime EffDateEnd { get; set; } public bool FlagAllDL { get; set; } = true; public string? Remark { get; set; } }
public class CardPromotionProgramDtlReq { public string? CardType { get; set; } public int Qty { get; set; } public string? Unit { get; set; } public string? Remark { get; set; } }
public class CardPromotionProgramSpecReq { public string? DealerCode { get; set; } }
public class CardPromotionUseReq { public string? DealNo { get; set; } public string? DealerCode { get; set; } public string? CardNo { get; set; } public string? MemberNo { get; set; } public string? CardType { get; set; } public int Qty { get; set; } public DateTime? At { get; set; } }
public class BirthdayPolicyReq { public string? Code { get; set; } public string Name { get; set; } = ""; public DateTime EffDateStart { get; set; } public DateTime EffDateEnd { get; set; } public bool FlagPoint { get; set; } = true; public decimal ParamValue { get; set; } = 1; public string? Remark { get; set; } }
public class BirthdayPolicyDtlReq { public string? CardType { get; set; } public decimal Point { get; set; } public string? Remark { get; set; } }
public class BirthdayCheckReq { public string? MemberNo { get; set; } public string? CardType { get; set; } public DateTime? DateOfBirth { get; set; } public DateTime? At { get; set; } }
public class BirthdayGrantReq { public string? MemberNo { get; set; } public string? CardNo { get; set; } public string? CardType { get; set; } public string? DealerCode { get; set; } public DateTime? DateOfBirth { get; set; } public DateTime? At { get; set; } }
public class BirthdayVoucherCheckReq { public string? MemberNo { get; set; } public string? CardType { get; set; } public DateTime? DateOfBirth { get; set; } public DateTime? At { get; set; } }
public class BirthdayVoucherIssueReq { public string? MemberNo { get; set; } public string? CardNo { get; set; } public string? CardType { get; set; } public DateTime? DateOfBirth { get; set; } public DateTime? At { get; set; } }
public class IssueVoucherReq { public string? Code { get; set; } public string Name { get; set; } = ""; public DateTime EffDateStart { get; set; } public DateTime EffDateEnd { get; set; } public int QtyVoucher { get; set; } public int QtyDateUse { get; set; } public int FavorType { get; set; } public int IssueForm { get; set; } public string? Remark { get; set; } }
public class IssueVoucherScopeReq { public int ScopeType { get; set; } public string? Value { get; set; } }
public class IssueVoucherProductReq { public int RefType { get; set; } public string? RefCode { get; set; } public string? RefName { get; set; } }
public class IssueVoucherPriceReq { public int IssueType { get; set; } public string? IssueTypeDtl { get; set; } public decimal UPDc { get; set; } public decimal UPRateDc { get; set; } public decimal UPDcMax { get; set; } public string? Remark { get; set; } }
public class IssueVoucherIssueReq { public string? VoucherNo { get; set; } public string? Receiver { get; set; } public DateTime? At { get; set; } }
public class IssueUseReq { public string? VoucherNo { get; set; } public string? OrderNo { get; set; } public DateTime? At { get; set; } }
public class ParamPromotionTypeReq { public string? Code { get; set; } public string Name { get; set; } = ""; public string? Remark { get; set; } }
public class ParamPromotionReq { public string? ProgramCode { get; set; } public string? ProgramName { get; set; } public int ParamPromotionTypeId { get; set; } public int QtyDateBefore { get; set; } public int QtyDateAfter { get; set; } public string? Remark { get; set; } }
public class ParamWindowReq { public string? ProgramCode { get; set; } public string? TypeCode { get; set; } public DateTime? Anchor { get; set; } }
public class PromotionTypeReq { public string? Code { get; set; } public string Name { get; set; } = ""; public string? Remark { get; set; } }
public class PromotionPrmInMainReq { public int MainTypeId { get; set; } public int PrmTypeId { get; set; } public string? Remark { get; set; } }
public class PrmInMainReq { public string? MainTypeCode { get; set; } public string? PrmTypeCode { get; set; } }
public class DiscountCodeReq { public string? Code { get; set; } public string? Description { get; set; } public int DiscountType { get; set; } public decimal DiscountAmount { get; set; } public int RemainQty { get; set; } public DateTime EffectDateFrom { get; set; } public DateTime EffectDateTo { get; set; } }
public class DealerDiscountMapReq { public string? DealerCode { get; set; } public string? DiscountCode { get; set; } public string? Remark { get; set; } }
public class DiscountCheckReq { public string? Code { get; set; } public decimal OrderAmount { get; set; } public DateTime? At { get; set; } }
public class DiscountApplyReq { public string? Code { get; set; } public decimal OrderAmount { get; set; } public DateTime? At { get; set; } }
public class VoucherIdGenReq { public int Amount { get; set; } = 1; public DateTime? At { get; set; } }
public class VoucherIdValidateReq { public string? VoucherNo { get; set; } }
public class IntroductionGrantReq { public string? NewMemberNo { get; set; } public string? ReferrerMemberNo { get; set; } public string? CardNo { get; set; } public string? CardTypeUse { get; set; } public string? CardTypeInit { get; set; } public string? DealerCode { get; set; } public decimal PointIntro { get; set; } public decimal ParamValue { get; set; } = 1; public DateTime? At { get; set; } }
public class CarPurchasePointReq { public string? MemberNo { get; set; } public string? CardNo { get; set; } public string? CardTypeUse { get; set; } public string? CardTypeInit { get; set; } public string? DealerCode { get; set; } public string? PrProgramCode { get; set; } public decimal PointBuyCar { get; set; } public decimal ParamValue { get; set; } = 1; public DateTime? At { get; set; } }
public class RankPolicyReq { public string? Code { get; set; } public string? CardType { get; set; } public int Value { get; set; } public decimal PointUpBegin { get; set; } public decimal PointUpEnd { get; set; } public int QtyVisitUpBegin { get; set; } public int QtyVisitUpEnd { get; set; } public decimal PointKeepBegin { get; set; } public decimal PointKeepEnd { get; set; } public int QtyVisitKeepBegin { get; set; } public int QtyVisitKeepEnd { get; set; } public int QtyMonth { get; set; } = 12; public string? Remark { get; set; } }
public class RankEvalReq { public string? CardType { get; set; } public decimal Point { get; set; } public int QtyVisit { get; set; } }
public class PolicyMoneyToPointReq { public string? Code { get; set; } public string Name { get; set; } = ""; public DateTime EffDateStart { get; set; } public DateTime EffDateEnd { get; set; } public string? Remark { get; set; } }
public class PolicyMoneyToPointDtlReq { public string? CardType { get; set; } public decimal ConvertValue { get; set; } public decimal ConvertPoint { get; set; } public decimal ValueRankCardType { get; set; } public decimal DiscountRate { get; set; } public string? Remark { get; set; } }
public class MoneyToPointCalcReq { public string? CardType { get; set; } public decimal Amount { get; set; } public DateTime? At { get; set; } }
public class MemberDiscountLineReq { public decimal AmountForDC { get; set; } public decimal PaymentDiscountRate { get; set; } public bool FlagDiscount { get; set; } = true; }
public class MemberDiscountCalcReq { public string? RefNo { get; set; } public string? CardTypeApply { get; set; } public DateTime? At { get; set; } public List<MemberDiscountLineReq>? Lines { get; set; } }
public class MemberDiscountRecordReq { public string? RefNo { get; set; } public string? DealerCode { get; set; } public string? MemberNo { get; set; } public string? CardNo { get; set; } public string? CardTypeUse { get; set; } public string? CardTypeInit { get; set; } public string? CardTypeApply { get; set; } public DateTime? At { get; set; } public List<MemberDiscountLineReq>? Lines { get; set; } }
public class ExpenseTypeReq { public string? Code { get; set; } public string Name { get; set; } = ""; public string? Remark { get; set; } }
public class PolicyExpenseTypeRowReq { public string? ExpenseType { get; set; } public string? ExpenseTypeNameActual { get; set; } public bool FlagPoint { get; set; } = true; public bool FlagPointRank { get; set; } public decimal AmountRate { get; set; } public decimal MaxRankReviewPoint { get; set; } public decimal MaxAccumulationPoint { get; set; } public bool FlagCountService { get; set; } public bool FlagDiscount { get; set; } public decimal DiscountRate { get; set; } public string? Remark { get; set; } }
public class PolicyExpenseTypeSaveReq { public string? PolicyExpenseTypeNo { get; set; } public List<PolicyExpenseTypeRowReq>? Rows { get; set; } }
public class ExpensePointCalcReq { public string? ExpenseType { get; set; } public decimal Amount { get; set; } }
