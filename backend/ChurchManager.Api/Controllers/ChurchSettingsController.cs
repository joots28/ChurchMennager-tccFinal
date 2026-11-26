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
                u.Phone ?? string.Empty,
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
            .Where(m => m.ChurchId == churchId && m.Status == "Ativo" && m.Role != "Administrador")
            .OrderBy(m => m.Name)
            .Select(m => new
            {
                m.Id,
                m.Name,
                m.Email,
                m.Phone,
                m.Ministry,
                m.Role,
                HasUserAccount = db.Users.Any(u => u.ChurchId == churchId && u.IsActive && ((m.UserId.HasValue && u.Id == m.UserId.Value) || u.Email == m.Email))
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

        // A conta institucional administra os perfis administrativos da própria igreja.
        // Um administrador também pode promover membros conforme o fluxo operacional existente.
        if (!IsInstitutionalAccount && !User.IsInRole("Administrador"))
            return Forbid();

        // A promoção exige uma conta pessoal já cadastrada pelo próprio membro.
        // Nunca criar acesso administrativo com senha padrão ou conhecida.
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.ChurchId == churchId && u.IsActive && (u.Id == member.UserId || u.Email == member.Email),
            cancellationToken);

        if (user is null)
            return BadRequest(new
            {
                message = "Este membro ainda não possui uma conta pessoal. Ele precisa se cadastrar como membro antes da promoção."
            });

        if (string.Equals(user.Role, "Administrador", StringComparison.OrdinalIgnoreCase))
            return Conflict(new { message = "Este membro já possui acesso administrativo." });

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
    /// Atualiza os dados de um administrador, mantendo o vínculo com o membro.
    /// Exclusivo da conta institucional da própria igreja.
    /// </summary>
    [HttpPut("admins/{userId:int}")]
    public async Task<IActionResult> UpdateAdmin(int userId, [FromBody] UpdateAdminRequest request, CancellationToken cancellationToken)
    {
        if (!IsInstitutionalAccount) return Forbid();
        if (CurrentChurchId is not int churchId) return Unauthorized();

        var name = request.Name?.Trim() ?? string.Empty;
        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var phone = request.Phone?.Trim() ?? string.Empty;

        if (name.Length is < 2 or > 160)
            return BadRequest(new { message = "O nome deve ter entre 2 e 160 caracteres." });
        if (string.IsNullOrWhiteSpace(email) || !new EmailAddressAttribute().IsValid(email) || email.Length > 254)
            return BadRequest(new { message = "Informe um e-mail válido." });
        if (phone.Length > 40)
            return BadRequest(new { message = "O telefone deve ter até 40 caracteres." });

        var admin = await db.Users.FirstOrDefaultAsync(
            u => u.Id == userId && u.ChurchId == churchId && u.Role == "Administrador" && u.IsActive,
            cancellationToken);
        if (admin is null)
            return NotFound(new { message = "Administrador ativo não encontrado nesta igreja." });

        var emailInUse = await db.Users.AnyAsync(u => u.Id != userId && u.Email == email, cancellationToken)
            || await db.Churches.AnyAsync(c => c.Email == email, cancellationToken);
        if (emailInUse)
            return Conflict(new { message = "Este e-mail já está vinculado a outra conta." });

        var oldEmail = admin.Email;
        admin.Name = name;
        admin.Email = email;
        admin.Phone = phone;

        var member = await db.Members.FirstOrDefaultAsync(
            m => m.ChurchId == churchId && (m.UserId == admin.Id || m.Email == oldEmail),
            cancellationToken);
        if (member is not null)
        {
            member.UserId = admin.Id;
            member.Name = name;
            member.Email = email;
            member.Phone = phone;
            member.Role = "Administrador";
        }

        db.AuditLogs.Add(new AuditLog
        {
            ChurchId = churchId,
            PerformedByEmail = CurrentUserEmail,
            TargetUserId = admin.Id,
            TargetUserEmail = email,
            Action = "AdminUpdated",
            Details = $"Dados do administrador '{oldEmail}' atualizados por '{CurrentUserEmail}'.",
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
        await notificationService.NotifyEntityChangedAsync(churchId, "roles", "admin-updated", new { userId = admin.Id }, adminOnly: true);

        return Ok(new AdminUserResponse(
            admin.Id,
            member?.Id,
            admin.Name,
            admin.Email,
            admin.Phone ?? string.Empty,
            admin.Role,
            admin.IsActive,
            admin.CreatedAt));
    }

    /// <summary>
    /// Remove o privilégio administrativo e preserva a conta como membro da igreja.
    /// Não apaga o histórico nem o cadastro da pessoa.
    /// </summary>
    [HttpDelete("admins/{userId:int}")]
    public async Task<IActionResult> DeleteAdmin(int userId, CancellationToken cancellationToken)
    {
        if (!IsInstitutionalAccount) return Forbid();
        if (CurrentChurchId is not int churchId) return Unauthorized();

        var admin = await db.Users.FirstOrDefaultAsync(
            u => u.Id == userId && u.ChurchId == churchId && u.Role == "Administrador" && u.IsActive,
            cancellationToken);
        if (admin is null)
            return NotFound(new { message = "Administrador ativo não encontrado nesta igreja." });

        var activeAdmins = await db.Users.CountAsync(
            u => u.ChurchId == churchId && u.Role == "Administrador" && u.IsActive,
            cancellationToken);
        if (activeAdmins <= 1)
            return BadRequest(new { message = "A igreja precisa manter pelo menos um administrador ativo." });

        admin.Role = "Membro";

        var member = await db.Members.FirstOrDefaultAsync(
            m => m.ChurchId == churchId && (m.UserId == admin.Id || m.Email == admin.Email),
            cancellationToken);
        if (member is not null)
        {
            member.UserId = admin.Id;
            member.Role = "Membro";
        }

        var adminRole = await db.Roles.FirstOrDefaultAsync(r => r.ChurchId == churchId && r.Name == "Administrador", cancellationToken);
        var memberRole = await db.Roles.FirstOrDefaultAsync(r => r.ChurchId == churchId && r.Name == "Membro", cancellationToken);
        if (memberRole is not null) admin.CargoId = memberRole.Id;
        db.AuditLogs.Add(new AuditLog
        {
            ChurchId = churchId,
            PerformedByEmail = CurrentUserEmail,
            TargetUserId = admin.Id,
            TargetUserEmail = admin.Email,
            Action = "AdminAccessRevoked",
            Details = $"O acesso administrativo de '{admin.Email}' foi removido por '{CurrentUserEmail}'. A conta e o cadastro como membro foram preservados.",
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);

        // Atualiza contagens depois de persistir a alteração de cargo do usuário.
        if (adminRole is not null)
            adminRole.Users = await db.Users.CountAsync(
                u => u.ChurchId == churchId && u.CargoId == adminRole.Id && u.IsActive,
                cancellationToken);
        if (memberRole is not null)
            memberRole.Users = await db.Users.CountAsync(
                u => u.ChurchId == churchId && u.CargoId == memberRole.Id && u.IsActive,
                cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        await notificationService.NotifyEntityChangedAsync(churchId, "roles", "admin-deleted", new { userId = admin.Id }, adminOnly: true);

        return Ok(new { message = "Administrador removido. A conta continua ativa como membro." });
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
