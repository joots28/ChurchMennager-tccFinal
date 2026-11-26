using System.Security.Claims;
using ChurchManager.Api.Data;
using ChurchManager.Api.DTOs;
using ChurchManager.Api.Interfaces;
using ChurchManager.Api.Models;
using ChurchManager.Api.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChurchManager.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/donations")]
public sealed class DonationsController(AppDbContext db, IChurchNotificationService notificationService) : ControllerBase
{
    private int? CurrentChurchId => int.TryParse(User.FindFirstValue("church_id"), out var id) ? id : null;
    private int? CurrentUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private bool IsAdmin =>
        string.Equals(User.FindFirstValue(ClaimTypes.Role), "Administrador", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(User.FindFirstValue(ClaimTypes.Role), "Igreja", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Administradores consultam as contribuições da igreja; membros consultam somente as próprias.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetDonations([FromQuery] string? status, CancellationToken cancellationToken)
    {
        if (CurrentChurchId is not int churchId) return Unauthorized();

        var query = db.Donations.AsNoTracking().Where(d => d.ChurchId == churchId);

        if (!IsAdmin)
        {
            if (CurrentUserId is not int userId || userId <= 0) return Unauthorized();
            query = query.Where(d => d.UserId == userId);
        }

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(d => d.Status == status);

        var list = await query
            .OrderByDescending(d => d.DonationDate)
            .Select(d => new DonationResponse(
                d.Id,
                d.UserId,
                d.DonorName,
                d.Amount,
                d.Category,
                d.ReceiptUrl,
                d.QrCode,
                d.Status,
                d.DonationDate
            ))
            .ToListAsync(cancellationToken);

        return Ok(list);
    }

    [HttpGet("my")]
    public async Task<IActionResult> GetMyDonations(CancellationToken cancellationToken)
    {
        if (CurrentChurchId is not int churchId || CurrentUserId is not int userId || userId <= 0)
            return Unauthorized();

        var list = await db.Donations.AsNoTracking()
            .Where(d => d.ChurchId == churchId && d.UserId == userId)
            .OrderByDescending(d => d.DonationDate)
            .Select(d => new DonationResponse(
                d.Id,
                d.UserId,
                d.DonorName,
                d.Amount,
                d.Category,
                d.ReceiptUrl,
                d.QrCode,
                d.Status,
                d.DonationDate
            ))
            .ToListAsync(cancellationToken);

        return Ok(list);
    }

    /// <summary>
    /// Registra uma oferta ou dízimo como pendente. O lançamento financeiro só nasce após conferência.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDonationRequest request, CancellationToken cancellationToken)
    {
        if (CurrentChurchId is not int churchId) return Unauthorized();

        var category = request.Category?.Trim();
        if (category is not ("Dízimo" or "Oferta"))
            return BadRequest(new { message = "Selecione Dízimo ou Oferta." });

        var userName = User.FindFirstValue(ClaimTypes.Name) ?? "Doador";
        var donor = !string.IsNullOrWhiteSpace(request.DonorName) ? request.DonorName.Trim() : userName;

        var donation = new Donation
        {
            ChurchId = churchId,
            UserId = CurrentUserId is > 0 ? CurrentUserId : null,
            DonorName = donor,
            Amount = request.Amount,
            Category = category,
            ReceiptUrl = request.ReceiptUrl?.Trim(),
            QrCode = request.QrCode?.Trim(),
            Status = "Pendente",
            DonationDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        ChurchEntityValidator.Normalize(donation);
        if (ChurchEntityValidator.Validate(donation) is { } error)
            return BadRequest(new { message = error });

        db.Donations.Add(donation);
        await db.SaveChangesAsync(cancellationToken);

        await notificationService.NotifyEntityChangedAsync(churchId, "donations", "created", new
        {
            id = donation.Id,
            donorName = donation.DonorName,
            category = donation.Category,
            amount = donation.Amount,
            status = donation.Status
        });

        return CreatedAtAction(nameof(GetDonations), new { id = donation.Id }, new DonationResponse(
            donation.Id,
            donation.UserId,
            donation.DonorName,
            donation.Amount,
            donation.Category,
            donation.ReceiptUrl,
            donation.QrCode,
            donation.Status,
            donation.DonationDate
        ));
    }

    /// <summary>
    /// Confirma ou recusa uma contribuição pendente. A confirmação gera exatamente um lançamento de entrada.
    /// </summary>
    [HttpPatch("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(
        int id,
        [FromBody] UpdateDonationStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAdmin) return Forbid();
        if (CurrentChurchId is not int churchId) return Unauthorized();

        var donation = await db.Donations
            .FirstOrDefaultAsync(d => d.Id == id && d.ChurchId == churchId, cancellationToken);

        if (donation is null) return NotFound(new { message = "Contribuição não encontrada nesta igreja." });
        if (donation.Status != "Pendente")
            return Conflict(new { message = "Esta contribuição já foi analisada." });

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        donation.Status = request.Status;

        if (request.Status == "Confirmada")
        {
            var alreadyRegistered = await db.Transactions
                .AnyAsync(t => t.ChurchId == churchId && t.DonationId == donation.Id, cancellationToken);

            if (!alreadyRegistered)
            {
                db.Transactions.Add(new FinanceTransaction
                {
                    ChurchId = churchId,
                    DonationId = donation.Id,
                    Description = $"{donation.Category} de {donation.DonorName}",
                    Category = donation.Category,
                    Amount = donation.Amount,
                    TransactionType = "Income",
                    TransactionDate = donation.DonationDate,
                    Method = "PIX",
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await notificationService.NotifyEntityChangedAsync(churchId, "donations", "updated", new
        {
            id = donation.Id,
            status = donation.Status
        });
        if (request.Status == "Confirmada")
            await notificationService.NotifyEntityChangedAsync(churchId, "transactions", "created", new { donationId = donation.Id }, adminOnly: true);

        return Ok(new
        {
            message = request.Status == "Confirmada"
                ? "Contribuição confirmada e lançada no financeiro."
                : "Contribuição recusada.",
            donation = new DonationResponse(
                donation.Id,
                donation.UserId,
                donation.DonorName,
                donation.Amount,
                donation.Category,
                donation.ReceiptUrl,
                donation.QrCode,
                donation.Status,
                donation.DonationDate)
        });
    }
}
