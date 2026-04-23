using RssSummarizer.Worker.Utilities;
using Xunit;

namespace RssSummarizer.Tests;

public sealed class HtmlTextExtractorTests
{
    [Fact]
    public void Extract_ReturnsEmptyString_ForNullInput()
    {
        var result = HtmlTextExtractor.Extract(null);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Extract_ReturnsEmptyString_ForWhitespaceInput()
    {
        var result = HtmlTextExtractor.Extract("   ");
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Extract_StripsTags_AndReturnsText()
    {
        var html = "<p>Hello <strong>world</strong></p>";
        var result = HtmlTextExtractor.Extract(html);
        Assert.Contains("Hello", result);
        Assert.Contains("world", result);
        Assert.DoesNotContain("<", result);
    }

    [Fact]
    public void Extract_RemovesScriptContent()
    {
        var html = "<p>Readable</p><script>var x = 1;</script>";
        var result = HtmlTextExtractor.Extract(html);
        Assert.Contains("Readable", result);
        Assert.DoesNotContain("var x", result);
    }

    [Fact]
    public void Extract_RemovesStyleContent()
    {
        var html = "<style>body { color: red; }</style><p>Content</p>";
        var result = HtmlTextExtractor.Extract(html);
        Assert.Contains("Content", result);
        Assert.DoesNotContain("color", result);
    }

    [Fact]
    public void Extract_DecodesHtmlEntities()
    {
        var html = "<p>Hello &amp; world &lt;3&gt;</p>";
        var result = HtmlTextExtractor.Extract(html);
        Assert.Contains("&", result);
    }

    [Fact]
    public void ExtractExcerpt_TruncatesAtMaxChars()
    {
        var html = "<p>" + new string('a', 2000) + "</p>";
        var result = HtmlTextExtractor.ExtractExcerpt(html, maxChars: 100);
        Assert.True(result.Length <= 105, $"Excerpt too long: {result.Length}");
        Assert.EndsWith("…", result);
    }

    [Fact]
    public void ExtractExcerpt_DoesNotTruncate_WhenContentFits()
    {
        var html = "<p>Short text</p>";
        var result = HtmlTextExtractor.ExtractExcerpt(html, maxChars: 1000);
        Assert.DoesNotContain("…", result);
    }
}
