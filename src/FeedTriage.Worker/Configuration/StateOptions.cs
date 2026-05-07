namespace FeedTriage.Worker.Configuration;

public sealed class StateOptions
{
    public const string SectionName = "FeedTriage:State";
    private const string LocalDefaultFilePath = "./data/state.json";
    private const string ContainerDefaultFilePath = "/data/state.json";

    public StateOptions()
    {
        FilePath = ResolveDefaultFilePath(
            Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"));
    }

    /// <summary>
    /// Path to the JSON file used to persist run state (last processed entry ID).
    /// Relative paths are resolved from the current working directory.
    /// Defaults to "./data/state.json" locally and "/data/state.json" inside containers.
    /// </summary>
    public string FilePath { get; set; }

    public static string ResolveDefaultFilePath(string? dotnetRunningInContainer)
    {
        return string.Equals(dotnetRunningInContainer, "true", StringComparison.OrdinalIgnoreCase)
            ? ContainerDefaultFilePath
            : LocalDefaultFilePath;
    }
}
