using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class Restaurant
{
    public string Id { get; set; } = null!;

    public string Slug { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public string? Logo { get; set; }

    public string Phone { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string Address { get; set; } = null!;

    public string City { get; set; } = null!;

    public string State { get; set; } = null!;

    public string Zip { get; set; } = null!;

    public string? CuisineType { get; set; }

    public int Tier { get; set; }

    public decimal? MonthlyRevenue { get; set; }

    public int? DeliveryPercentage { get; set; }

    public List<string>? PlatformsUsed { get; set; }

    public string? PosSystem { get; set; }

    public bool Active { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public bool DeliveryEnabled { get; set; }

    public bool DineInEnabled { get; set; }

    public bool PickupEnabled { get; set; }

    public decimal TaxRate { get; set; }

    public string? Location { get; set; }

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    public string? RestaurantGroupId { get; set; }

    public string? AiSettings { get; set; }

    public string? BusinessHours { get; set; }

    public string? MerchantProfile { get; set; }

    public string? PaymentProcessor { get; set; }

    public string? PaypalMerchantId { get; set; }

    public int PlatformFeeFixed { get; set; }

    public double PlatformFeePercent { get; set; }

    public string? StripeConnectedAccountId { get; set; }

    public string PlanTier { get; set; } = null!;

    public string? NotificationSettings { get; set; }

    public string? BusinessCategory { get; set; }

    public string? DefaultBrandingColor { get; set; }

    public string? DefaultBrandingLogoUrl { get; set; }

    public string? DefaultInvoiceNotes { get; set; }

    public bool HasUsedTrial { get; set; }

    public DateTime? TrialEndsAt { get; set; }

    public DateTime? TrialExpiredAt { get; set; }

    public DateTime? TrialStartedAt { get; set; }

    public virtual ICollection<AiUsageLog> AiUsageLogs { get; set; } = new List<AiUsageLog>();

    public virtual ICollection<BreakType> BreakTypes { get; set; } = new List<BreakType>();

    public virtual ICollection<Campaign1> Campaign1s { get; set; } = new List<Campaign1>();

    public virtual CateringCapacitySetting? CateringCapacitySetting { get; set; }

    public virtual ICollection<CateringEvent> CateringEvents { get; set; } = new List<CateringEvent>();

    public virtual ICollection<Combo> Combos { get; set; } = new List<Combo>();

    public virtual ICollection<CommissionRule> CommissionRules { get; set; } = new List<CommissionRule>();

    public virtual ICollection<ComplianceAlert> ComplianceAlerts { get; set; } = new List<ComplianceAlert>();

    public virtual ICollection<CustomerFeedback> CustomerFeedbacks { get; set; } = new List<CustomerFeedback>();

    public virtual ICollection<Customer> Customers { get; set; } = new List<Customer>();

    public virtual ICollection<CycleCount> CycleCounts { get; set; } = new List<CycleCount>();

    public virtual ICollection<Device1> Device1s { get; set; } = new List<Device1>();

    public virtual ICollection<DeviceMode> DeviceModes { get; set; } = new List<DeviceMode>();

    public virtual ICollection<Event> Events { get; set; } = new List<Event>();

    public virtual ICollection<FoodCostRecipe> FoodCostRecipes { get; set; } = new List<FoodCostRecipe>();

    public virtual ICollection<GiftCard> GiftCards { get; set; } = new List<GiftCard>();

    public virtual ICollection<HouseAccount> HouseAccounts { get; set; } = new List<HouseAccount>();

    public virtual ICollection<InventoryItem> InventoryItems { get; set; } = new List<InventoryItem>();

    public virtual ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();

    public virtual ICollection<KioskProfile> KioskProfiles { get; set; } = new List<KioskProfile>();

    public virtual ICollection<Layaway> Layaways { get; set; } = new List<Layaway>();

    public virtual ICollection<LocationGroupMember> LocationGroupMembers { get; set; } = new List<LocationGroupMember>();

    public virtual ICollection<LoyaltyReward> LoyaltyRewards { get; set; } = new List<LoyaltyReward>();

    public virtual ICollection<LoyaltyTransaction> LoyaltyTransactions { get; set; } = new List<LoyaltyTransaction>();

    public virtual ICollection<MarketingAutomation> MarketingAutomations { get; set; } = new List<MarketingAutomation>();

    public virtual ICollection<MarketplaceIntegration> MarketplaceIntegrations { get; set; } = new List<MarketplaceIntegration>();

    public virtual ICollection<MarketplaceMenuMapping> MarketplaceMenuMappings { get; set; } = new List<MarketplaceMenuMapping>();

    public virtual ICollection<MarketplaceOrder> MarketplaceOrders { get; set; } = new List<MarketplaceOrder>();

    public virtual ICollection<MarketplaceStatusSyncJob> MarketplaceStatusSyncJobs { get; set; } = new List<MarketplaceStatusSyncJob>();

    public virtual ICollection<MarketplaceWebhookEvent> MarketplaceWebhookEvents { get; set; } = new List<MarketplaceWebhookEvent>();

    public virtual ICollection<MenuCategory> MenuCategories { get; set; } = new List<MenuCategory>();

    public virtual ICollection<MenuItem> MenuItems { get; set; } = new List<MenuItem>();

    public virtual ICollection<MessageTemplate> MessageTemplates { get; set; } = new List<MessageTemplate>();

    public virtual ICollection<MessageThread> MessageThreads { get; set; } = new List<MessageThread>();

    public virtual ICollection<ModifierGroup> ModifierGroups { get; set; } = new List<ModifierGroup>();

    public virtual ICollection<OrderSentiment> OrderSentiments { get; set; } = new List<OrderSentiment>();

    public virtual ICollection<OrderTemplate> OrderTemplates { get; set; } = new List<OrderTemplate>();

    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();

    public virtual ICollection<PayrollPeriod> PayrollPeriods { get; set; } = new List<PayrollPeriod>();

    public virtual ICollection<PeripheralDevice> PeripheralDevices { get; set; } = new List<PeripheralDevice>();

    public virtual ICollection<PermissionSet> PermissionSets { get; set; } = new List<PermissionSet>();

    public virtual ICollection<PrimaryCategory> PrimaryCategories { get; set; } = new List<PrimaryCategory>();

    public virtual ICollection<PrinterProfile> PrinterProfiles { get; set; } = new List<PrinterProfile>();

    public virtual ICollection<Printer> Printers { get; set; } = new List<Printer>();

    public virtual ICollection<PurchaseInvoice> PurchaseInvoices { get; set; } = new List<PurchaseInvoice>();

    public virtual ICollection<RecurringReservation> RecurringReservations { get; set; } = new List<RecurringReservation>();

    public virtual ReferralConfig? ReferralConfig { get; set; }

    public virtual ICollection<ReportSchedule> ReportSchedules { get; set; } = new List<ReportSchedule>();

    public virtual ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();

    public virtual RestaurantAiCredential? RestaurantAiCredential { get; set; }

    public virtual RestaurantDeliveryCredential? RestaurantDeliveryCredential { get; set; }

    public virtual RestaurantGroup? RestaurantGroup { get; set; }

    public virtual RestaurantLoyaltyConfig? RestaurantLoyaltyConfig { get; set; }

    public virtual ICollection<RestaurantProviderProfileEvent> RestaurantProviderProfileEvents { get; set; } = new List<RestaurantProviderProfileEvent>();

    public virtual ICollection<RestaurantProviderProfile> RestaurantProviderProfiles { get; set; } = new List<RestaurantProviderProfile>();

    public virtual RestaurantSupplierCredential? RestaurantSupplierCredential { get; set; }

    public virtual ICollection<RestaurantTable> RestaurantTables { get; set; } = new List<RestaurantTable>();

    public virtual ICollection<RetailCategory> RetailCategories { get; set; } = new List<RetailCategory>();

    public virtual ICollection<RetailItem> RetailItems { get; set; } = new List<RetailItem>();

    public virtual ICollection<RetailOptionSet> RetailOptionSets { get; set; } = new List<RetailOptionSet>();

    public virtual ICollection<RetailQuickKey> RetailQuickKeys { get; set; } = new List<RetailQuickKey>();

    public virtual ICollection<RetailStock> RetailStocks { get; set; } = new List<RetailStock>();

    public virtual ICollection<SavedReport> SavedReports { get; set; } = new List<SavedReport>();

    public virtual ICollection<SmartGroup> SmartGroups { get; set; } = new List<SmartGroup>();

    public virtual Subscription? Subscription { get; set; }

    public virtual ICollection<TeamMember> TeamMembers { get; set; } = new List<TeamMember>();

    public virtual ICollection<UnitConversion> UnitConversions { get; set; } = new List<UnitConversion>();

    public virtual ICollection<UserRestaurantAccess> UserRestaurantAccesses { get; set; } = new List<UserRestaurantAccess>();

    public virtual ICollection<Vendor> Vendors { get; set; } = new List<Vendor>();
}
