using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ChurchManager.Api.Interfaces;
using ChurchManager.Api.Models;
using Microsoft.IdentityModel.Tokens;

namespace ChurchManager.Api.Services;

public sealed class TokenService(IConfiguration config) : ITokenService
{
    public string GenerateToken(int userId, string name, string email, string role, int churchId, string churchName, string accessMode)
    {
        var key = Environment.GetEnvironmentVariable("JWT_KEY") ?? config["Jwt:Key"] ?? "";
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
            throw new InvalidOperationException("JWT_KEY deve possuir no mínimo 32 caracteres.");

        var issuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? config["Jwt:Issuer"] ?? "ChurchManager.Api";
        var audience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? config["Jwt:Audience"] ?? "ChurchManager.Web";

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, name),
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Role, role),
            new Claim("church_id", churchId.ToString()),
            new Claim("church_name", churchName),
            new Claim("access_mode", accessMode)
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddSeconds(-30), // clock skew margin
            expires: DateTime.UtcNow.AddHours(12),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateChurchInstitutionalToken(Church church)
    {
        return GenerateToken(
            userId: -church.Id, // id institucional negativo exclusivo
            name: church.Name,
            email: church.Email,
            role: "Igreja",
            churchId: church.Id,
            churchName: church.Name,
            accessMode: "church");
    }
}
