using System.Security.Claims;
using ChurchManager.Api.Data;
using ChurchManager.Api.Interfaces;
using ChurchManager.Api.Models;
using ChurchManager.Api.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChurchManager.Api.Controllers;

[ApiController]
[Authorize(Roles = "Administrador")]
public abstract class ChurchCrudController<T>(AppDbContext db, IChurchNotificationService notificationService) : ControllerBase where T : class, IChurchOwnedEntity, new()
{
    protected AppDbContext Db { get; } = db;
    protected IChurchNotificationService NotificationService { get; } = notificationService;
    protected int? CurrentChurchId => int.TryParse(User.FindFirstValue("church_id"), out var id) ? id : null;
    protected abstract string ResourceName { get; }

    private async Task<string?> ValidateReferencesAsync(T item, int churchId)
    {
        // Todo vínculo enviado pelo cliente deve apontar para um registro da mesma igreja.
        if (item is Member member && member.UserId is int userId &&
            !await Db.Users.AnyAsync(user => user.Id == userId && user.ChurchId == churchId))
            return "A conta de usuário selecionada não pertence a esta igreja.";

        if (item is ChurchScale scale && scale.EventId is int eventId &&
            !await Db.Events.AnyAsync(churchEvent => churchEvent.Id == eventId && churchEvent.ChurchId == churchId))
            return "O evento selecionado não pertence a esta igreja.";

        if (item is FinanceTransaction transaction && transaction.DonationId is int donationId &&
            !await Db.Donations.AnyAsync(donation => donation.Id == donationId && donation.ChurchId == churchId))
            return "A doação vinculada não pertence a esta igreja.";

        return null;
    }

    [HttpGet]
    public virtual async Task<IActionResult> GetAll()
    {
        if (CurrentChurchId is not int churchId) return Unauthorized();

        var list = await Db.Set<T>().AsNoTracking()
            .Where(x => x.ChurchId == churchId)
            .OrderByDescending(x => x.Id)
            .ToListAsync();

        if (typeof(T) == typeof(ChurchRole))
        {
            var counts = await Db.Users
                .Where(x => x.ChurchId == churchId && x.CargoId.HasValue)
                .GroupBy(x => x.CargoId!.Value)
                .Select(g => new { CargoId = g.Key, Total = g.Count() })
                .ToDictionaryAsync(x => x.CargoId, x => x.Total);

            foreach (var role in list.Cast<ChurchRole>())
                role.Users = counts.GetValueOrDefault(role.Id);
        }

        return Ok(list);
    }

    [HttpGet("{id:int}")]
    public virtual async Task<IActionResult> GetById(int id)
    {
        if (CurrentChurchId is not int churchId) return Unauthorized();

        var item = await Db.Set<T>().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.ChurchId == churchId);

        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public virtual async Task<IActionResult> Create([FromBody] T item)
    {
        if (CurrentChurchId is not int churchId) return Unauthorized();

        if (await ValidateReferencesAsync(item, churchId) is { } referenceError)
            return BadRequest(new { message = referenceError });

        // REGRA DE SEGURANÇA: Cargo Administrador só pode ser atribuído pela promoção em Configurações
        if (item is Member member && string.Equals(member.Role, "Administrador", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "O cargo Administrador só pode ser atribuído pelo fluxo seguro de promoção em Configurações." });

        ChurchEntityValidator.Normalize(item);
        if (ChurchEntityValidator.Validate(item) is { } validationError)
            return BadRequest(new { message = validationError });

        item.Id = 0;
        item.ChurchId = churchId;
        item.CreatedAt = DateTime.UtcNow;

        if (item is ChurchRole newRole) newRole.Users = 0;

        Db.Set<T>().Add(item);
        await Db.SaveChangesAsync();

        // Emite atualização em tempo real via SignalR para os usuários da mesma igreja
        await NotificationService.NotifyEntityChangedAsync(churchId, ResourceName, "created", item);

        return CreatedAtAction(nameof(GetById), new { id = item.Id }, item);
    }

    [HttpPut("{id:int}")]
    public virtual async Task<IActionResult> Update(int id, [FromBody] T input)
    {
        if (CurrentChurchId is not int churchId) return Unauthorized();

        var current = await Db.Set<T>().FirstOrDefaultAsync(x => x.Id == id && x.ChurchId == churchId);
        if (current is null) return NotFound();

        if (await ValidateReferencesAsync(input, churchId) is { } referenceError)
            return BadRequest(new { message = referenceError });

        if (input is Member member && string.Equals(member.Role, "Administrador", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "O cargo Administrador só pode ser atribuído pelo fluxo seguro de promoção em Configurações." });

        ChurchEntityValidator.Normalize(input);
        if (ChurchEntityValidator.Validate(input) is { } validationError)
            return BadRequest(new { message = validationError });

        var createdAt = current.CreatedAt;

        if (input is ChurchRole incomingRole && current is ChurchRole existingRole)
            incomingRole.Users = existingRole.Users;

        if (input is ChurchScale incomingScale && current is ChurchScale existingScale)
            incomingScale.EventId = existingScale.EventId;

        if (input is ChurchEvent incomingEvent && current is ChurchEvent existingEvent)
        {
            incomingEvent.IsRestricted = existingEvent.IsRestricted;
            incomingEvent.RecurrenceType = existingEvent.RecurrenceType;
        }

        Db.Entry(current).CurrentValues.SetValues(input);
        current.Id = id;
        current.ChurchId = churchId;
        current.CreatedAt = createdAt;

        await Db.SaveChangesAsync();

        // Emite atualização em tempo real via SignalR
        await NotificationService.NotifyEntityChangedAsync(churchId, ResourceName, "updated", current);

        return Ok(current);
    }

