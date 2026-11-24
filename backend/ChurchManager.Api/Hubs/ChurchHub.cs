using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ChurchManager.Api.Hubs;

[Authorize]
public sealed class ChurchHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var churchId = Context.User?.FindFirstValue("church_id");
        if (string.IsNullOrWhiteSpace(churchId))
        {
            Context.Abort();
            return;
        }

        // Adiciona a conexão ao grupo isolado da igreja
        await Groups.AddToGroupAsync(Context.ConnectionId, $"church_{churchId}");

        var role = Context.User?.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        if (string.Equals(role, "Administrador", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Igreja", StringComparison.OrdinalIgnoreCase))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"church_{churchId}_admin");
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var churchId = Context.User?.FindFirstValue("church_id");
        if (!string.IsNullOrWhiteSpace(churchId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"church_{churchId}");
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"church_{churchId}_admin");
        }

        await base.OnDisconnectedAsync(exception);
    }
}
