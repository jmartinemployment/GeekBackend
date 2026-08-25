namespace GeekRepository.Data.Entities.ContentCreatorV2;

/// <summary>
/// Append-only event log for a <see cref="GccV2Job"/>. <see cref="Seq"/> is monotonic per job
/// (computed server-side on append) so clients can reconnect and replay via <c>afterSeq</c>.
/// </summary>
public class GccV2JobEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public int Seq { get; set; }
    public string Type { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
