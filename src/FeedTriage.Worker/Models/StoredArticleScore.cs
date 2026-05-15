namespace FeedTriage.Worker.Models;

public sealed class StoredArticleScore
{
    public required DateOnly ScoreDate { get; init; }
    public required long EntryId { get; init; }
    public required string Title { get; init; }
    public required string Url { get; init; }
    public required int TotalScore { get; init; }
    public required IReadOnlyDictionary<string, int> TopicScores { get; init; }
}
