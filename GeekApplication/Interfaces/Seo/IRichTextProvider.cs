namespace GeekApplication.Interfaces.Seo;

public interface IRichTextProvider
{
    string ExtractPlainText(string html);
    int CountWords(string html);
    string ProviderName { get; }
}
