using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace LogicLab.Web.Tests;

internal static class WebTestMarkup
{
    public static IDocument Parse(string html) =>
        new HtmlParser().ParseDocument(html);

    public static IElement RequireElement(IDocument document, string selector) =>
        document.QuerySelector(selector)
            ?? throw new InvalidOperationException(
                $"Markup did not contain an element matching {selector}.");
}
