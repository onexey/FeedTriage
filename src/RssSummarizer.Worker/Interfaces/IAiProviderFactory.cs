namespace RssSummarizer.Worker.Interfaces;

/// <summary>
/// Resolves <see cref="IAiProvider"/> instances by their configured name.
/// </summary>
public interface IAiProviderFactory
{
    /// <summary>
    /// Returns the provider for <paramref name="instanceName"/>, or null if the
    /// name is not found in configuration or the provider type is unsupported.
    /// </summary>
    IAiProvider? Get(string instanceName);
}
