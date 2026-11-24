using ChurchManager.Api.Data;
using ChurchManager.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChurchManager.Api.Controllers;

[ApiController]
[Route("api/churches")]
public sealed class ChurchesController(AppDbContext db) : ControllerBase
{
    /// <summary>
    /// Lista pública das igrejas cadastradas para a seleção dinâmica de igrejas no cadastro de membros.
    /// Retorna apenas dados públicos necessários, sem vazar informações confidenciais.
    /// </summary>
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> GetPublicList([FromQuery] string? search, CancellationToken cancellationToken)
    {
        var query = db.Churches.AsNoTracking().Where(c => c.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(c => c.Name.ToLower().Contains(term) ||
                                     (c.Denomination != null && c.Denomination.ToLower().Contains(term)));
        }

        var list = await query
            .OrderBy(c => c.Name)
            .Take(50)
            .Select(c => new ChurchPublicDto(
                c.Id,
                c.Name,
                c.Denomination,
                c.LogoUrl,
                c.Address,
                c.Phone
            ))
            .ToListAsync(cancellationToken);

        return Ok(list);
    }

    /// <summary>
    /// Obtém detalhes públicos de uma igreja por ID.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetPublicDetails(int id, CancellationToken cancellationToken)
    {
        var church = await db.Churches.AsNoTracking()
            .Where(c => c.Id == id && c.IsActive)
            .Select(c => new ChurchPublicDto(
                c.Id,
                c.Name,
                c.Denomination,
                c.LogoUrl,
                c.Address,
                c.Phone
            ))
            .FirstOrDefaultAsync(cancellationToken);

        return church is null ? NotFound(new { message = "Igreja não encontrada." }) : Ok(church);
    }
}
