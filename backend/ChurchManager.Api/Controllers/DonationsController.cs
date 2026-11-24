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
    private bool IsAdmin => string.Equals(User.FindFirstValue(ClaimTypes.Role), "Administrador", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(User.FindFirstValue(ClaimTypes.Role), "Igreja", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Lista doações da igreja. Administradores visualizam todas; membros visualizam as suas próprias.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetDonations([FromQuery] string? status, CancellationToken cancellationToken)
    {
        if (CurrentChurchId is not int churchId) return Unauthorized();

        var query = db.Donations.AsNoTracking().Where(d => d.ChurchId == churchId);

        if (!IsAdmin)
        {
            if (CurrentUserId is not int userId) return Unauthorized();
            query = query.Where(d => d.UserId == userId);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(d => d.Status == status);
        }

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

    /// <summary>
    /// Consulta as doações do usuário autenticado (compatibilidade funcional com Felippe /api/doacao/usuario/{idUsuario}).
    /// </summary>
    [HttpGet("my")]
    public async Task<IActionResult> GetMyDonations(CancellationToken cancellationToken)
    {
        if (CurrentChurchId is not int churchId || CurrentUserId is not int userId)
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
    /// Registra uma nova doação (Dízimo, Oferta, etc.).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDonationRequest request, CancellationToken cancellationToken)
    {
        if (CurrentChurchId is not int churchId) return Unauthorized();

        var userName = User.FindFirstValue(ClaimTypes.Name) ?? "Doador Anônimo";
        var donor = !string.IsNullOrWhiteSpace(request.DonorName) ? request.DonorName.Trim() : userName;

        var donation = new Donation
        {
            ChurchId = churchId,
            UserId = CurrentUserId > 0 ? CurrentUserId : null,
            DonorName = donor,
            Amount = request.Amount,
            Category = string.IsNullOrWhiteSpace(request.Category) ? "Dízimo" : request.Category.Trim(),
            ReceiptUrl = request.ReceiptUrl?.Trim(),
            QrCode = request.QrCode?.Trim(),
            Status = "Confirmada", // Integração direta
            DonationDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        ChurchEntityValidator.Normalize(donation);
        if (ChurchEntityValidator.Validate(donation) is { } error)
            return BadRequest(new { message = error });

        // Cria automaticamente a movimentação financeira correspondente para não duplicar dados
        var transaction = new FinanceTransaction
        {
            ChurchId = churchId,
            Description = $"{donation.Category} de {donation.DonorName}",
            Category = donation.Category,
            Amount = donation.Amount,
            TransactionType = "Income",
            TransactionDate = donation.DonationDate,
            Method = "PIX",
            CreatedAt = DateTime.UtcNow
        };

        db.Donations.Add(donation);
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync(cancellationToken);

        transaction.DonationId = donation.Id;
        await db.SaveChangesAsync(cancellationToken);

        await notificationService.NotifyEntityChangedAsync(churchId, "donations", "created", donation);
        await notificationService.NotifyEntityChangedAsync(churchId, "transactions", "created", transaction, adminOnly: true);

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
}
