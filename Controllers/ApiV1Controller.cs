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
public class ApiV1Controller(IPromoService svc, IVoucherService vouchers, IVoucherProgramService programs, ICarPromotionService carPromos, IPromotionProgramService promotions, ICarRecommendService carRecommends, ICache cache, ITenantContext tenant) : ControllerBase
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
            mains = p.Mains.Select(m => new { m.Id, m.Idx, m.Qty, m.Amount, m.TotalValOrd })
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
        var o = await promotions.CalcAsync(r.OrderAmount, r.Qty, r.At);
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
public class PromotionCalcReq { public decimal OrderAmount { get; set; } public int Qty { get; set; } public DateTime? At { get; set; } }
public class CarRecommendReq { public string? Code { get; set; } public string Name { get; set; } = ""; public string? DealerCode { get; set; } public DateTime EffDateStart { get; set; } public DateTime EffDateEnd { get; set; } public bool FlagAllModel { get; set; } = true; public decimal PointValAllModel { get; set; } public string? Remark { get; set; } }
public class CarRecommendDtlReq { public string? ModelCode { get; set; } public decimal PointVal { get; set; } public string? Remark { get; set; } }
public class CarRecommendCalcReq { public string? DealerCode { get; set; } public string? ModelCode { get; set; } }
