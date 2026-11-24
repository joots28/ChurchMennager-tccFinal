using ChurchManager.Api.Models;

namespace ChurchManager.Api.Interfaces;

public interface ITokenService
{
    string GenerateToken(int userId, string name, string email, string role, int churchId, string churchName, string accessMode);
    string GenerateChurchInstitutionalToken(Church church);
}
