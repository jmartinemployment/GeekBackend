using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreator;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.ContentCreator;

/// <summary>
/// The one client table.
/// </summary>
/// <remarks>
/// An address is stored as nulls when nothing was given, never as empty strings: "" in a column
/// reads as a value someone meant to store, and it is not one. The same rule applies to the
/// publish target — all-or-nothing, because half of it configures nothing.
/// </remarks>
public class GccClientRepository : IGccClientRepository
{
    private readonly ContentCreatorDbContext _db;

    public GccClientRepository(ContentCreatorDbContext db) => _db = db;

    public async Task<GccClientDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.GccClients.FirstOrDefaultAsync(c => c.Id == id, ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<GccClientDto?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        var trimmedName = name.Trim();
        var entity = await _db.GccClients
            .FirstOrDefaultAsync(c => c.Name.ToLower() == trimmedName.ToLower(), ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<IReadOnlyList<GccClientDto>> ListAsync(CancellationToken ct = default)
    {
        var entities = await _db.GccClients.OrderBy(c => c.Name).ToListAsync(ct);
        return entities.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<GccClientDto> CreateAsync(CreateGccClientCommand command, CancellationToken ct = default)
    {
        var entity = new GccClient
        {
            Name = command.Name.Trim(),
            Notes = Normalize(command.Notes),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        ApplyDetails(
            entity,
            command.ContactName,
            command.ContactEmail,
            command.ContactPhone,
            command.BillingContactName,
            command.BillingEmail,
            command.ContactAddress,
            command.BillingAddress,
            command.PaymentTermsDays,
            command.Rate,
            command.Currency,
            command.TaxId,
            command.PoReference,
            command.PublishTarget);

        _db.GccClients.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    public async Task<GccClientDto?> UpdateAsync(UpdateGccClientCommand command, CancellationToken ct = default)
    {
        var entity = await _db.GccClients.FirstOrDefaultAsync(c => c.Id == command.Id, ct);
        if (entity is null) return null;

        entity.Name = command.Name.Trim();
        entity.Notes = Normalize(command.Notes);
        entity.UpdatedAtUtc = DateTime.UtcNow;

        ApplyDetails(
            entity,
            command.ContactName,
            command.ContactEmail,
            command.ContactPhone,
            command.BillingContactName,
            command.BillingEmail,
            command.ContactAddress,
            command.BillingAddress,
            command.PaymentTermsDays,
            command.Rate,
            command.Currency,
            command.TaxId,
            command.PoReference,
            command.PublishTarget);

        _db.GccClients.Update(entity);
        await _db.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    /// <summary>
    /// Delete a client.
    /// </summary>
    /// <remarks>
    /// No cascade is written here on purpose. gcc_projects.client_id is RESTRICT, so a client with
    /// projects cannot be deleted and Postgres says so — which is the answer wanted, not something
    /// to work around by deleting the projects too. Their time, deliverables and log are the
    /// client's record of work done.
    /// </remarks>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.GccClients.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null) return false;

        _db.GccClients.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static void ApplyDetails(
        GccClient entity,
        string contactName,
        string contactEmail,
        string? contactPhone,
        string? billingContactName,
        string billingEmail,
        GccClientAddress? contactAddress,
        GccClientAddress? billingAddress,
        int paymentTermsDays,
        decimal? rate,
        string currency,
        string? taxId,
        string? poReference,
        GccClientPublishTarget? publishTarget)
    {
        entity.ContactName = contactName.Trim();
        entity.ContactEmail = contactEmail.Trim();
        entity.ContactPhone = Normalize(contactPhone);
        entity.BillingContactName = Normalize(billingContactName);
        entity.BillingEmail = billingEmail.Trim();

        var contact = contactAddress ?? GccClientAddress.Empty;
        entity.ContactAddressLine1 = Normalize(contact.Line1);
        entity.ContactAddressLine2 = Normalize(contact.Line2);
        entity.ContactCity = Normalize(contact.City);
        entity.ContactRegion = Normalize(contact.Region);
        entity.ContactPostalCode = Normalize(contact.PostalCode);
        entity.ContactCountry = Normalize(contact.Country);

        var billing = billingAddress ?? GccClientAddress.Empty;
        entity.BillingAddressLine1 = Normalize(billing.Line1);
        entity.BillingAddressLine2 = Normalize(billing.Line2);
        entity.BillingCity = Normalize(billing.City);
        entity.BillingRegion = Normalize(billing.Region);
        entity.BillingPostalCode = Normalize(billing.PostalCode);
        entity.BillingCountry = Normalize(billing.Country);

        entity.PaymentTermsDays = paymentTermsDays;
        entity.Rate = rate;
        entity.Currency = currency.Trim().ToUpperInvariant();
        entity.TaxId = Normalize(taxId);
        entity.PoReference = Normalize(poReference);

        entity.PublishApiBaseUrl = Normalize(publishTarget?.ApiBaseUrl);
        entity.PublishOAuthTokenEndpoint = Normalize(publishTarget?.OAuthTokenEndpoint);
        entity.PublishClientIdEnvVar = Normalize(publishTarget?.ClientIdEnvVar);
        entity.PublishClientSecretEnvVar = Normalize(publishTarget?.ClientSecretEnvVar);
        entity.PublishDefaultAuthorId = publishTarget?.DefaultAuthorId;
        entity.PublishCategoryStrategy = Normalize(publishTarget?.CategoryStrategy);
    }

    /// <summary>Blank is absent. A column holding "" is a value nothing meant to store.</summary>
    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static GccClientDto MapToDto(GccClient entity) =>
        new(
            entity.Id,
            entity.Name,
            entity.Notes,
            entity.ContactName,
            entity.ContactEmail,
            entity.ContactPhone,
            entity.BillingContactName,
            entity.BillingEmail,
            new GccClientAddress(
                entity.ContactAddressLine1,
                entity.ContactAddressLine2,
                entity.ContactCity,
                entity.ContactRegion,
                entity.ContactPostalCode,
                entity.ContactCountry),
            new GccClientAddress(
                entity.BillingAddressLine1,
                entity.BillingAddressLine2,
                entity.BillingCity,
                entity.BillingRegion,
                entity.BillingPostalCode,
                entity.BillingCountry),
            entity.PaymentTermsDays,
            entity.Rate,
            entity.Currency.Trim(),
            entity.TaxId,
            entity.PoReference,
            // The base URL is what makes a publish target usable; without it the rest configures
            // nothing, so the whole thing reads as absent rather than as a half-filled object.
            string.IsNullOrWhiteSpace(entity.PublishApiBaseUrl)
                ? null
                : new GccClientPublishTarget(
                    entity.PublishApiBaseUrl,
                    entity.PublishOAuthTokenEndpoint ?? "api/oauth/token",
                    entity.PublishClientIdEnvVar ?? string.Empty,
                    entity.PublishClientSecretEnvVar ?? string.Empty,
                    entity.PublishDefaultAuthorId,
                    entity.PublishCategoryStrategy),
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);
}