    [HttpDelete("{id:int}")]
    public virtual async Task<IActionResult> Delete(int id)
    {
        if (CurrentChurchId is not int churchId) return Unauthorized();

        var current = await Db.Set<T>().FirstOrDefaultAsync(x => x.Id == id && x.ChurchId == churchId);
        if (current is null) return NotFound();

        if (typeof(T) == typeof(ChurchRole) &&
            await Db.Users.AnyAsync(x => x.CargoId == id && x.ChurchId == churchId))
            return Conflict(new { message = "Este cargo está vinculado a usuários e não pode ser excluído." });

        Db.Remove(current);
        await Db.SaveChangesAsync();

        // Emite exclusão em tempo real via SignalR
        await NotificationService.NotifyEntityChangedAsync(churchId, ResourceName, "deleted", new { id });

        return NoContent();
    }
}

[Route("api/members")]
public sealed class MembersController(AppDbContext db, IChurchNotificationService notificationService)
    : ChurchCrudController<Member>(db, notificationService)
{
    protected override string ResourceName => "members";
}

[Route("api/ministries")]
public sealed class MinistriesController(AppDbContext db, IChurchNotificationService notificationService)
    : ChurchCrudController<Ministry>(db, notificationService)
{
    protected override string ResourceName => "ministries";
}

[Route("api/events")]
public sealed class EventsController(AppDbContext db, IChurchNotificationService notificationService)
    : ChurchCrudController<ChurchEvent>(db, notificationService)
{
    protected override string ResourceName => "events";

    /// <summary>
    /// Consulta presenças confirmadas no evento (PresencaEvento do Felippe).
    /// </summary>
    [HttpGet("{id:int}/attendances")]
    public async Task<IActionResult> GetAttendances(int id)
    {
        if (CurrentChurchId is not int churchId) return Unauthorized();

        var attendances = await Db.EventAttendances.AsNoTracking()
            .Where(a => a.EventId == id && a.ChurchId == churchId)
            .Include(a => a.User)
            .Select(a => new
            {
                a.Id,
                a.EventId,
                a.UserId,
                UserName = a.User != null ? a.User.Name : "Participante",
                a.ConfirmedAt
            })
            .ToListAsync();

        return Ok(attendances);
    }
}

[Route("api/transactions")]
public sealed class TransactionsController(AppDbContext db, IChurchNotificationService notificationService)
    : ChurchCrudController<FinanceTransaction>(db, notificationService)
{
    protected override string ResourceName => "transactions";

    /// <summary>
    /// Resumo financeiro consolidado da igreja.
    /// </summary>
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        if (CurrentChurchId is not int churchId) return Unauthorized();

        var transactions = await Db.Transactions.AsNoTracking()
            .Where(t => t.ChurchId == churchId)
            .Select(t => new { t.TransactionType, t.Amount })
            .ToListAsync();

        var income = transactions.Where(x => x.TransactionType == "Income").Sum(x => x.Amount);
        var expense = transactions.Where(x => x.TransactionType == "Expense").Sum(x => x.Amount);

        return Ok(new
        {
            totalIncome = income,
            totalExpense = expense,
            netBalance = income - expense
        });
    }
}

[Route("api/roles")]
public sealed class RolesController(AppDbContext db, IChurchNotificationService notificationService)
    : ChurchCrudController<ChurchRole>(db, notificationService)
{
    protected override string ResourceName => "roles";
}

[Route("api/scales")]
public sealed class ScalesController(AppDbContext db, IChurchNotificationService notificationService)
    : ChurchCrudController<ChurchScale>(db, notificationService)
{
    protected override string ResourceName => "scales";

    /// <summary>
    /// Compatibilidade com o endpoint do Felippe /api/Escala/evento/{idEvento}.
    /// </summary>
    [HttpGet("by-event/{eventId:int}")]
    public async Task<IActionResult> GetByEvent(int eventId)
    {
        if (CurrentChurchId is not int churchId) return Unauthorized();

        var scales = await Db.Scales.AsNoTracking()
            .Where(s => s.EventId == eventId && s.ChurchId == churchId)
            .ToListAsync();

        return Ok(scales);
    }
}
