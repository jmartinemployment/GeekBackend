namespace GeekRepository.Data.Entities.ContentWriterV4;

public class ProviderModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public decimal InputCostPer1K { get; set; }
    public decimal OutputCostPer1K { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime EffectiveAtUtc { get; set; } = DateTime.UtcNow;
}
