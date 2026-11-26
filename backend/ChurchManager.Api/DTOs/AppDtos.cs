using System.ComponentModel.DataAnnotations;

namespace ChurchManager.Api.DTOs;

#region Autenticação e Registro

public sealed record ChurchRegisterRequest(
    [param: Required, StringLength(100, MinimumLength = 2)] string Name,
    [param: StringLength(100)] string? Denomination,
    [param: Required, EmailAddress, StringLength(254)] string Email,
    [param: Required, StringLength(128, MinimumLength = 8)] string Password,
    [param: StringLength(40)] string? Phone,
    [param: StringLength(250)] string? Address,
    [param: StringLength(500)] string? LogoUrl);

public sealed record MemberRegisterRequest(
    [param: Required, StringLength(160, MinimumLength = 2)] string Name,
    [param: Required, EmailAddress, StringLength(254)] string Email,
    [param: Required, StringLength(128, MinimumLength = 8)] string Password,
    [param: Required, Range(1, int.MaxValue)] int ChurchId,
    [param: StringLength(40)] string? Phone,
    [param: StringLength(120)] string? Ministry);

public sealed record LoginRequest(
    [param: Required, StringLength(254, MinimumLength = 1)] string Identifier,
    [param: Required, StringLength(128, MinimumLength = 1)] string Password,
    [param: StringLength(20)] string AccessMode = "admin");

public sealed record AuthResponse(
    string Token,
    int Id,
    string Name,
    string Email,
    string Role,
    int ChurchId,
    string ChurchName,
    string AccessMode);

public sealed record PromoteAdminRequest(
    [param: Required, Range(1, int.MaxValue)] int MemberId);

public sealed record DemoteAdminRequest(
    [param: Required, Range(1, int.MaxValue)] int UserId);

public sealed record AssignInitialAdminRequest(
    [param: Required, Range(1, int.MaxValue)] int MemberId);

#endregion

#region Igrejas e Configurações

public sealed record ChurchPublicDto(
    int Id,
    string Name,
    string? Denomination,
    string? LogoUrl,
    string? Address,
    string? Phone);

public sealed record ChurchSettingsRequest(
    [param: Required, StringLength(100, MinimumLength = 2)] string ChurchName,
    [param: StringLength(100)] string? Denomination,
    [param: Required, EmailAddress, StringLength(254)] string Email,
    [param: StringLength(40)] string? Phone,
    [param: StringLength(250)] string? Address,
    [param: StringLength(500)] string? LogoUrl);

public sealed record ChurchSettingsResponse(
    string ChurchName,
    string? Denomination,
    string Email,
    string Phone,
    string Address,
    string? LogoUrl);

public sealed record AuditLogResponse(
    int Id,
    string Action,
    string PerformedByEmail,
    string? TargetUserEmail,
    string Details,
    DateTime CreatedAt);

public sealed record AdminUserResponse(
    int UserId,
    int? MemberId,
    string Name,
    string Email,
    string Role,
    bool IsActive,
    DateTime CreatedAt);

#endregion

#region Doações e Finanças

public sealed record CreateDonationRequest(
    [param: Required, Range(0.01, 9999999999.99)] decimal Amount,
    [param: Required, StringLength(50)] string Category,
    [param: Required, StringLength(30)] string PaymentMethod,
    [param: StringLength(500)] string? ReceiptUrl,
    [param: StringLength(500)] string? QrCode,
    [param: StringLength(160)] string? DonorName);

public sealed record DonationResponse(
    int Id,
    int? UserId,
    string DonorName,
    decimal Amount,
    string Category,
    string PaymentMethod,
    string? ReceiptUrl,
    string? QrCode,
    string Status,
    DateTime DonationDate);

#endregion

#region Presença em Eventos

public sealed record ConfirmAttendanceRequest(
    [param: Required, Range(1, int.MaxValue)] int EventId);

public sealed record AttendanceResponse(
    int Id,
    int EventId,
    int UserId,
    string UserName,
    DateTime ConfirmedAt);

#endregion

#region Participação em Escalas

public sealed record AddScaleParticipantRequest(
    [param: Required, Range(1, int.MaxValue)] int ScaleId,
    int? UserId,
    [param: Required, StringLength(160)] string MemberName,
    [param: StringLength(50)] string Status = "Confirmado");

public sealed record ScaleParticipantResponse(
    int Id,
    int ScaleId,
    int? UserId,
    string MemberName,
    string Status);

#endregion
