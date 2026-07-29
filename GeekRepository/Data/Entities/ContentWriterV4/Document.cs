namespace GeekRepository.Data.Entities.ContentWriterV4;

public class Document
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public Guid? TemplateId { get; set; }
    public Guid? BrandVoiceId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string InputsJson { get; set; } = "{}";
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public Template? Template { get; set; }
    public BrandVoice? BrandVoice { get; set; }
}
