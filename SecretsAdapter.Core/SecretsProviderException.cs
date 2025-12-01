namespace Neillans.Adapters.Secrets.Core;

/// <summary>
/// Exception thrown when a secrets provider operation fails.
/// </summary>
public class SecretsProviderException : Exception
{
    public SecretsProviderException()
    {
    }

    public SecretsProviderException(string message) : base(message)
    {
    }

    public SecretsProviderException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
