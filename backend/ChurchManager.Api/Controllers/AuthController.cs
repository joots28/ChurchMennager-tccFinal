using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using ChurchManager.Api.Data;
using ChurchManager.Api.DTOs;
using ChurchManager.Api.Interfaces;
using ChurchManager.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ChurchManager.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    AppDbContext db,
    IPasswordHasher<AppUser> userHasher,
    IPasswordHasher<Church> churchHasher,
    ITokenService tokenService,
    IChurchNotificationService notificationService,
    ILogger<AuthController> logger) : ControllerBase
{
    /// <summary>
    /// FLUXO 1 — Entrar como Igreja: Cadastro Institucional de Nova Igreja.
    /// Único fluxo com permissão para criar uma nova igreja no sistema.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("Auth")]
    [HttpPost("register-church")]
    public async Task<IActionResult> RegisterChurch([FromBody] ChurchRegisterRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var denomination = request.Denomination?.Trim();
        var phone = request.Phone?.Trim() ?? string.Empty;
        var address = request.Address?.Trim() ?? string.Empty;
        var logoUrl = request.LogoUrl?.Trim();

        if (name.Length is < 2 or > 100)
            return BadRequest(new { message = "O nome da igreja deve ter entre 2 e 100 caracteres." });

        if (string.IsNullOrWhiteSpace(email) || !new EmailAddressAttribute().IsValid(email))
            return BadRequest(new { message = "Informe um e-mail institucional válido." });

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length is < 8 or > 128)
            return BadRequest(new { message = "A senha institucional deve ter entre 8 e 128 caracteres." });

        // Validação de duplicidade de igreja
        if (await db.Churches.AnyAsync(c => c.Email == email, cancellationToken))
            return Conflict(new { message = "Já existe uma igreja cadastrada com este e-mail institucional." });

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var church = new Church
            {
                Name = name,
                Denomination = denomination,
                Email = email,
                Phone = phone,
                Address = address,
                LogoUrl = logoUrl,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            church.PasswordHash = churchHasher.HashPassword(church, request.Password);
            db.Churches.Add(church);
            await db.SaveChangesAsync(cancellationToken);

            // Cargos padrão da nova igreja
            var adminRole = new ChurchRole
            {
                ChurchId = church.Id,
                Name = "Administrador",
                Description = "Acesso completo à administração da igreja.",
                PermissionsJson = "[\"Membros\",\"Eventos\",\"Financeiro\",\"Relatórios\",\"Configurações\",\"Perfil\",\"Escalas\"]",
                Users = 0,
                CreatedAt = DateTime.UtcNow
            };
            var leaderRole = new ChurchRole
            {
                ChurchId = church.Id,
                Name = "Líder",
                Description = "Gestão de eventos, escalas e equipes ministeriais.",
                PermissionsJson = "[\"Membros\",\"Eventos\",\"Escalas\",\"Perfil\"]",
                Users = 0,
                CreatedAt = DateTime.UtcNow
            };
            var memberRole = new ChurchRole
            {
                ChurchId = church.Id,
                Name = "Membro",
                Description = "Acesso ao portal comunitário e escalas do membro.",
                PermissionsJson = "[\"Eventos\",\"Escalas\",\"Perfil\"]",
                Users = 0,
                CreatedAt = DateTime.UtcNow
            };
            db.Roles.AddRange(adminRole, leaderRole, memberRole);
            await db.SaveChangesAsync(cancellationToken);

            // Registra no AuditLog o provisionamento institucional da igreja
            db.AuditLogs.Add(new AuditLog
            {
                ChurchId = church.Id,
                PerformedByEmail = church.Email,
                Action = "ChurchRegistered",
                Details = $"Igreja '{church.Name}' cadastrada com sucesso.",
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            var token = tokenService.GenerateChurchInstitutionalToken(church);
            return Ok(new AuthResponse(
                token,
                -church.Id,
                church.Name,
                church.Email,
                "Igreja",
                church.Id,
                church.Name,
                "church"
            ));
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogError(ex, "Erro ao registrar igreja: {Name}", name);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "Não foi possível concluir o cadastro da igreja. Verifique os dados e tente novamente."
            });
        }
    }

    /// <summary>
    /// FLUXO 2 — Entrar como Membro: Cadastro de Conta Pessoal de Membro.
    /// REGRA FUNDAMENTAL: Novos usuários sempre começam como Membros.
    /// Requer a seleção de uma igreja já existente.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("Auth")]
    [HttpPost("register-member")]
    public async Task<IActionResult> RegisterMember([FromBody] MemberRegisterRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var phone = request.Phone?.Trim() ?? string.Empty;
        var ministry = request.Ministry?.Trim() ?? string.Empty;

        if (name.Length is < 2 or > 160)
            return BadRequest(new { message = "O nome deve ter entre 2 e 160 caracteres." });

        if (string.IsNullOrWhiteSpace(email) || !new EmailAddressAttribute().IsValid(email))
            return BadRequest(new { message = "Informe um e-mail válido." });

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length is < 8 or > 128)
            return BadRequest(new { message = "A senha deve ter entre 8 e 128 caracteres." });

        var church = await db.Churches.FirstOrDefaultAsync(c => c.Id == request.ChurchId && c.IsActive, cancellationToken);
        if (church is null)
            return BadRequest(new { message = "A igreja selecionada não foi encontrada ou está inativa." });

        if (await db.Users.AnyAsync(u => u.Email == email, cancellationToken))
            return Conflict(new { message = "Já existe uma conta cadastrada com esse e-mail." });

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var defaultRole = await db.Roles.FirstOrDefaultAsync(r => r.ChurchId == church.Id && r.Name == "Membro", cancellationToken);

            var user = new AppUser
            {
                ChurchId = church.Id,
                CargoId = defaultRole?.Id,
                Name = name,
                Email = email,
                Phone = phone,
                // REGRA INEGOCIÁVEL: Perfil inicial SEMPRE Membro!
                Role = "Membro",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            user.PasswordHash = userHasher.HashPassword(user, request.Password);
            db.Users.Add(user);
            await db.SaveChangesAsync(cancellationToken);

            // Cria o registro correspondente em Members para constar na lista da igreja
            var existingMember = await db.Members.FirstOrDefaultAsync(m => m.ChurchId == church.Id && m.Email == email, cancellationToken);
            Member registeredMember;
            var memberAction = "created";

            if (existingMember is null)
            {
                registeredMember = new Member
                {
                    ChurchId = church.Id,
                    UserId = user.Id,
                    Name = name,
                    Email = email,
                    Phone = phone,
                    Ministry = string.IsNullOrWhiteSpace(ministry) ? "Todos" : ministry,
                    Role = "Membro",
                    Status = "Ativo",
                    CreatedAt = DateTime.UtcNow
                };
                db.Members.Add(registeredMember);
            }
            else
            {
                registeredMember = existingMember;
                memberAction = "updated";
                registeredMember.UserId = user.Id;
                registeredMember.Name = name;
                registeredMember.Email = email;
                if (!string.IsNullOrWhiteSpace(phone)) registeredMember.Phone = phone;
                if (!string.IsNullOrWhiteSpace(ministry)) registeredMember.Ministry = ministry;
            }

            if (defaultRole != null)
            {
                defaultRole.Users = await db.Users.CountAsync(u => u.ChurchId == church.Id && u.CargoId == defaultRole.Id, cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            await notificationService.NotifyEntityChangedAsync(church.Id, "members", memberAction, new
            {
                id = registeredMember.Id,
                name = registeredMember.Name,
                email = registeredMember.Email
            });

            var token = tokenService.GenerateToken(
                user.Id,
                user.Name,
                user.Email,
                "Membro",
                church.Id,
                church.Name,
                "member"
            );

            return Ok(new AuthResponse(
                token,
                user.Id,
                user.Name,
                user.Email,
                "Membro",
                church.Id,
                church.Name,
                "member"
            ));
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogError(ex, "Erro ao registrar membro: {Email}", email);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "Não foi possível concluir o cadastro. Verifique os dados e tente novamente."
            });
        }
    }

    /// <summary>
    /// Endpoint legado desativado. Utilize register-church ou register-member
    /// conforme a modalidade de acesso, sem misturar os dois fluxos.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("register")]
    public IActionResult RegisterLegacy() =>
        StatusCode(StatusCodes.Status410Gone, new
        {
            message = "Este cadastro legado foi desativado. Utilize o fluxo Entrar como Igreja ou Entrar como Membro."
        });


    /// <summary>
    /// Autenticação unificada para as 3 modalidades:
    /// 'church' -> Entrar como Igreja (credenciais institucionais da instituição)
    /// 'admin' -> Entrar como Administrador (valida perfil de administrador da igreja)
    /// 'member' -> Entrar como Membro (portal comunitário do membro)
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("Login")]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var identifier = request.Identifier?.Trim() ?? string.Empty;
        var password = request.Password ?? string.Empty;
        var accessMode = request.AccessMode?.Trim().ToLowerInvariant() ?? "admin";

        if (identifier.Length is 0 or > 254 || password.Length is 0 or > 128)
            return BadRequest(new { message = "Informe e-mail e senha." });

        if (!identifier.Contains('@') || !new EmailAddressAttribute().IsValid(identifier))
            return BadRequest(new { message = "Utilize o e-mail cadastrado para entrar." });

        if (accessMode is not ("church" or "admin" or "member"))
            return BadRequest(new { message = "Modalidade de acesso inválida. Use 'church', 'admin' ou 'member'." });

        // MODALIDADE 1: Entrar como Igreja
        if (accessMode == "church")
        {
            Church? church;
            if (identifier.Contains('@'))
            {
                church = await db.Churches.FirstOrDefaultAsync(
                    c => c.Email == identifier.ToLowerInvariant() && c.IsActive,
                    cancellationToken);
            }
            else
            {
                church = await db.Churches.FirstOrDefaultAsync(
                    c => c.Name.ToUpper() == identifier.ToUpper() && c.IsActive,
                    cancellationToken);
            }

            if (church is null || string.IsNullOrEmpty(church.PasswordHash) ||
                churchHasher.VerifyHashedPassword(church, church.PasswordHash, password) == PasswordVerificationResult.Failed)
            {
                return Unauthorized(new { message = "Credenciais institucionais inválidas." });
            }

            var token = tokenService.GenerateChurchInstitutionalToken(church);
            return Ok(new AuthResponse(
                token,
                -church.Id,
                church.Name,
                church.Email,
                "Igreja",
                church.Id,
                church.Name,
                "church"
            ));
        }

        // MODALIDADES 2 e 3: Usuários Pessoais (Administrador ou Membro)
        AppUser? user;
        if (identifier.Contains('@'))
        {
            user = await db.Users
                .Include(u => u.Church)
                .FirstOrDefaultAsync(u => u.Email == identifier.ToLowerInvariant() && u.IsActive, cancellationToken);
        }
        else
        {
            user = await db.Users
                .Include(u => u.Church)
                .FirstOrDefaultAsync(u => u.Name.ToUpper() == identifier.ToUpper() && u.IsActive, cancellationToken);
        }

        if (user is null ||
            userHasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new { message = "Usuário ou senha inválidos." });
        }

        if (user.Church is null || !user.Church.IsActive)
            return Unauthorized(new { message = "A igreja vinculada a esta conta está inativa ou não foi encontrada." });

        var isAdmin = string.Equals(user.Role, "Administrador", StringComparison.OrdinalIgnoreCase);

        // Cada perfil deve entrar exclusivamente pelo seu ambiente.
        if (accessMode == "admin" && !isAdmin)
        {
            return Unauthorized(new
            {
                message = "Esta conta não possui privilégios de Administrador. Acesse pelo Portal do Membro."
            });
        }

        if (accessMode == "member" && isAdmin)
        {
            return Unauthorized(new
            {
                message = "Esta conta pertence à administração. Utilize o acesso de Administrador."
            });
        }

        var activeRole = isAdmin ? "Administrador" : "Membro";
        var tokenValue = tokenService.GenerateToken(
            user.Id,
            user.Name,
            user.Email,
            activeRole,
            user.ChurchId,
            user.Church.Name,
            accessMode
        );

        return Ok(new AuthResponse(
            tokenValue,
            user.Id,
            user.Name,
            user.Email,
            activeRole,
            user.ChurchId,
            user.Church.Name,
            accessMode
        ));
    }

    /// <summary>
    /// Validação da sessão ativa para restauração confiável sem erros 401 transitórios.
    /// </summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser(CancellationToken cancellationToken)
    {
        var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        var churchIdStr = User.FindFirstValue("church_id");

        if (!int.TryParse(churchIdStr, out var churchId))
            return Unauthorized(new { message = "Sessão inválida: identificador da igreja ausente." });

        var church = await db.Churches.AsNoTracking().FirstOrDefaultAsync(c => c.Id == churchId && c.IsActive, cancellationToken);
        if (church is null)
            return Unauthorized(new { message = "Igreja não encontrada ou inativa." });

        if (string.Equals(role, "Igreja", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new
            {
                id = -church.Id,
                name = church.Name,
                email = church.Email,
                role = "Igreja",
                churchId = church.Id,
                churchName = church.Name,
                accessMode = "church"
            });
        }

        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out var userId))
            return Unauthorized(new { message = "Sessão inválida: identificador do usuário ausente." });

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.ChurchId == churchId && u.IsActive, cancellationToken);
        if (user is null)
            return Unauthorized(new { message = "Usuário não encontrado ou inativo." });

        return Ok(new
            {
                id = user.Id,
                name = user.Name,
                email = user.Email,
                role = user.Role,
                churchId = church.Id,
                churchName = church.Name,
                accessMode = string.Equals(user.Role, "Administrador", StringComparison.OrdinalIgnoreCase) ? "admin" : "member"
            });
    }

    [Authorize]
    [HttpPost("logout")]
    public IActionResult Logout() => NoContent();
}

public sealed record LegacyRegisterRequest(
    string? Name,
    string? Email,
    string? Password,
    string? ChurchName,
    int ChurchId = 0,
    string? Denomination = null,
    string? Phone = null,
    string? Address = null,
    string? LogoUrl = null,
    string? Ministry = null);
