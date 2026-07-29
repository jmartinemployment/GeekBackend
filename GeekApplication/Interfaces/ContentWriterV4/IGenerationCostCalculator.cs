namespace GeekApplication.Interfaces.ContentWriterV4;

public interface IGenerationCostCalculator
{
    Task<decimal> CalculateCostUsdAsync(
        string provider,
        string model,
        int inputTokens,
        int outputTokens,
        CancellationToken ct = default);
}
