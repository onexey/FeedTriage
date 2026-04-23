using HtmlAgilityPack;
using System.Text;

namespace RssSummarizer.Worker.Utilities;

/// <summary>
/// Converts HTML to plain readable text by stripping tags, scripts, and style blocks.
/// </summary>
public static class HtmlTextExtractor
{
    private static readonly HashSet<string> SkipTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "noscript", "head", "meta", "link", "iframe", "object", "embed"
    };

    /// <summary>
    /// Strips HTML markup and returns readable plain text.
    /// Returns an empty string if input is null or whitespace.
    /// </summary>
    public static string Extract(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var sb = new StringBuilder();
        ExtractText(doc.DocumentNode, sb);

        // Collapse excessive whitespace while preserving paragraph breaks
        var lines = sb.ToString()
            .Split('\n', StringSplitOptions.None)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0);

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Strips HTML and truncates to <paramref name="maxChars"/> characters.
    /// Appends "…" if truncated.
    /// </summary>
    public static string ExtractExcerpt(string? html, int maxChars = 1000)
    {
        var text = Extract(html);
        if (text.Length <= maxChars)
            return text;

        // Truncate at a word boundary
        var truncated = text[..maxChars];
        var lastSpace = truncated.LastIndexOf(' ');
        return (lastSpace > 0 ? truncated[..lastSpace] : truncated) + "…";
    }

    private static void ExtractText(HtmlNode node, StringBuilder sb)
    {
        if (node.NodeType == HtmlNodeType.Document)
        {
            foreach (var child in node.ChildNodes)
                ExtractText(child, sb);

            return;
        }

        if (node.NodeType == HtmlNodeType.Text)
        {
            var text = HtmlEntity.DeEntitize(node.InnerText);
            if (!string.IsNullOrWhiteSpace(text))
            {
                sb.Append(text);
                sb.Append(' ');
            }
            return;
        }

        if (node.NodeType != HtmlNodeType.Element)
            return;

        if (SkipTags.Contains(node.Name))
            return;

        // Add a newline before block-level elements
        if (IsBlockElement(node.Name))
            sb.AppendLine();

        foreach (var child in node.ChildNodes)
            ExtractText(child, sb);

        // Add a newline after block-level elements
        if (IsBlockElement(node.Name))
            sb.AppendLine();
    }

    private static bool IsBlockElement(string tag) =>
        tag is "p" or "div" or "section" or "article" or "header" or "footer"
            or "h1" or "h2" or "h3" or "h4" or "h5" or "h6"
            or "ul" or "ol" or "li" or "blockquote" or "pre" or "br" or "hr"
            or "table" or "tr" or "td" or "th";
}
