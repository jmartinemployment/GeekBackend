using System.Text.Json;
using GeekAPI.Services.ContentCreatorV2.BrandKit;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Providers;

namespace GeekAPI.Services.ContentCreatorV2.Carousel;

public static class GccV2LinkedInCarouselPromptBuilder
{
    public static ChatCompletionRequest BuildRequest(
        ContentDocument sourceDocument,
        string sourceTitle,
        string? targetKeyword,
        GccV2BrandKitContent? brandKit)
    {
        var sourceJson = JsonSerializer.Serialize(sourceDocument, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var voice = brandKit?.VoiceGuidance.Count > 0
            ? string.Join("; ", brandKit.VoiceGuidance.Take(3))
            : "Direct, practical, consultant-grade — no hype.";
        var company = brandKit?.CompanyName ?? "the author";
        var keyword = string.IsNullOrWhiteSpace(targetKeyword) ? sourceTitle : targetKeyword.Trim();

        var userBrief = $$"""
            Turn the source long-form content below into a LinkedIn document carousel (PDF upload).
            One practical idea only — teach it fast with a personal POV or real client lesson.

            Requirements:
            - Output exactly 8–10 slides in JSON (see schema below).
            - Slide roles: cover (hook), problem, 4–6 teach slides (one tactical insight each), framework (before/after or mini-playbook), cta (soft CTA + takeaway).
            - Each teach slide: 2–4 short bullets (max 12 words each).
            - Cover: bold hook title + subtitle.
            - Caption: 150–250 words for the feed post that accompanies the PDF upload. Start with a hook line. End with a conversation starter question.
            - 3–5 hashtags (no # in values).
            - suggestedFilename: professional snake_case name (e.g. AI_Implementation_Framework) — never Draft_v4_final.
            - Topic clarity: {{keyword}}
            - Voice: {{voice}}
            - Company/author context: {{company}}

            Reply with valid JSON only:
            {
              "slides": [
                { "role": "cover|problem|teach|framework|cta", "title": "string", "subtitle": "string|null", "bullets": ["string"] }
              ],
              "caption": "string",
              "hashtags": ["string"],
              "suggestedFilename": "string"
            }

            Source title: {{sourceTitle}}

            Source document:
            {{sourceJson}}
            """;

        return new ChatCompletionRequest(
            Messages:
            [
                new ChatMessage(
                    ChatRole.System,
                    "You create LinkedIn document carousel slide decks as strict JSON only. Each slide teaches one idea. Professional, conversational, no filler."),
                new ChatMessage(ChatRole.User, userBrief),
            ],
            Temperature: 0.45,
            MaxOutputTokens: 8192);
    }
}
