namespace FeedTriage.Worker.Configuration;

public sealed class StateOptions
{
    public const string SectionName = "FeedTriage:State";
    private const string LocalDefaultFilePath = "./data/state.json";
    private const string ContainerDefaultFilePath = "/data/state.json";

    /// <summary>
    /// Path to the JSON file used to persist run state (last processed entry ID).
    /// Relative paths are resolved from the current working directory.
    /// Defaults to "./data/state.json" locally and "/data/state.json" inside containers.
    /// </summary>
    public string FilePath { get; set; } = IsRunningInContainer()
        ? ContainerDefaultFilePath
        : LocalDefaultFilePath;

    private static bool IsRunningInContainer()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }
}
