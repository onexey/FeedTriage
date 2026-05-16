using Microsoft.Extensions.Logging;

namespace FeedTriage.Worker.Configuration;

public sealed class AppLoggingOptions
{
    public const string SectionName = "FeedTriage:Logging";

    public string Level { get; set; } = "Information";

    public static bool TryParseLevel(string? value, out LogLevel level)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            level = default;
            return false;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "information":
            case "info":
                level = LogLevel.Information;
                return true;
            case "verbose":
            case "debug":
                level = LogLevel.Debug;
                return true;
            case "trace":
                level = LogLevel.Trace;
                return true;
            case "warning":
            case "warn":
                level = LogLevel.Warning;
                return true;
            case "error":
                level = LogLevel.Error;
                return true;
            case "critical":
            case "fatal":
                level = LogLevel.Critical;
                return true;
            case "none":
                level = LogLevel.None;
                return true;
            default:
                level = default;
                return false;
        }
    }
}
