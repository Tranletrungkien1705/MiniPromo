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
public class ApiV1Controller(IPromoService svc, IVoucherService vouchers, IVoucherProgramService programs, ICache cache, ITenantContext tenant) : ControllerBase
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

    // Tính giá trị voucher cho một xe theo chương trình đang hiệu lực (công khai).
    [HttpPost("voucher-program/calc")]
    public async Task<IActionResult> CalcVoucher([FromBody] VoucherCalcReq r)
    {
        var o = await programs.CalcAsync(r.ModelCode ?? "", r.DeliveryDate, r.RegistrationDate);
        return o.ok ? Ok(new { ok = o.ok, msg = o.msg, pointVoucher = o.pointVoucher, pointUseLimit = o.pointUseLimit, modelCode = o.modelCode })
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
