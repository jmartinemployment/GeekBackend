namespace GeekAPI.Services.Workflow.Services;

public static class ImagePromptDefaults
{
    public const string DefaultImageModel = "Default";
    public const string DefaultImageModelId = "default";

    public const int PillarWidth = 1536;
    public const int PillarHeight = 1024;
    public const string PillarStylePreset = "Illustration";

    /// <summary>The scene the model writes, before the closing is appended: asked for this many words.</summary>
    public const int AskedMinWords = 90;
    public const int AskedMaxWords = 130;

    /// <summary>What is accepted. Outside it the whole set is refused (GccImageStyle.Complete).</summary>
    public const int SceneMinWords = 50;
    public const int SceneMaxWords = 180;
}
