using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using ChurchManager.Api.Data;
using ChurchManager.Api.DTOs;
using ChurchManager.Api.Interfaces;
using ChurchManager.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChurchManager.Api.Controllers;

[ApiController]
[Authorize(Roles = "Administrador,Igreja")]
[Route("api/church/settings")]
public sealed class ChurchSettingsController(
    AppDbContext db,
    IChurchNotificationService notificationService) : ControllerBase
{
    private int? CurrentChurchId => int.TryParse(User.FindFirstValue("church_id"), out var id) ? id : null;
    private string CurrentUserEmail => User.FindFirstValue(ClaimTypes.Email) ?? "sistema@churchmanager";
    private bool IsInstitutionalAccount => string.Equals(User.FindFirstValue(ClaimTypes.Role), "Igreja", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Consulta os dados institucionais da própria igreja.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (CurrentChurchId is not int churchId) return Unauthorized();

        var church = await db.Churches.AsNoTracking().FirstOrDefaultAsync(c => c.Id == churchId, cancellationToken);
        if (church is null) return NotFound(new { message = "Igreja não encontrada." });

        return Ok(new ChurchSettingsResponse(
            church.Name,
            church.Denomination,
            church.Email,
            church.Phone,
            church.Address,
            church.LogoUrl
        ));
    }

    /// <summary>
    /// Atualiza dados institucionais da igreja.
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> Update([FromBody] ChurchSettingsRequest request, CancellationToken cancellationToken)
    {
        if (CurrentChurchId is not int churchId) return Unauthorized();

        var name = request.ChurchName?.Trim() ?? string.Empty;
        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var denomination = request.Denomination?.Trim();
        var phone = request.Phone?.Trim() ?? string.Empty;
        var address = request.Address?.Trim() ?? string.Empty;
        var logoUrl = request.LogoUrl?.Trim();

        if (name.Length is < 2 or > 100)
            return BadRequest(new { message = "O nome da igreja deve ter de 2 a 100 caracteres." });

        if (string.IsNullOrWhiteSpace(email) || !new EmailAddressAttribute().IsValid(email))
            return BadRequest(new { message = "Informe um e-mail institucional válido." });

        var church = await db.Churches.FirstOrDefaultAsync(c => c.Id == churchId, cancellationToken);
        if (church is null) return NotFound();

        church.Name = name;
        church.Denomination = denomination;
        church.Email = email;
        church.Phone = phone;
        church.Address = address;
        church.LogoUrl = logoUrl;

        await db.SaveChangesAsync(cancellationToken);

        await notificationService.NotifyEntityChangedAsync(churchId, "settings", "updated", new
        {
            churchName = church.Name,
            denomination = church.Denomination,
            email = church.Email,
            phone = church.Phone,
            address = church.Address,
            logoUrl = church.LogoUrl
        });

        return Ok(new ChurchSettingsResponse(
            church.Name,
            church.Denomination,
            church.Email,
            church.Phone,
            church.Address,
            church.LogoUrl
        ));
    }

    /// <summary>
    /// Lista os administradores ativos da igreja.
    /// </summary>
    [HttpGet("admins")]
    public async Task<IActionResult> GetAdmins(CancellationToken cancellationToken)
    {
        if (CurrentChurchId is not int churchId) return Unauthorized();

        var admins = await db.Users.AsNoTracking()
            .Where(u => u.ChurchId == churchId && u.Role == "Administrador" && u.IsActive)
            .OrderBy(u => u.Name)
            .Select(u => new AdminUserResponse(
                u.Id,
                null,
                u.Name,
                u.Email,
                u.Role,
                u.IsActive,
                u.CreatedAt
            ))
            .ToListAsync(cancellationToken);

        return Ok(admins);
    }

    /// <summary>
    /// Lista os membros elegíveis para promoção a administrador.
    /// </summary>
    [HttpGet("eligible-members")]
    public async Task<IActionResult> GetEligibleMembers(CancellationToken cancellationToken)
    {
        if (CurrentChurchId is not int churchId) return Unauthorized();

        var members = await db.Members.AsNoTracking()
            .Where(m => m.ChurchId == churchId && m.Status == "Ativo")
            .OrderBy(m => m.Name)
            .Select(m => new
            {
                m.Id,
                m.Name,
                m.Email,
                m.Phone,
                m.Ministry,
                m.Role,
                HasUserAccount = m.UserId.HasValue || db.Users.Any(u => u.ChurchId == churchId && u.Email == m.Email)
            })
            .ToListAsync(cancellationToken);

        return Ok(members);
    }

    /// <summary>
    /// PROMOÇÃO SEGURA DE MEMBRO A ADMINISTRADOR.
    /// Exclusivo em Configurações. Registra em AuditLog.
    /// </summary>
    [HttpPost("promote-admin")]
    public async Task<IActionResult> PromoteAdmin([FromBody] PromoteAdminRequest request, CancellationToken cancellationToken)
    {
        if (CurrentChurchId is not int churchId) return Unauthorized();

        var member = await db.Members.FirstOrDefaultAsync(m => m.Id == request.MemberId && m.ChurchId == churchId, cancellationToken);
        if (member is null)
            return NotFound(new { message = "Membro não encontrado nesta igreja." });

        // A conta institucional só pode realizar o provisionamento do PRIMEIRO administrador.
        // Depois disso, somente um administrador já existente pode promover outros membros.
        if (IsInstitutionalAccount)
        {
            var activeAdminCount = await db.Users.CountAsync(
                u => u.ChurchId == churchId && u.Role == "Administrador" && u.IsActive,
                cancellationToken);

            if (activeAdminCount > 0)
                return Forbid();
        }
        else if (!User.IsInRole("Administrador"))
        {
            return Forbid();
        }

        // A promoção exige uma conta pessoal já cadastrada pelo próprio membro.
        // Nunca criar acesso administrativo com senha padrão ou conhecida.
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.ChurchId == churchId && (u.Id == member.UserId || u.Email == member.Email),
            cancellationToken);

        if (user is null)
            return BadRequest(new
            {
                message = "Este membro ainda não possui uma conta pessoal. Ele precisa se cadastrar como membro antes da promoção."
            });

        user.Role = "Administrador";
        user.IsActive = true;
        member.Role = "Administrador";

        var adminRole = await db.Roles.FirstOrDefaultAsync(r => r.ChurchId == churchId && r.Name == "Administrador", cancellationToken);
        if (adminRole != null)
        {
            user.CargoId = adminRole.Id;
            adminRole.Users = await db.Users.CountAsync(u => u.ChurchId == churchId && u.Role == "Administrador" && u.IsActive, cancellationToken) + 1;
        }

        // Auditoria obrigatória
        db.AuditLogs.Add(new AuditLog
        {
            ChurchId = churchId,
            PerformedByEmail = CurrentUserEmail,
            TargetUserId = user.Id,
            TargetUserEmail = user.Email,
            Action = "AdminPromotion",
            Details = $"Membro '{member.Name}' ({member.Email}) foi promovido a Administrador por '{CurrentUserEmail}'.",
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);

        await notificationService.NotifyEntityChangedAsync(churchId, "roles", "promoted", new
        {
            userId = user.Id,
            memberId = member.Id,
            name = user.Name,
            role = user.Role
        }, adminOnly: true);

        return Ok(new
        {
            message = $"Membro '{member.Name}' promovido a Administrador com sucesso.",
            userId = user.Id,
            name = user.Name,
            role = user.Role
        });
    }

    /// <summary>
    /// REBAIXAMENTO / REMOÇÃO DO CARGO DE ADMINISTRADOR.
    /// Validação de segurança: Impede deixar a igreja sem administradores ativos!
    /// </summary>
    [HttpPost("demote-admin")]
    public async Task<IActionResult> DemoteAdmin([FromBody] DemoteAdminRequest request, CancellationToken cancellationToken)
    {
        // A conta institucional não administra cargos de usuários depois do provisionamento inicial.
        if (IsInstitutionalAccount || !User.IsInRole("Administrador")) return Forbid();
        if (CurrentChurchId is not int churchId) return Unauthorized();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId && u.ChurchId == churchId, cancellationToken);
        if (user is null)
            return NotFound(new { message = "Usuário não encontrado nesta igreja." });

        if (!string.Equals(user.Role, "Administrador", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Este usuário não possui cargo de Administrador." });

        // Validação obrigatória: Impede deixar a igreja com ZERO administradores ativos
        var totalAdmins = await db.Users.CountAsync(u => u.ChurchId == churchId && u.Role == "Administrador" && u.IsActive, cancellationToken);
        if (totalAdmins <= 1)
        {
            return BadRequest(new
            {
                message = "Operação bloqueada: A igreja deve possuir ao menos um Administrador ativo."
            });
        }

        user.Role = "Membro";
        var member = await db.Members.FirstOrDefaultAsync(m => m.ChurchId == churchId && (m.UserId == user.Id || m.Email == user.Email), cancellationToken);
        if (member != null) member.Role = "Membro";

        var memberRole = await db.Roles.FirstOrDefaultAsync(r => r.ChurchId == churchId && r.Name == "Membro", cancellationToken);
        if (memberRole != null) user.CargoId = memberRole.Id;

        // Auditoria
        db.AuditLogs.Add(new AuditLog
        {
            ChurchId = churchId,
            PerformedByEmail = CurrentUserEmail,
            TargetUserId = user.Id,
            TargetUserEmail = user.Email,
            Action = "AdminDemotion",
            Details = $"Usuário '{user.Name}' ({user.Email}) teve o cargo de Administrador removido por '{CurrentUserEmail}'.",
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);

        await notificationService.NotifyEntityChangedAsync(churchId, "roles", "demoted", new
        {
            userId = user.Id,
            name = user.Name,
            role = user.Role
        }, adminOnly: true);

        return Ok(new
        {
            message = $"Cargo de administrador do usuário '{user.Name}' foi removido com sucesso.",
            userId = user.Id,
            role = user.Role
        });
    }

    /// <summary>
    /// Histórico de auditoria da igreja.
    /// </summary>
    [HttpGet("audit-logs")]
    public async Task<IActionResult> GetAuditLogs(CancellationToken cancellationToken)
    {
        if (CurrentChurchId is not int churchId) return Unauthorized();

        var logs = await db.AuditLogs.AsNoTracking()
            .Where(l => l.ChurchId == churchId)
            .OrderByDescending(l => l.CreatedAt)
            .Take(100)
            .Select(l => new AuditLogResponse(
                l.Id,
                l.Action,
                l.PerformedByEmail,
                l.TargetUserEmail,
                l.Details,
                l.CreatedAt
            ))
            .ToListAsync(cancellationToken);

        return Ok(logs);
    }
}
