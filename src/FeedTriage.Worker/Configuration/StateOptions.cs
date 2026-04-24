namespace FeedTriage.Worker.Configuration;

public sealed class StateOptions
{
    public const string SectionName = "FeedTriage:State";

    /// <summary>
    /// Path to the JSON file used to persist run state (last processed entry ID).
    /// Relative paths are resolved from the current working directory.
    /// Defaults to "./data/state.json" so container runs can persist state via the mounted data volume.
    /// </summary>
    public string FilePath { get; set; } = "./data/state.json";
}
