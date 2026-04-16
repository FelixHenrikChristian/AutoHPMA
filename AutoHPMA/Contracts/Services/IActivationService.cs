namespace AutoHPMA.Contracts.Services;

public interface IActivationService
{
    Task ActivateAsync(object activationArgs);
}
