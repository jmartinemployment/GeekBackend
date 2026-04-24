using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class Order
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string? CustomerId { get; set; }

    public string OrderNumber { get; set; } = null!;

    public string OrderType { get; set; } = null!;

    public string Status { get; set; } = null!;

    public decimal Subtotal { get; set; }

    public decimal Tax { get; set; }

    public decimal Tip { get; set; }

    public decimal Total { get; set; }

    public string? PaymentMethod { get; set; }

    public string PaymentStatus { get; set; } = null!;

    public string? StripePaymentIntentId { get; set; }

    public string? SpecialInstructions { get; set; }

    public DateTime? ScheduledTime { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string? DeliveryAddress { get; set; }

    public decimal DeliveryFee { get; set; }

    public decimal? DeliveryLat { get; set; }

    public decimal? DeliveryLng { get; set; }

    public string? DeliveryProvider { get; set; }

    public string? DeliveryTrackingUrl { get; set; }

    public decimal Discount { get; set; }

    public string OrderSource { get; set; } = null!;

    public DateTime? SentToKitchenAt { get; set; }

    public string? ServerId { get; set; }

    public string? TableId { get; set; }

    public string? CancellationReason { get; set; }

    public DateTime? CancelledAt { get; set; }

    public string? CancelledBy { get; set; }

    public DateTime? ConfirmedAt { get; set; }

    public DateTime? PreparingAt { get; set; }

    public DateTime? ReadyAt { get; set; }

    public string? SourceDeviceId { get; set; }

    public string? DeliveryAddress2 { get; set; }

    public string? DeliveryCity { get; set; }

    public string? DeliveryStateUs { get; set; }

    public string? DeliveryZip { get; set; }

    public string? DeliveryNotes { get; set; }

    public string? DeliveryStatus { get; set; }

    public DateTime? DeliveryEstimatedAt { get; set; }

    public DateTime? DispatchedAt { get; set; }

    public DateTime? DeliveredAt { get; set; }

    public string? VehicleDescription { get; set; }

    public bool ArrivalNotified { get; set; }

    public DateTime? EventDate { get; set; }

    public string? EventTime { get; set; }

    public int? Headcount { get; set; }

    public string? EventType { get; set; }

    public bool SetupRequired { get; set; }

    public decimal? DepositAmount { get; set; }

    public bool DepositPaid { get; set; }

    public string? CateringInstructions { get; set; }

    public string? ApprovalStatus { get; set; }

    public string? ApprovedBy { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public string? PaypalOrderId { get; set; }

    public string? PaypalCaptureId { get; set; }

    public int LoyaltyPointsEarned { get; set; }

    public int LoyaltyPointsRedeemed { get; set; }

    public string? DeliveryExternalId { get; set; }

    public string? DispatchStatus { get; set; }

    public DateTime? ThrottleHeldAt { get; set; }

    public string? ThrottleReason { get; set; }

    public string? ThrottleReleaseReason { get; set; }

    public DateTime? ThrottleReleasedAt { get; set; }

    public string? ThrottleSource { get; set; }

    public string ThrottleState { get; set; } = null!;

    public virtual Customer? Customer { get; set; }

    public virtual ICollection<LoyaltyTransaction> LoyaltyTransactions { get; set; } = new List<LoyaltyTransaction>();

    public virtual MarketplaceOrder? MarketplaceOrder { get; set; }

    public virtual ICollection<OrderCheck> OrderChecks { get; set; } = new List<OrderCheck>();

    public virtual ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();

    public virtual ICollection<OrderStatusHistory> OrderStatusHistories { get; set; } = new List<OrderStatusHistory>();

    public virtual ICollection<PrintJob> PrintJobs { get; set; } = new List<PrintJob>();

    public virtual Restaurant Restaurant { get; set; } = null!;

    public virtual RestaurantTable? Table { get; set; }
}
