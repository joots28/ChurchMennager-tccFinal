namespace ChurchManager.Api.Interfaces;

public interface IChurchNotificationService
{
    Task NotifyEntityChangedAsync(int churchId, string resource, string action, object? data = null, bool adminOnly = false);
}
