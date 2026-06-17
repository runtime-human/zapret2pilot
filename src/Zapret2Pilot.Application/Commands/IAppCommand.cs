namespace Zapret2Pilot.Application.Commands;

/// <summary>
/// Marker interface for application commands.
/// </summary>
/// <typeparam name="TResponse">Command response type.</typeparam>
public interface IAppCommand<TResponse>
    where TResponse : notnull
{
}
