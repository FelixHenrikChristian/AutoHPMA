using AutoHPMA.Core.Models;

namespace AutoHPMA.Core.Contracts.Services;

public interface IWindowInteractionService
{
    Task ExecuteAsync(IntPtr hWnd, MouseActionOptions options, CancellationToken cancellationToken = default);

    bool TrySetForegroundWindow(IntPtr hWnd);
}
