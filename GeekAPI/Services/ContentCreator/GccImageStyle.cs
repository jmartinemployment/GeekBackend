using GeekAPI.Services.Workflow.Services;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// What every image prompt the Content Creator writes has in common: the brand palette, the two
/// backgrounds a scene may sit on, and the closing sentences the code appends. The model writes the
/// scene and never the palette, so the hexes are exact on every prompt. The colours are the site's
/// (geek-style-guide.html); the wording is from the two prompts the owner supplied on 2026-11-04.
/// </summary>
public static class GccImageStyle
{
    public const string Navy = "#0B162A";
    public const string Orange = "#C83803";
    public const string Teak = "#8C4E2A";

    public const string TransparentBackground = "a transparent background";
    public const string NavyBackground = $"a solid dark navy background ({Navy})";

    /// <summary>Appended to every scene by <see cref="Complete"/>. Not shown to the model to write.</summary>
    public const string Closing =
        $"The scene uses bold, minimal shapes with deep, crisp shadows and striking accents of vibrant brand orange ({Orange}) "
        + $"and rich brand teak ({Teak}). The lighting is sharp and graphic with smooth gradients, a professional but "
        + "approachable tech aesthetic. No text or labels.";

    /// <summary>
    /// The rules for the scene the model writes, shared by the per-section call and the single-picture
    /// call so the two cannot drift. Plain sentences; nothing here is markup.
    /// </summary>
    public static string SceneRules() =>
        "SCENE (what you write, one paragraph per prompt, "
        + $"{ImagePromptDefaults.AskedMinWords} to {ImagePromptDefaults.AskedMaxWords} words):\n"
        + $"- Open with: \"A clean, flat-design 3D vector illustration of ...\" and set it against either {TransparentBackground} "
        + $"or {NavyBackground}.\n"
        + "- Name the concrete objects and the setting that the text you are given is about. Show its point as one bold visual "
        + "metaphor, such as an hourglass leaking coins for lost time.\n"
        + "- Take the people and the place from the audience lines when they are given. Never invent a place or a business "
        + "the audience lines do not give.\n"
        + "- Never put words in the picture. Do not quote a label, a heading or a document name to be drawn; show a document "
        + "or a category as a colour and a shape.\n"
        + "- No screenshots, dashboards or software screens (image models invent them), no logos, no watermarks.\n"
        + "- Two prompts in one reply must not share a composition.\n"
        + "- Do not write the palette, the lighting or the words \"No text or labels\". They are added after your scene.";

    /// <summary>
    /// A scene with the closing appended, or a refusal when the scene is outside the band. A scene that
    /// is a few words or a wall of text is not an image prompt, and nothing fixes it silently.
    /// </summary>
    public static string Complete(string? scene, string where)
    {
        var text = (scene ?? string.Empty).Trim();
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        if (words < ImagePromptDefaults.SceneMinWords || words > ImagePromptDefaults.SceneMaxWords)
        {
            throw LlmResponseJsonParser.UnusableReply(
                $"The image prompt for {where} is {words} words; a scene is {ImagePromptDefaults.SceneMinWords} to "
                + $"{ImagePromptDefaults.SceneMaxWords}. Not attaching a partial set.");
        }

        return $"{text} {Closing}";
    }
}
