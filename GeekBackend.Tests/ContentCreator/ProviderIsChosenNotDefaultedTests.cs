using GeekAPI.Controllers.ContentCreator;
using GeekApplication.Interfaces.ContentWriterV3;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The requested provider is parsed strictly, or refused — never defaulted.
/// </summary>
/// <remarks>
/// Since 764b139 the version's metadata records which provider wrote it, and that record is what the
/// workspace shows to tell two drafts apart. A silently defaulted provider is therefore not a harmless
/// convenience: it is asserted afterwards as a choice the operator made.
/// </remarks>
public class ProviderIsChosenNotDefaultedTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_absent_provider_is_refused_rather_than_becoming_OpenAi(string? raw)
    {
        // Every live caller sends one. Absence is a bug in a caller, not a case to absorb --
        // .claude/CLAUDE.md §2, "No Auto-Repair/Defaults".
        Assert.False(GccController.TryParseProvider(raw, out _, out var error));
        Assert.Contains("No provider was specified", error!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("7")]
    [InlineData("99")]
    [InlineData("-1")]
    public void A_numeric_string_is_refused_even_though_Enum_TryParse_accepts_it(string raw)
    {
        // Enum.TryParse returns TRUE for numeric strings whether or not the value is defined, so
        // provider "7" parsed successfully into an undefined ContentGeneratorProvider -- which ToLlm
        // then mapped to OpenAi, because its test is `== Anthropic`. Enum.IsDefined is what closes it.
        Assert.False(GccController.TryParseProvider(raw, out _, out var error));
        Assert.Contains($"Unknown provider '{raw}'", error!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("OpenAi", ContentGeneratorProvider.OpenAi)]
    [InlineData("openai", ContentGeneratorProvider.OpenAi)]
    [InlineData("Anthropic", ContentGeneratorProvider.Anthropic)]
    [InlineData("ANTHROPIC", ContentGeneratorProvider.Anthropic)]
    public void A_named_provider_parses_case_insensitively(string raw, ContentGeneratorProvider expected)
    {
        // The picker emits exactly "OpenAi"/"Anthropic"; case-insensitivity is for hand-made requests.
        Assert.True(GccController.TryParseProvider(raw, out var provider, out var error));
        Assert.Equal(expected, provider);
        Assert.Null(error);
    }

    [Fact]
    public void An_unknown_name_names_what_is_valid()
    {
        Assert.False(GccController.TryParseProvider("Gemini", out _, out var error));
        Assert.Contains("OpenAi", error!, StringComparison.Ordinal);
        Assert.Contains("Anthropic", error!, StringComparison.Ordinal);
    }
}
