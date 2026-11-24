using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace ChurchManager.Api.Models;

public interface IChurchOwnedEntity
{
    int Id { get; set; }
    int ChurchId { get; set; }
    DateTime CreatedAt { get; set; }
}

public abstract class ChurchEntity : IChurchOwnedEntity
{
    [Key]
    public int Id { get; set; }
    public int ChurchId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Representa a instituição Igreja. Suporta as credenciais institucionais ("Entrar como Igreja")
/// e dados complementares provenientes de GestaoIgreja (Denominação, Logotipo).
/// </summary>
[Table("Churches")]
public sealed class Church
{
    [Key]
    public int Id { get; set; }

    [Required, StringLength(100, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [StringLength(100)]
    public string? Denomination { get; set; }

    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Hash da senha institucional para a modalidade "Entrar como Igreja".
    /// </summary>
    [StringLength(512)]
    public string? PasswordHash { get; set; }

    [StringLength(40)]
    public string Phone { get; set; } = string.Empty;

    [StringLength(250)]
    public string Address { get; set; } = string.Empty;

    [StringLength(500)]
    public string? LogoUrl { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Coleções de navegação
    [JsonIgnore]
    public ICollection<AppUser> Users { get; set; } = new List<AppUser>();
    [JsonIgnore]
    public ICollection<Member> Members { get; set; } = new List<Member>();
    [JsonIgnore]
    public ICollection<Ministry> Ministries { get; set; } = new List<Ministry>();
    [JsonIgnore]
    public ICollection<ChurchEvent> Events { get; set; } = new List<ChurchEvent>();
    [JsonIgnore]
    public ICollection<FinanceTransaction> Transactions { get; set; } = new List<FinanceTransaction>();
    [JsonIgnore]
    public ICollection<ChurchRole> Roles { get; set; } = new List<ChurchRole>();
    [JsonIgnore]
    public ICollection<ChurchScale> Scales { get; set; } = new List<ChurchScale>();
    [JsonIgnore]
    public ICollection<Donation> Donations { get; set; } = new List<Donation>();
}

/// <summary>
/// Conta de autenticação de usuário no sistema.
/// </summary>
[Table("Users")]
public sealed class AppUser
{
    [Key]
    public int Id { get; set; }

    public int ChurchId { get; set; }
    [ForeignKey(nameof(ChurchId)), JsonIgnore]
    public Church? Church { get; set; }

    public int? CargoId { get; set; }
    [ForeignKey(nameof(CargoId)), JsonIgnore]
    public ChurchRole? Cargo { get; set; }

    [Required, StringLength(160, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(512)]
    [JsonIgnore]
    public string PasswordHash { get; set; } = string.Empty;

    [StringLength(14)]
    public string? Cpf { get; set; }

    public DateTime? BirthDate { get; set; }

    [StringLength(40)]
    public string? Phone { get; set; }

    [StringLength(500)]
    public string? PhotoUrl { get; set; }

    /// <summary>
    /// Perfil no sistema: "Administrador", "Membro" ou "Igreja".
    /// Novos cadastros de usuários SEMPRE iniciam com perfil "Membro".
    /// </summary>
    [Required, StringLength(80)]
    public string Role { get; set; } = "Membro";

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Cadastro de membro da igreja.
/// </summary>
[Table("Members")]
public sealed class Member : ChurchEntity
{
    public int? UserId { get; set; }
    [ForeignKey(nameof(UserId)), JsonIgnore]
    public AppUser? User { get; set; }

    [Required, StringLength(160, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [EmailAddress, StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [StringLength(40)]
    public string Phone { get; set; } = string.Empty;

    [StringLength(120)]
    public string Ministry { get; set; } = "Todos";

    [StringLength(40)]
    public string Status { get; set; } = "Ativo";

    [StringLength(80)]
    public string Role { get; set; } = "Membro";
}

/// <summary>
/// Ministério da igreja (Louvor, Comunicação, Recepção, etc.).
/// </summary>
[Table("Ministries")]
public sealed class Ministry : ChurchEntity
{
    [Required, StringLength(160, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    [StringLength(160)]
    public string Coordinator { get; set; } = string.Empty;

    [StringLength(40)]
    public string Status { get; set; } = "Ativo";
}

/// <summary>
/// Evento da igreja.
/// </summary>
[Table("Events")]
public sealed class ChurchEvent : ChurchEntity
{
    [Required, StringLength(100, MinimumLength = 2)]
    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public DateTime StartsAt { get; set; }

    [StringLength(255)]
    public string Location { get; set; } = string.Empty;

    [StringLength(120)]
    public string Ministry { get; set; } = "Todos";

    [StringLength(40)]
    public string Status { get; set; } = "Planejado";

    public bool IsRestricted { get; set; }

    [StringLength(50)]
    public string RecurrenceType { get; set; } = "Nenhuma";

    [JsonIgnore]
    public ICollection<ChurchScale> Scales { get; set; } = new List<ChurchScale>();

    [JsonIgnore]
    public ICollection<EventAttendance> Attendances { get; set; } = new List<EventAttendance>();
}

/// <summary>
/// Confirmação de presença em eventos (PresencaEvento do Felippe).
/// </summary>
[Table("EventAttendances")]
public sealed class EventAttendance : ChurchEntity
{
    public int EventId { get; set; }
    [ForeignKey(nameof(EventId)), JsonIgnore]
    public ChurchEvent? Event { get; set; }

    public int UserId { get; set; }
    [ForeignKey(nameof(UserId)), JsonIgnore]
    public AppUser? User { get; set; }

    public DateTime ConfirmedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Escala de ministérios para atividades e eventos (Escala do Felippe).
/// </summary>
[Table("Scales")]
public sealed class ChurchScale : ChurchEntity
{
    [Required, StringLength(180, MinimumLength = 2)]
    public string Title { get; set; } = string.Empty;

    [Required, StringLength(120)]
    public string Ministry { get; set; } = string.Empty;

    public DateTime StartsAt { get; set; }

    /// <summary>
    /// Lista de membros serializada em JSON para compatibilidade imediata com formulários React.
    /// </summary>
    public string AssignedMembersJson { get; set; } = "[]";

    [StringLength(40)]
    public string Status { get; set; } = "Pendente";

    public int? EventId { get; set; }
    [ForeignKey(nameof(EventId)), JsonIgnore]
    public ChurchEvent? Event { get; set; }

    [JsonIgnore]
    public ICollection<ScaleParticipant> Participants { get; set; } = new List<ScaleParticipant>();
}

/// <summary>
/// Participante em escala (ParticipacaoEscala do Felippe).
/// </summary>
[Table("ScaleParticipants")]
public sealed class ScaleParticipant : ChurchEntity
{
    public int ScaleId { get; set; }
    [ForeignKey(nameof(ScaleId)), JsonIgnore]
    public ChurchScale? Scale { get; set; }

    public int? UserId { get; set; }
    [ForeignKey(nameof(UserId)), JsonIgnore]
    public AppUser? User { get; set; }

    [Required, StringLength(160)]
    public string MemberName { get; set; } = string.Empty;

    [StringLength(50)]
    public string Status { get; set; } = "Confirmado";
}

/// <summary>
/// Movimentação financeira geral (entradas, saídas, dízimos, ofertas).
/// </summary>
[Table("Transactions")]
public sealed class FinanceTransaction : ChurchEntity
{
    [Required, StringLength(180, MinimumLength = 2)]
    public string Description { get; set; } = string.Empty;

    [StringLength(100)]
    public string Category { get; set; } = "Outros";

    [Column(TypeName = "numeric(12,2)")]
    public decimal Amount { get; set; }

    [StringLength(30)]
    public string TransactionType { get; set; } = "Income";

    public DateTime TransactionDate { get; set; }

    [StringLength(50)]
    public string Method { get; set; } = "PIX";

    public int? DonationId { get; set; }
    [ForeignKey(nameof(DonationId)), JsonIgnore]
    public Donation? Donation { get; set; }
}

/// <summary>
/// Doação registrada (Doacao do Felippe).
/// </summary>
[Table("Donations")]
public sealed class Donation : ChurchEntity
{
    public int? UserId { get; set; }
    [ForeignKey(nameof(UserId)), JsonIgnore]
    public AppUser? User { get; set; }

    [StringLength(160)]
    public string DonorName { get; set; } = string.Empty;

    [Column(TypeName = "numeric(10,2)")]
    public decimal Amount { get; set; }

    [Required, StringLength(50)]
    public string Category { get; set; } = "Dízimo";

    [StringLength(500)]
    public string? ReceiptUrl { get; set; }

    [StringLength(500)]
    public string? QrCode { get; set; }

    [StringLength(50)]
    public string Status { get; set; } = "Pendente";

    public DateTime DonationDate { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Cargos e perfis na igreja (Cargo do Felippe).
/// </summary>
[Table("Roles")]
public sealed class ChurchRole : ChurchEntity
{
    [Required, StringLength(50, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    public string PermissionsJson { get; set; } = "[]";

    public int Users { get; set; }

    [JsonIgnore]
    public ICollection<RolePermission> Permissions { get; set; } = new List<RolePermission>();
}

/// <summary>
/// Permissão granular por módulo (Permissao do Felippe).
/// </summary>
[Table("RolePermissions")]
public sealed class RolePermission
{
    [Key]
    public int Id { get; set; }

    public int CargoId { get; set; }
    [ForeignKey(nameof(CargoId)), JsonIgnore]
    public ChurchRole? Cargo { get; set; }

    [Required, StringLength(50)]
    public string Module { get; set; } = string.Empty;

    public bool CanView { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
}

/// <summary>
/// Log de auditoria para operações sensíveis (como promoção/rebaixamento de administradores).
/// </summary>
[Table("AuditLogs")]
public sealed class AuditLog : ChurchEntity
{
    public int? PerformedByUserId { get; set; }
    [StringLength(254)]
    public string PerformedByEmail { get; set; } = string.Empty;

    public int? TargetUserId { get; set; }
    [StringLength(254)]
    public string? TargetUserEmail { get; set; }

    [Required, StringLength(80)]
    public string Action { get; set; } = string.Empty;

    [StringLength(1000)]
    public string Details { get; set; } = string.Empty;
}
