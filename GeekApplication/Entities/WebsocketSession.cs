namespace GeekApplication.Entities;

public class WebsocketSession
{
    public required string SessionId { get; set; }
    public Guid UserId { get; set; }
    public Guid DeviceId { get; set; }
    public required string ServerNode { get; set; } // Hostname for multicast routing
    public DateTime ConnectedAt { get; set; }
    public DateTime LastHeartbeatAt { get; set; }

    // Navigation properties
    public virtual User User { get; set; } = null!;
    public virtual DeviceOauth Device { get; set; } = null!;
}
