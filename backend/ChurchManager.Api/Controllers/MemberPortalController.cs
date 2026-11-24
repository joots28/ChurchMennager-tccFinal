using System.Security.Claims;
using System.Text.Json;
using ChurchManager.Api.Data;
using ChurchManager.Api.DTOs;
using ChurchManager.Api.Interfaces;
using ChurchManager.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChurchManager.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/member")]
public sealed class MemberPortalController(AppDbContext db) : ControllerBase
{
    private int? CurrentChurchId => int.TryParse(User.FindFirstValue("church_id"), out var id) ? id : null;
    private int? CurrentUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private string CurrentUserName => User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;

    /// <summary>
    /// Eventos públicos e abertos para os membros da comunidade.
    /// </summary>
    [HttpGet("events")]
    public async Task<IActionResult> GetEvents(CancellationToken cancellationToken)
    {
        if (CurrentChurchId is not int churchId) return Unauthorized();

        var events = await db.Events.AsNoTracking()
            .Where(x => x.ChurchId == churchId && !x.IsRestricted)
            .OrderBy(x => x.StartsAt)
            .ToListAsync(cancellationToken);

        var userAttendances = new HashSet<int>();
        if (CurrentUserId.HasValue)
        {
            userAttendances = (await db.EventAttendances.AsNoTracking()
                .Where(a => a.ChurchId == churchId && a.UserId == CurrentUserId.Value)
                .Select(a => a.EventId)
                .ToListAsync(cancellationToken)).ToHashSet();
        }

        return Ok(events.Select(x => new
        {
            id = x.Id,
            title = x.Title,
            description = x.Description,
            date = x.StartsAt.ToString("yyyy-MM-dd"),
            time = x.StartsAt.ToString("HH:mm"),
            location = x.Location,
            ministry = x.Ministry,
            status = x.Status,
            isConfirmed = userAttendances.Contains(x.Id)
        }));
    }

    /// <summary>
    /// Confirma presença do membro em um evento.
    /// </summary>
    [HttpPost("events/{id:int}/confirm")]
    public async Task<IActionResult> ConfirmAttendance(int id, CancellationToken cancellationToken)
    {
        if (CurrentChurchId is not int churchId || CurrentUserId is not int userId)
            return Unauthorized();

        var churchEvent = await db.Events.FirstOrDefaultAsync(e => e.Id == id && e.ChurchId == churchId, cancellationToken);
        if (churchEvent is null)
            return NotFound(new { message = "Evento não encontrado." });

        var existing = await db.EventAttendances.FirstOrDefaultAsync(a => a.EventId == id && a.UserId == userId, cancellationToken);
        if (existing is null)
        {
            var attendance = new EventAttendance
            {
                ChurchId = churchId,
                EventId = id,
                UserId = userId,
                ConfirmedAt = DateTime.UtcNow
            };
            db.EventAttendances.Add(attendance);
            await db.SaveChangesAsync(cancellationToken);
        }

        return Ok(new { message = "Presença confirmada com sucesso!" });
    }

    /// <summary>
    /// Escalas onde o membro atual está escalado.
    /// </summary>
    [HttpGet("scales")]
    public async Task<IActionResult> GetScales(CancellationToken cancellationToken)
    {
        if (CurrentChurchId is not int churchId) return Unauthorized();

        var scales = await db.Scales.AsNoTracking()
            .Where(x => x.ChurchId == churchId)
            .OrderBy(x => x.StartsAt)
            .ToListAsync(cancellationToken);

        var memberScales = scales
            .Select(x => new { Scale = x, Members = ParseMembers(x.AssignedMembersJson) })
            .Where(x => string.IsNullOrWhiteSpace(CurrentUserName) ||
                        x.Members.Any(name => string.Equals(name, CurrentUserName, StringComparison.OrdinalIgnoreCase)) ||
                        x.Members.Count == 0) // exibe escalas abertas se não houver membro específico
            .Select(x => new
            {
                id = x.Scale.Id,
                title = x.Scale.Title,
                ministry = x.Scale.Ministry,
                date = x.Scale.StartsAt.ToString("yyyy-MM-dd"),
                time = x.Scale.StartsAt.ToString("HH:mm"),
                status = x.Scale.Status,
                members = string.Join(", ", x.Members)
            });

        return Ok(memberScales);
    }

    /// <summary>
    /// Ministérios disponíveis na comunidade do membro.
    /// </summary>
    [HttpGet("ministries")]
    public async Task<IActionResult> GetMinistries(CancellationToken cancellationToken)
    {
        if (CurrentChurchId is not int churchId) return Unauthorized();

        var ministries = await db.Ministries.AsNoTracking()
            .Where(m => m.ChurchId == churchId && m.Status == "Ativo")
            .OrderBy(m => m.Name)
            .Select(m => new
            {
                m.Id,
                m.Name,
                m.Description,
                m.Coordinator
            })
            .ToListAsync(cancellationToken);

        return Ok(ministries);
    }

    /// <summary>
    /// Dados institucionais da igreja para exibição no portal do membro.
    /// </summary>
    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken)
    {
        if (CurrentChurchId is not int churchId) return Unauthorized();

        var church = await db.Churches.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == churchId, cancellationToken);

        return church is null
            ? NotFound()
            : Ok(new
            {
                churchName = church.Name,
                denomination = church.Denomination,
                email = church.Email,
                phone = church.Phone,
                address = church.Address,
                logoUrl = church.LogoUrl
            });
    }

    /// <summary>
    /// Perfil pessoal do membro autenticado.
    /// </summary>
    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        if (CurrentChurchId is not int churchId || CurrentUserId is not int userId)
            return Unauthorized();

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.ChurchId == churchId, cancellationToken);
        if (user is null) return NotFound();

        var member = await db.Members.AsNoTracking().FirstOrDefaultAsync(m => m.ChurchId == churchId && (m.UserId == userId || m.Email == user.Email), cancellationToken);

        return Ok(new
        {
            id = user.Id,
            name = user.Name,
            email = user.Email,
            phone = user.Phone ?? member?.Phone ?? "",
            cpf = user.Cpf ?? "",
            birthDate = user.BirthDate?.ToString("yyyy-MM-dd"),
            photoUrl = user.PhotoUrl ?? "",
            ministry = member?.Ministry ?? "Geral",
            role = user.Role
        });
    }

    /// <summary>
    /// Atualização do perfil do membro.
    /// </summary>
    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateMemberProfileRequest request, CancellationToken cancellationToken)
    {
        if (CurrentChurchId is not int churchId || CurrentUserId is not int userId)
            return Unauthorized();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.ChurchId == churchId, cancellationToken);
        if (user is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(request.Phone)) user.Phone = request.Phone.Trim();
        if (!string.IsNullOrWhiteSpace(request.PhotoUrl)) user.PhotoUrl = request.PhotoUrl.Trim();
        if (request.BirthDate.HasValue) user.BirthDate = request.BirthDate.Value;

        var member = await db.Members.FirstOrDefaultAsync(m => m.ChurchId == churchId && (m.UserId == userId || m.Email == user.Email), cancellationToken);
        if (member != null && !string.IsNullOrWhiteSpace(request.Phone))
        {
            member.Phone = request.Phone.Trim();
        }

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = "Perfil atualizado com sucesso." });
    }

    private static List<string> ParseMembers(string? json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(string.IsNullOrWhiteSpace(json) ? "[]" : json)
                ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }
}

public sealed record UpdateMemberProfileRequest(
    string? Phone,
    DateTime? BirthDate,
    string? PhotoUrl);
