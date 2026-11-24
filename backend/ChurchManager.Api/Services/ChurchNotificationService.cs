using ChurchManager.Api.Hubs;
using ChurchManager.Api.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace ChurchManager.Api.Services;

public sealed class ChurchNotificationService(IHubContext<ChurchHub> hubContext) : IChurchNotificationService
{
    public async Task NotifyEntityChangedAsync(int churchId, string resource, string action, object? data = null, bool adminOnly = false)
    {
        var groupName = adminOnly ? $"church_{churchId}_admin" : $"church_{churchId}";
        await hubContext.Clients.Group(groupName).SendAsync("EntityChanged", new
        {
            churchId,
            resource,
            action,
            data,
            timestamp = DateTime.UtcNow
        });
    }
}
