using System.Data;
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
[Authorize(Roles = "Administrador,Membro")]
[Route("api/donations")]
public sealed class DonationsController(AppDbContext db, IChurchNotificationService notificationService) : ControllerBase
{
    private static readonly HashSet<string> AllowedCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dízimo", "Oferta", "Missões", "Ação social", "Construção", "Outra"
    };

    private static readonly HashSet<string> AllowedPaymentMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "PIX", "Dinheiro", "Transferência bancária"
    };

    private int? CurrentChurchId => int.TryParse(User.FindFirstValue("church_id"), out var id) ? id : null;
    private int? CurrentUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private bool IsAdmin =>
        string.Equals(User.FindFirstValue(ClaimTypes.Role), "Administrador", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Administradores visualizam as contribuições da igreja; membros visualizam somente as próprias.
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
            query = query.Where(d => d.Status == status.Trim());

        var donations = await query
            .OrderByDescending(d => d.DonationDate)
            .Select(d => new DonationResponse(
                d.Id,
                d.UserId,
                d.DonorName,
                d.Amount,
                d.Category,
                d.PaymentMethod,
                d.ReceiptUrl,
                d.QrCode,
                d.Status,
                d.DonationDate))
            .ToListAsync(cancellationToken);

        return Ok(donations);
    }

    [HttpGet("my")]
    public async Task<IActionResult> GetMyDonations(CancellationToken cancellationToken)
    {
        if (CurrentChurchId is not int churchId || CurrentUserId is not int userId)
            return Unauthorized();

        var donations = await db.Donations.AsNoTracking()
            .Where(d => d.ChurchId == churchId && d.UserId == userId)
            .OrderByDescending(d => d.DonationDate)
            .Select(d => new DonationResponse(
                d.Id,
                d.UserId,
                d.DonorName,
                d.Amount,
                d.Category,
                d.PaymentMethod,
                d.ReceiptUrl,
                d.QrCode,
                d.Status,
                d.DonationDate))
            .ToListAsync(cancellationToken);

        return Ok(donations);
    }

    /// <summary>
    /// Registra a intenção de contribuição. O valor não entra no caixa até a confirmação administrativa.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDonationRequest request, CancellationToken cancellationToken)
    {
        if (CurrentChurchId is not int churchId || CurrentUserId is not int userId)
            return Unauthorized();

        var category = AllowedCategories.FirstOrDefault(x =>
            x.Equals(request.Category?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (category is null)
            return BadRequest(new { message = "Selecione uma finalidade de contribuição válida." });

        var paymentMethod = AllowedPaymentMethods.FirstOrDefault(x =>
            x.Equals(request.PaymentMethod?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (paymentMethod is null)
            return BadRequest(new { message = "Selecione uma forma de contribuição válida." });

        var donation = new Donation
        {
            ChurchId = churchId,
            UserId = userId,
            DonorName = User.FindFirstValue(ClaimTypes.Name) ?? "Membro",
            Amount = request.Amount,
            Category = category,
            PaymentMethod = paymentMethod,
            Status = "Pendente",
            DonationDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        ChurchEntityValidator.Normalize(donation);
        if (ChurchEntityValidator.Validate(donation) is { } error)
            return BadRequest(new { message = error });

        db.Donations.Add(donation);
        await db.SaveChangesAsync(cancellationToken);

        await notificationService.NotifyEntityChangedAsync(churchId, "donations", "created", donation);

        return CreatedAtAction(nameof(GetMyDonations), null, ToResponse(donation));
    }

    /// <summary>
    /// Confirma o recebimento e cria uma única entrada financeira vinculada à contribuição.
    /// </summary>
    [HttpPost("{id:int}/confirm")]
    public async Task<IActionResult> Confirm(int id, CancellationToken cancellationToken)
    {
        if (!IsAdmin) return Forbid();
        if (CurrentChurchId is not int churchId) return Unauthorized();

        await using var dbTransaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        var donation = await db.Donations.SingleOrDefaultAsync(
            d => d.Id == id && d.ChurchId == churchId, cancellationToken);
        if (donation is null) return NotFound();

        var existingTransaction = await db.Transactions.SingleOrDefaultAsync(
            t => t.DonationId == donation.Id && t.ChurchId == churchId, cancellationToken);

        if (existingTransaction is not null)
        {
            if (!string.Equals(donation.Status, "Confirmada", StringComparison.OrdinalIgnoreCase))
            {
                donation.Status = "Confirmada";
                await db.SaveChangesAsync(cancellationToken);
            }

            await dbTransaction.CommitAsync(cancellationToken);
            return Ok(ToResponse(donation));
        }

        if (!string.Equals(donation.Status, "Pendente", StringComparison.OrdinalIgnoreCase))
            return Conflict(new { message = "Somente contribuições pendentes podem ser confirmadas." });

        donation.Status = "Confirmada";

        db.Transactions.Add(new FinanceTransaction
        {
            ChurchId = churchId,
            Description = $"{donation.Category} de {donation.DonorName}",
            Category = donation.Category,
            Amount = donation.Amount,
            TransactionType = "Income",
            TransactionDate = DateTime.UtcNow,
            Method = donation.PaymentMethod,
            DonationId = donation.Id,
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);

        await notificationService.NotifyEntityChangedAsync(churchId, "donations", "updated", donation);
        await notificationService.NotifyEntityChangedAsync(churchId, "transactions", "created", new
        {
            donationId = donation.Id,
            donation.Amount,
            donation.Category
        }, adminOnly: true);

        return Ok(ToResponse(donation));
    }

    /// <summary>
    /// Recusa uma intenção pendente sem gerar movimentação financeira.
    /// </summary>
    [HttpPost("{id:int}/reject")]
    public async Task<IActionResult> Reject(int id, CancellationToken cancellationToken)
    {
        if (!IsAdmin) return Forbid();
        if (CurrentChurchId is not int churchId) return Unauthorized();

        var donation = await db.Donations.SingleOrDefaultAsync(
            d => d.Id == id && d.ChurchId == churchId, cancellationToken);
        if (donation is null) return NotFound();

        var alreadyReceived = await db.Transactions.AnyAsync(
            t => t.DonationId == donation.Id && t.ChurchId == churchId, cancellationToken);
        if (alreadyReceived || string.Equals(donation.Status, "Confirmada", StringComparison.OrdinalIgnoreCase))
            return Conflict(new { message = "Uma contribuição recebida não pode ser recusada." });

        if (string.Equals(donation.Status, "Recusada", StringComparison.OrdinalIgnoreCase))
            return Ok(ToResponse(donation));

        if (!string.Equals(donation.Status, "Pendente", StringComparison.OrdinalIgnoreCase))
            return Conflict(new { message = "Somente contribuições pendentes podem ser recusadas." });

        donation.Status = "Recusada";
        await db.SaveChangesAsync(cancellationToken);
        await notificationService.NotifyEntityChangedAsync(churchId, "donations", "updated", donation);

        return Ok(ToResponse(donation));
    }

    private static DonationResponse ToResponse(Donation donation) => new(
        donation.Id,
        donation.UserId,
        donation.DonorName,
        donation.Amount,
        donation.Category,
        donation.PaymentMethod,
        donation.ReceiptUrl,
        donation.QrCode,
        donation.Status,
        donation.DonationDate);
}
