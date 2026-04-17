using AutoHPMA.Models;

namespace AutoHPMA.Contracts.Services;

public interface IUpdateService
{
    Task CheckUpdateAsync(UpdateOption option);
}
