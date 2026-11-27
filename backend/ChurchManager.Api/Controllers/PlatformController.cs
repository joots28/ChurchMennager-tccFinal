using System.ComponentModel.DataAnnotations;
using ChurchManager.Api.Data;
using ChurchManager.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ChurchManager.Api.Controllers;

[ApiController]
[Route("api/platform")]
[Authorize(Roles = "SuperAdministrador")]
public sealed class PlatformController(
    AppDbContext db,
    IPasswordHasher<Church> churchHasher,
    IPasswordHasher<PlatformAdmin> adminHasher) : ControllerBase
{
    [HttpGet("churches")]
    public async Task<IActionResult> GetChurches(CancellationToken ct)
    {
        var churches = await db.Churches.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name, c.Email, c.Denomination, c.IsActive, c.CreatedAt,
                Members = db.Users.Count(u => u.ChurchId == c.Id && u.IsActive) })
            .ToListAsync(ct);
        return Ok(churches);
    }

    [HttpPost("churches")]
    public async Task<IActionResult> CreateChurch([FromBody] CreatePlatformChurchRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? "";
        var email = request.Email?.Trim().ToLowerInvariant() ?? "";
        if (name.Length is < 2 or > 100 || !new EmailAddressAttribute().IsValid(email))
            return BadRequest(new { message = "Informe o nome e um e-mail institucional válido." });
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length is < 12 or > 128)
            return BadRequest(new { message = "A senha institucional deve ter entre 12 e 128 caracteres." });
        if (await db.Churches.AnyAsync(c => c.Email == email, ct))
            return Conflict(new { message = "Já existe uma igreja cadastrada com esse e-mail." });

        var church = new Church {
            Name = name, Email = email, Denomination = request.Denomination?.Trim(),
            Phone = request.Phone?.Trim() ?? "", Address = request.Address?.Trim() ?? "",
            IsActive = true, CreatedAt = DateTime.UtcNow
        };
        church.PasswordHash = churchHasher.HashPassword(church, request.Password);
        db.Churches.Add(church);
        await db.SaveChangesAsync(ct);
        var defaults = new[] {
            new ChurchRole { ChurchId = church.Id, Name = "Administrador", Description = "Acesso administrativo da igreja.", PermissionsJson = "[\"Membros\",\"Eventos\",\"Financeiro\",\"Relatórios\",\"Configurações\",\"Perfil\",\"Escalas\"]", CreatedAt = DateTime.UtcNow },
            new ChurchRole { ChurchId = church.Id, Name = "Líder", Description = "Gestão de equipes e atividades.", PermissionsJson = "[\"Membros\",\"Eventos\",\"Escalas\",\"Perfil\"]", CreatedAt = DateTime.UtcNow },
            new ChurchRole { ChurchId = church.Id, Name = "Membro", Description = "Acesso comunitário.", PermissionsJson = "[\"Eventos\",\"Escalas\",\"Perfil\"]", CreatedAt = DateTime.UtcNow }
        };
        db.Roles.AddRange(defaults);
        db.AuditLogs.Add(new AuditLog { ChurchId = church.Id, PerformedByEmail = User.FindFirstValue(ClaimTypes.Email) ?? "platform", Action = "PlatformChurchCreated", Details = $"Igreja '{church.Name}' cadastrada por superadministrador.", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetChurches), new { id = church.Id }, new { church.Id, church.Name, church.Email, church.IsActive });
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePlatformPasswordRequest request, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            return Unauthorized();
        var admin = await db.PlatformAdmins.FirstOrDefaultAsync(a => a.Id == id && a.IsActive, ct);
        if (admin is null) return Unauthorized();
        if (adminHasher.VerifyHashedPassword(admin, admin.PasswordHash, request.CurrentPassword ?? "") == PasswordVerificationResult.Failed)
            return BadRequest(new { message = "A senha atual está incorreta." });
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 12 || request.NewPassword.Length > 128)
            return BadRequest(new { message = "A nova senha deve ter entre 12 e 128 caracteres." });
        admin.PasswordHash = adminHasher.HashPassword(admin, request.NewPassword);
        admin.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

public sealed record CreatePlatformChurchRequest(string? Name, string? Email, string? Password, string? Denomination, string? Phone, string? Address);
public sealed record ChangePlatformPasswordRequest(string? CurrentPassword, string? NewPassword);
