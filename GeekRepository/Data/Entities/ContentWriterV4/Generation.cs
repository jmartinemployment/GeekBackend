namespace GeekRepository.Data.Entities.ContentWriterV4;

public class Generation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? DocumentId { get; set; }
    public Guid TemplateId { get; set; }
    public Guid? BrandVoiceId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string InputsJson { get; set; } = "{}";
    public string Output { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal CostUsd { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Document? Document { get; set; }
    public Template? Template { get; set; }
    public BrandVoice? BrandVoice { get; set; }
}
