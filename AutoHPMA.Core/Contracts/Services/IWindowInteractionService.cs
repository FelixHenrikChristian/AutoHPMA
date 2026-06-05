using AutoHPMA.Core.Models;

namespace AutoHPMA.Core.Contracts.Services;

public interface IWindowInteractionService
{
    Task ExecuteAsync(IntPtr hWnd, MouseActionOptions options, CancellationToken cancellationToken = default);

    Task SendKeyAsync(IntPtr hWnd, int virtualKey, CancellationToken cancellationToken = default);

    bool TrySetForegroundWindow(IntPtr hWnd);
}
