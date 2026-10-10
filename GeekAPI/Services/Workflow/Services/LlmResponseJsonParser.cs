using System.Text.Json;
using System.Text.RegularExpressions;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekAPI.Services.Workflow.Services;

/// <summary>Parses JSON-shaped LLM responses with light repair for common local-model mistakes.</summary>
public static class LlmResponseJsonParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    /// <summary>internal (not private) so <see cref="ContentSectionJsonSchema"/> can generate the
    /// provider-facing schema from these exact options — the single source of truth for the real
    /// deserialization contract, so the schema can never silently drift from it.</summary>
    internal static readonly JsonSerializerOptions SectionJsonOptions = CreateSectionJsonOptions();
    private static readonly Regex InlineLinkSyntax = new(@"\[([^\]]*)\]\(([^)]+)\)", RegexOptions.Compiled);

    /// <summary>Stray formatting symbols that should never appear in a plain-text field — see the
    /// content-hygiene validation pass in the design plan: cheap to check because there's no markup
    /// to balance, just stray symbols that shouldn't be there.</summary>
    private static readonly Regex LeakedMarkupSyntax = new(
        @"(\*\*|##+\s|\[[^\]]*\]\([^)]*\)|<[a-zA-Z/][^>]*>)",
        RegexOptions.Compiled);

    private static JsonSerializerOptions CreateSectionJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            // JsonSchemaExporter (used by ContentSectionJsonSchema) requires an explicit
            // TypeInfoResolver — reflection-based resolution isn't picked up implicitly for it.
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        options.Converters.Add(new ParagraphJsonConverter());
        return options;
    }

    /// <summary>
    /// Parses the model's structured Section-tree response. Two-tier validation: JSON/record-type
    /// deserialization is the schema check (a response that doesn't fit the records fails here);
    /// <see cref="ValidateContentHygiene"/> is the second tier, catching stray formatting symbols
    /// a model typed into a plain-text field out of base-model habit.
    /// </summary>
    public static Section ParseSection(string rawContent, string expectedTag, string label)
    {
        string? why = null;

        foreach (var candidate in JsonReplySanitizer.Candidates(rawContent))
        {
            try
            {
                var section = JsonSerializer.Deserialize<Section>(candidate.Text, SectionJsonOptions);
                if (section is not null && !string.IsNullOrWhiteSpace(section.Heading))
                {
                    JsonRepairTrace.Note(label, WithFormattingDrop(candidate.Repairs, HasWriterFormatting(section)));
                    var normalized = Normalize(section) with { Tag = expectedTag };
                    ValidateContentHygiene(normalized, label);
                    return normalized;
                }

                why ??= "the reply parsed but its heading was empty";
            }
            catch (JsonException ex)
            {
                why ??= JsonFault(ex);
            }
        }

        throw Unusable("structured section", label, rawContent, why);
    }

    /// <summary>Parses a top-level sections array (whole-body regeneration/expansion responses).</summary>
    public static IReadOnlyList<Section> ParseSections(string rawContent, string label)
    {
        string? why = null;

        foreach (var candidate in JsonReplySanitizer.Candidates(rawContent))
        {
            try
            {
                // Models frequently drop the {"sections": [...]} wrapper and return the bare array
                // directly when asked for "the sections array" — accept both shapes.
                var isBareArray = candidate.Text.TrimStart().StartsWith('[');
                var sectionsRaw = isBareArray
                    ? JsonSerializer.Deserialize<List<Section>>(candidate.Text, SectionJsonOptions)
                    : JsonSerializer.Deserialize<SectionsArrayResponse>(candidate.Text, SectionJsonOptions)?.Sections;

                if (sectionsRaw is { Count: > 0 } sections)
                {
                    JsonRepairTrace.Note(label, WithFormattingDrop(candidate.Repairs, sections.Any(HasWriterFormatting)));
                    var normalized = sections.Select(Normalize).ToList();
                    foreach (var section in normalized)
                    {
                        ValidateContentHygiene(section, label);
                    }
                    return normalized;
                }

                why ??= "the reply carried no sections";
            }
            catch (JsonException ex)
            {
                why ??= JsonFault(ex);
            }
        }

        throw Unusable("sections array", label, rawContent, why);
    }

    /// <summary>Parses the opening lede: a heading + paragraphs + which pattern was used.</summary>
    public static (Section Lede, LedeType LedeType) ParseLede(string rawContent, string label)
    {
        string? why = null;

        foreach (var candidate in JsonReplySanitizer.Candidates(rawContent))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<LedeResponse>(candidate.Text, SectionJsonOptions);
                // Paragraphs are the acceptance test, not the heading. It was a non-empty heading
                // here -- the same check already corrected in ParseLedeAndIntroduction and missed in
                // this sibling -- so the blog and tool paths rejected every lede for the six days the
                // contract had no heading key. The heading is kept when it is there (see
                // BuildLedeSection) and is not required for the response to be a lede. It was thrown
                // away here, hardcoded to empty, so the Blog and every tool page opened with no H2 of
                // their own while the pillar kept its (2026-10-07).
                if (parsed is not null && parsed.Paragraphs is { Count: > 0 })
                {
                    JsonRepairTrace.Note(label, WithFormattingDrop(candidate.Repairs, HasWriterFormatting(parsed.Paragraphs)));
                    var ledeType = ParseLedeTypeStrict(parsed.LedeType, label);
                    var section = BuildLedeSection(parsed);
                    ValidateContentHygiene(section, label);
                    return (section, ledeType);
                }

                why ??= parsed is null ? "the reply was JSON null" : "the lede carried no paragraphs";
            }
            catch (JsonException ex)
            {
                why ??= JsonFault(ex);
            }
        }

        throw Unusable("lede", label, rawContent, why);
    }

    private sealed record LedeAndIntroductionResponse(LedeResponse? Lede, Section? Introduction);

    /// <summary>Parses the combined lede + Introduction section response (one call covering both,
    /// since they cover overlapping ground) — same candidate-JSON repair loop as <see cref="ParseLede"/>.</summary>
    public static (Section Lede, LedeType LedeType, Section Introduction) ParseLedeAndIntroduction(string rawContent, string label)
    {
        // What went wrong on the first candidate, so the refusal can say it. Without this the message
        // was the first 200 characters and no reason -- and the first 200 characters of a lede look
        // perfectly well formed, because the problem is always further in.
        string? why = null;

        foreach (var candidate in JsonReplySanitizer.Candidates(rawContent))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<LedeAndIntroductionResponse>(candidate.Text, SectionJsonOptions);
                // Paragraphs, not a heading, are what makes this a lede: an opening with a heading
                // and no prose is not an opening. The acceptance test was a non-empty heading until
                // 2026-09-23. The heading itself is kept when present -- see BuildLedeSection.
                if (parsed?.Lede is not { } lede || lede.Paragraphs is not { Count: > 0 })
                {
                    why ??= parsed?.Lede is null
                        ? "the response carried no \"lede\" object"
                        : "the \"lede\" object carried no paragraphs";
                    continue;
                }

                JsonRepairTrace.Note(
                    label,
                    WithFormattingDrop(
                        candidate.Repairs,
                        HasWriterFormatting(lede.Paragraphs)
                            || (lede.Children ?? []).Any(HasWriterFormatting)
                            || (parsed.Introduction is { } opened && HasWriterFormatting(opened))));
                var ledeType = ParseLedeTypeStrict(lede.LedeType, label);
                var ledeSection = BuildLedeSection(lede);
                ValidateContentHygiene(ledeSection, $"{label} (lede)");

                var introduction = parsed.Introduction;
                if (introduction is not null && !string.IsNullOrWhiteSpace(introduction.Heading))
                {
                    // Heading dropped: the introduction continues the lede, under the page title,
                    // and a heading here is the second headline the lede no longer carries either.
                    var introSection = Normalize(introduction) with { Tag = "h2", Heading = string.Empty };
                    ValidateContentHygiene(introSection, $"{label} (introduction)");
                    return (ledeSection, ledeType, introSection);
                }

                // Lede-only or blank intro heading — synthesize intro for downstream merge.
                var introChildren = introduction?.Children ?? lede.Children ?? [];
                var introParagraphs = introduction?.Paragraphs ?? [];
                var syntheticIntro = Normalize(new Section(
                    "h2",
                    string.Empty,
                    introParagraphs,
                    null,
                    introChildren,
                    introduction?.ImagePrompt)) with { Tag = "h2" };
                ValidateContentHygiene(syntheticIntro, $"{label} (introduction)");
                return (ledeSection, ledeType, syntheticIntro);
            }
            catch (JsonException ex)
            {
                why ??= JsonFault(ex);
            }
        }

        throw Unusable("lede+introduction", label, rawContent, why);
    }

    /// <summary>
    /// The lede as this page's first H2. The heading is the model's own -- it used to be
    /// <c>string.Empty</c>, hardcoded, which discarded a heading the prompt's user block was
    /// explicitly asking for ("You write its heading.") and that <c>LedeResponse.Heading</c> had
    /// already deserialized. Jeff, 2026-09-29: "While you are correct normally lede paragraphs have
    /// no heading, in this codebase they do."
    ///
    /// <para>
    /// Discarding it was not cosmetic. <c>GccGenerateService</c>'s revise path reads
    /// <c>document.Lede.Heading</c> as the draft's <c>Title</c>, <c>MetaDescription</c> and
    /// <c>Keywords</c>, and passes it as the topic to <c>BuildMinimalContext</c> -- so a revise of
    /// any freshly generated draft was handed an empty title, an empty meta description and a
    /// one-element keyword list containing the empty string.
    /// </para>
    ///
    /// <para>
    /// Still no fallback and no synthesis: if the model returns no heading the lede carries none,
    /// and the renderers skip a blank heading rather than emitting an empty tag. What is removed is
    /// the discard, not the tolerance.
    /// </para>
    /// </summary>
    private static Section BuildLedeSection(LedeResponse lede) =>
        Normalize(new Section("h2", lede.Heading, lede.Paragraphs ?? [], null, [], lede.ImagePrompt));

    private sealed record SectionsArrayResponse(List<Section>? Sections);

    private sealed record LedeResponse(
        string? LedeType,
        string Heading,
        List<Paragraph>? Paragraphs,
        string? ImagePrompt,
        List<Section>? Children = null);

    private static LedeType ParseLedeTypeStrict(string? raw, string label)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw UnusableReply($"Model returned ledeType null/empty for {label} — expected one of the 12 taxonomy values.");
        var normalized = raw.Trim();
        if (Enum.TryParse<LedeType>(normalized, ignoreCase: true, out var parsed) && Enum.IsDefined(typeof(LedeType), parsed))
            return parsed;
        // Also accept camelCase variants that differ only by case — Enum.TryParse with ignoreCase already handles, but ensure no alias
        throw UnusableReply($"Model returned unknown ledeType '{raw}' for {label} — expected one of: {string.Join(", ", Enum.GetNames<LedeType>())}.");
    }

    /// <summary>
    /// System.Text.Json does not enforce non-null on a record's reference-typed constructor params —
    /// a provider without native schema enforcement (Groq today; see the provider
    /// reality check in the design plan) can omit "children"/"paragraphs"/"runs" or leave a "text"
    /// field null, which would otherwise NRE the first time anything iterates the tree. Normalize
    /// once at the parse boundary so every downstream consumer can trust the non-null invariant.
    /// </summary>
    /// <remarks>
    /// A section's href is not the writer's to set: the contract states it as null, and a heading is
    /// linked only by code after the reply is read. One a reply carries anyway is dropped here, with
    /// the run-level formatting and under the same repair name.
    /// </remarks>
    private static Section Normalize(Section section) => section with
    {
        Heading = section.Heading ?? string.Empty,
        Href = null,
        Paragraphs = (section.Paragraphs ?? []).Select(NormalizeParagraph).ToList(),
        Children = (section.Children ?? []).Select(Normalize).ToList(),
    };

    private static Paragraph NormalizeParagraph(Paragraph paragraph) => paragraph switch
    {
        TextParagraph text => new TextParagraph((text.Runs ?? []).Select(NormalizeRun).ToList()),
        ListParagraph list => new ListParagraph(
            list.Ordered,
            (list.Items ?? []).Select(item => (IReadOnlyList<Run>)(item ?? []).Select(NormalizeRun).ToList()).ToList()),
        QuoteParagraph quote => quote with { Runs = (quote.Runs ?? []).Select(NormalizeRun).ToList() },
        _ => paragraph,
    };

    /// <summary>
    /// A run is its text. Bold and italic are not the writer's to set: nothing in any prompt
    /// asks for them, and the writer copied a linked name's bold onto the runs around it (Bill's and Ramp's
    /// openings, and the Blog's, whose copy took the link too and cost the page, 2026-10-07). The contract no
    /// longer offers them; one a reply carries anyway is dropped here and recorded as
    /// <see cref="DropWriterFormattingRepair"/>.
    /// </summary>
    /// <remarks>
    /// An href is not the writer's to set either (Jeff, 2026-10-10): the contract offers a run its text
    /// and nothing else, and a link is put on a partner tool's name by <c>GccToolLinker</c> after this.
    /// One a reply carries anyway is dropped here with the bold and italic, under the same repair name.
    /// </remarks>
    private static Run NormalizeRun(Run run) =>
        run with { Text = run.Text ?? string.Empty, Bold = false, Italic = false, Href = null };

    private const string DropWriterFormattingRepair = "drop-writer-formatting";

    private static IReadOnlyList<string> WithFormattingDrop(IReadOnlyList<string> repairs, bool formattingDropped) =>
        formattingDropped ? [.. repairs, DropWriterFormattingRepair] : repairs;

    private static bool HasWriterFormatting(Section section) =>
        !string.IsNullOrWhiteSpace(section.Href)
        || HasWriterFormatting(section.Paragraphs)
        || (section.Children ?? []).Any(HasWriterFormatting);

    private static bool HasWriterFormatting(IEnumerable<Paragraph>? paragraphs) =>
        (paragraphs ?? []).Any(paragraph => RunsOf(paragraph).Any(
            run => run.Bold || run.Italic || !string.IsNullOrWhiteSpace(run.Href)));

    private static IEnumerable<Run> RunsOf(Paragraph paragraph) => paragraph switch
    {
        TextParagraph text => text.Runs ?? [],
        ListParagraph list => (list.Items ?? []).SelectMany(item => item ?? []),
        QuoteParagraph quote => quote.Runs ?? [],
        _ => [],
    };

    private static void ValidateContentHygiene(Section section, string label)
    {
        CheckText(section.Heading, label);
        foreach (var paragraph in section.Paragraphs)
        {
            // Quote runs too. They were skipped (`_ => []`), so "[Source: Bill](https://...)" or
            // "**bold**" inside a block quotation passed hygiene on every type -- and on pillar and
            // blog no quote guard runs afterwards, so it would have rendered as literal brackets in
            // a <blockquote>. A quotation is plain text like everything else the model returns.
            var runs = paragraph switch
            {
                TextParagraph text => text.Runs,
                ListParagraph list => list.Items.SelectMany(item => item).ToList(),
                QuoteParagraph quote => quote.Runs,
                _ => [],
            };
            foreach (var run in runs)
            {
                CheckText(run.Text, label);
            }
        }
        foreach (var child in section.Children)
        {
            ValidateContentHygiene(child, label);
        }
    }

    private static void CheckText(string text, string label)
    {
        // There used to be an early return here for any text containing a literal
        // `<a href="/tools/` -- "permitted ... per prompt". No live prompt asks for a literal anchor
        // (a tool link is put on the tool's name by code), and the exemption did not merely admit that one tag: it
        // skipped every check below for the whole run, so a run carrying the anchor could also carry
        // "[Source: x](url)" and "**bold**" and ship them all as literal text. The renderer encodes
        // run text, so the "permitted" anchor would have been published as escaped markup anyway.
        // An image prompt written into the prose. The field was removed from the lede contract on
        // 2026-09-23 because the model kept answering it as a paragraph -- "Image prompt: A
        // conceptual image of a futuristic office..." sitting in the body of a published draft.
        // Prompt changes are not a guarantee, so this fails the generation rather than shipping it.
        if (text.TrimStart().StartsWith("Image prompt", StringComparison.OrdinalIgnoreCase))
        {
            throw UnusableReply(
                $"Model wrote an image prompt into the prose for {label}: \"{text[..Math.Min(80, text.Length)]}\". " +
                "Image prompts are produced by their own call and shipped as separate files, never as page text.");
        }
        if (LeakedMarkupSyntax.IsMatch(text))
        {
            throw UnusableReply(
                $"Model typed stray formatting symbols into a plain-text field for {label}: \"{text}\". " +
                "A run is plain text only.");
        }
    }

    public static T Parse<T>(string rawContent, string label) =>
        Parse<T>(rawContent, label, "JSON object", static _ => null);

    /// <summary>
    /// Parses a reply into <typeparamref name="T"/> and refuses one that parses but is not usable.
    /// <paramref name="unacceptable"/> returns the reason a parsed reply cannot be used (a field the call
    /// requires is blank), or null when it can. It is part of the candidate loop, as the other parse
    /// methods' acceptance tests are, so the refusal carries the same fields whichever way a reply fails.
    /// </summary>
    public static T Parse<T>(string rawContent, string label, string what, Func<T, string?> unacceptable)
    {
        string? why = null;

        foreach (var candidate in JsonReplySanitizer.Candidates(rawContent))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<T>(candidate.Text, JsonOptions);
                if (parsed is null)
                {
                    why ??= "the reply was JSON null";
                    continue;
                }

                var problem = unacceptable(parsed);
                if (problem is not null)
                {
                    why ??= problem;
                    continue;
                }

                JsonRepairTrace.Note(label, candidate.Repairs);
                return parsed;
            }
            catch (JsonException ex)
            {
                why ??= JsonFault(ex);
            }
        }

        throw Unusable(what, label, rawContent, why);
    }

    public static string ParseSocialText(string rawContent, string articleUrl, string label)
    {
        string? why = null;

        foreach (var candidate in JsonReplySanitizer.Candidates(rawContent))
        {
            if (TryDeserializeSocial(candidate.Text, out var text, out var socialWhy))
            {
                JsonRepairTrace.Note(label, candidate.Repairs);
                return NormalizeSocialText(text, articleUrl);
            }

            why ??= socialWhy;
        }

        var unfenced = JsonReplySanitizer.StripCodeFence(rawContent);
        var strictMatch = Regex.Match(unfenced, @"""text""\s*:\s*""((?:\\.|[^""\\])*)""", RegexOptions.Singleline);
        if (strictMatch.Success)
        {
            JsonRepairTrace.Note(label, [SalvageTextFieldRepair]);
            return NormalizeSocialText(UnescapeJsonString(strictMatch.Groups[1].Value), articleUrl);
        }

        // Truncated or broken JSON — salvage the text field and ensure the link is present.
        var salvageMatch = Regex.Match(unfenced, @"""text""\s*:\s*""(.*)", RegexOptions.Singleline);
        if (salvageMatch.Success)
        {
            var salvaged = UnescapeJsonString(salvageMatch.Groups[1].Value.TrimEnd('"', ' ', '\r', '\n', '}'));
            if (salvaged.Length > 0)
            {
                JsonRepairTrace.Note(label, [SalvageTextFieldRepair]);
                return NormalizeSocialText(salvaged, articleUrl);
            }
        }

        throw Unusable("social post", label, rawContent, why);
    }

    /// <summary>
    /// Named so the run's record shows it. The two <c>text</c>-field patterns in <see cref="ParseSocialText"/>
    /// predate the sanitiser and are left as they were; what changed is that using one is no longer silent.
    /// </summary>
    private const string SalvageTextFieldRepair = "salvage-text-field";

    public static ColdOutreachEmailDraft ParseColdOutreach(string rawContent, string label)
    {
        string? why = null;

        foreach (var candidate in JsonReplySanitizer.Candidates(rawContent))
        {
            if (TryDeserializeColdOutreach(candidate.Text, out var draft, out var outreachWhy))
            {
                JsonRepairTrace.Note(label, candidate.Repairs);
                return ValidateColdOutreach(draft, label);
            }

            why ??= outreachWhy;
        }

        throw Unusable("cold-outreach email", label, rawContent, why);
    }

    public static ImagePromptSectionPromptsDraft ParseSectionImagePrompts(
        string rawContent,
        IReadOnlyList<ImagePromptSectionTarget> expectedSections,
        string label)
    {
        string? why = null;

        foreach (var candidate in JsonReplySanitizer.Candidates(rawContent))
        {
            if (TryDeserializeSectionImagePrompts(candidate.Text, out var draft, out var promptsWhy))
            {
                JsonRepairTrace.Note(label, candidate.Repairs);
                return ValidateSectionImagePrompts(draft, expectedSections, label);
            }

            why ??= promptsWhy;
        }

        throw Unusable("section image prompts", label, rawContent, why);
    }

    private static bool TryDeserializeSectionImagePrompts(string json, out ImagePromptSectionPromptsDraft draft, out string? why)
    {
        draft = new ImagePromptSectionPromptsDraft([]);
        why = null;

        try
        {
            var parsed = JsonSerializer.Deserialize<ImagePromptSectionsResponse>(json, JsonOptions);
            if (parsed?.Sections is null || parsed.Sections.Count == 0)
            {
                why = "the reply carried no sections";
                return false;
            }

            draft = new ImagePromptSectionPromptsDraft(
                parsed.Sections.Select(ToSectionDraft).ToList());
            return true;
        }
        catch (JsonException ex)
        {
            why = JsonFault(ex);
            return false;
        }
    }

    private static ImagePromptSectionDraft ToSectionDraft(ImagePromptSectionResponse item) =>
        new(
            (item.SourceType ?? "").Trim().ToLowerInvariant(),
            (item.Heading ?? "").Trim(),
            item.Order,
            (item.Prompt ?? "").Trim(),
            item.Width,
            item.Height,
            (item.ImageModel ?? "").Trim(),
            (item.StylePreset ?? "").Trim(),
            item.Alchemy ?? true,
            item.PhotoReal ?? false,
            string.IsNullOrWhiteSpace(item.Notes) ? null : item.Notes.Trim());

    private static ImagePromptSectionPromptsDraft ValidateSectionImagePrompts(
        ImagePromptSectionPromptsDraft draft,
        IReadOnlyList<ImagePromptSectionTarget> expectedSections,
        string label)
    {
        // Match by (sourceType, heading, order) — the model may return extra sections when only
        // one target was requested (common for spawned image-prompt jobs).
        var matched = new List<ImagePromptSectionDraft>(expectedSections.Count);

        for (var i = 0; i < expectedSections.Count; i++)
        {
            var expected = expectedSections[i];
            var item = draft.Sections.FirstOrDefault(s =>
                string.Equals(s.SourceType, expected.SourceType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.Heading, expected.Heading, StringComparison.OrdinalIgnoreCase)
                && s.Order == expected.Order);

            if (item is null)
            {
                throw UnusableReply(
                    $"{label} is missing a prompt for {expected.SourceType} section \"{expected.Heading}\" (order {expected.Order}).");
            }

            ValidateSectionImagePromptItem(item, expected, label);
            matched.Add(item);
        }

        return new ImagePromptSectionPromptsDraft(matched);
    }

    private static void ValidateSectionImagePromptItem(
        ImagePromptSectionDraft item,
        ImagePromptSectionTarget expected,
        string label)
    {
        if (string.IsNullOrWhiteSpace(item.Prompt))
        {
            throw UnusableReply(
                $"Model returned empty prompt for {expected.SourceType} section \"{expected.Heading}\" in {label}.");
        }

        // Word-count range is advisory only (soft gate) — out-of-range prompts still save so the
        // user can copy/edit them. Empty prompt and invalid dimensions/model remain hard failures.

        if (item.Width < 512 || item.Height < 512 || item.Width > 2048 || item.Height > 2048)
        {
            throw UnusableReply($"Dimensions for \"{expected.Heading}\" are out of range (512–2048).");
        }

        if (item.Width < item.Height)
        {
            throw UnusableReply($"Prompt for \"{expected.Heading}\" should be landscape (width >= height).");
        }

        if (string.IsNullOrWhiteSpace(item.ImageModel))
        {
            throw UnusableReply($"Model returned empty imageModel for \"{expected.Heading}\" in {label}.");
        }

        if (string.IsNullOrWhiteSpace(item.StylePreset))
        {
            throw UnusableReply($"Model returned empty stylePreset for \"{expected.Heading}\" in {label}.");
        }
    }

    private static bool TryDeserializeColdOutreach(string json, out ColdOutreachEmailDraft draft, out string? why)
    {
        draft = new ColdOutreachEmailDraft("", "", "");
        why = null;
        try
        {
            var parsed = JsonSerializer.Deserialize<ColdOutreachResponse>(json, JsonOptions);
            if (parsed is null)
            {
                why = "the reply was JSON null";
                return false;
            }

            draft = new ColdOutreachEmailDraft(
                (parsed.Subject ?? "").Trim(),
                (parsed.BodyText ?? "").Trim(),
                (parsed.CtaLabel ?? "").Trim());
            return true;
        }
        catch (JsonException ex)
        {
            why = JsonFault(ex);
            return false;
        }
    }

    private static ColdOutreachEmailDraft ValidateColdOutreach(ColdOutreachEmailDraft draft, string label)
    {
        if (string.IsNullOrWhiteSpace(draft.Subject))
        {
            throw UnusableReply($"Model returned empty subject for {label}.");
        }

        if (string.IsNullOrWhiteSpace(draft.BodyText))
        {
            throw UnusableReply($"Model returned empty body for {label}.");
        }

        if (string.IsNullOrWhiteSpace(draft.CtaLabel))
        {
            throw UnusableReply($"Model returned empty ctaLabel for {label}.");
        }

        // Word-count range is advisory only (soft gate, same as blog/tools) — out-of-range
        // bodies still parse/save so the UI can show an amber badge and the user can decide.
        return draft;
    }

    private static bool TryDeserializeSocial(string json, out string text, out string? why)
    {
        text = string.Empty;
        why = null;
        try
        {
            var parsed = JsonSerializer.Deserialize<SocialTextResponse>(json, JsonOptions);
            if (string.IsNullOrWhiteSpace(parsed?.Text))
            {
                why = "the reply carried no text";
                return false;
            }

            text = parsed.Text;
            return true;
        }
        catch (JsonException ex)
        {
            why = JsonFault(ex);
            return false;
        }
    }

    /// <summary>
    /// The one refusal for a reply that could not be used. Every parse method ends here, so the Run log
    /// reads the same whichever call it was: what was being parsed, why the first candidate failed, which
    /// named repairs were tried, how long the reply was, and the start of it.
    /// </summary>
    /// <remarks>
    /// Marked <see cref="ContentGenerationFailureKind.UnusableReply"/>: the model's answer, not a fault in
    /// the code, so the run's record shows it as a reply to read rather than a stack to chase. The
    /// truncation hint is judged on the reply as written (fence stripped), never on a repaired candidate.
    /// </remarks>
    private static ContentGenerationException Unusable(string what, string label, string rawContent, string? why)
    {
        var reply = rawContent ?? string.Empty;
        var reason = string.IsNullOrWhiteSpace(reply)
            ? "the reply was empty"
            : why ?? "no candidate could be read";

        var unfenced = JsonReplySanitizer.StripCodeFence(reply);
        var looksTruncated = unfenced.Length > 0 && !unfenced.EndsWith('}') && !unfenced.EndsWith(']');
        var hint = looksTruncated
            ? " The reply does not end with a closing brace or bracket, so it was cut off -- most likely the max output token limit."
            : string.Empty;

        return new ContentGenerationException(
            $"Model did not return a valid {what} for {label}. "
            + $"Reason: {reason}. "
            + $"Repairs tried: {JsonReplySanitizer.RepairsTried(reply)}. "
            + $"Reply was {reply.Length} chars; first 200: {reply[..Math.Min(200, reply.Length)]}.{hint}")
        {
            Kind = ContentGenerationFailureKind.UnusableReply,
        };
    }

    /// <summary>
    /// A model's content that broke a rule the call stated, as the same kind of failure as an unreadable reply.
    /// Internal so a caller that checks a parsed reply's fields refuses it the way the parser does.
    /// </summary>
    internal static ContentGenerationException UnusableReply(string message) =>
        new(message) { Kind = ContentGenerationFailureKind.UnusableReply };

    private static string JsonFault(JsonException ex) =>
        $"JSON error at byte {ex.BytePositionInLine} (path {ex.Path}): {ex.Message}";

    private static string UnescapeJsonString(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<string>($"\"{value}\"") ?? value;
        }
        catch (JsonException)
        {
            return value.Replace("\\n", "\n", StringComparison.Ordinal)
                .Replace("\\\"", "\"", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal);
        }
    }

    private static string NormalizeSocialText(string text, string articleUrl)
    {
        text = InlineLinkSyntax.Replace(text, "$2").Trim();
        if (!text.Contains(articleUrl, StringComparison.OrdinalIgnoreCase))
        {
            text = $"{text.TrimEnd()} {articleUrl}".Trim();
        }

        return text;
    }

    private sealed record SocialTextResponse(string Text);

    private sealed record ColdOutreachResponse(string? Subject, string? BodyText, string? CtaLabel);

    private sealed record ImagePromptSectionsResponse(IReadOnlyList<ImagePromptSectionResponse>? Sections);

    private sealed record ImagePromptSectionResponse(
        string? SourceType,
        string? Heading,
        int Order,
        string? Prompt,
        int Width,
        int Height,
        string? ImageModel,
        string? StylePreset,
        bool? Alchemy,
        bool? PhotoReal,
        string? Notes);
}
