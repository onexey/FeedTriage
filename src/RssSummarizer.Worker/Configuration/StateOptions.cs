namespace RssSummarizer.Worker.Configuration;

public sealed class StateOptions
{
    public const string SectionName = "RssSummarizer:State";

    /// <summary>
    /// Path to the JSON file used to persist run state (last processed entry ID).
    /// Relative paths are resolved from the current working directory.
    /// Defaults to "state.json" next to the binary.
    /// </summary>
    public string FilePath { get; set; } = "state.json";
}
