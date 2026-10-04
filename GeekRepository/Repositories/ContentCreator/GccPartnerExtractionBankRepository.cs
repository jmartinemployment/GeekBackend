using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreator;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.ContentCreator;

public class GccPartnerExtractionBankRepository(ContentCreatorDbContext db) : IGccPartnerExtractionBankRepository
{
    public async Task<GccBankedPartnerExtractionDto?> FindAsync(
        string partnerHost, string pagesDigest, CancellationToken ct = default)
    {
        var row = await db.GccPartnerExtractions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.PartnerHost == partnerHost && e.PagesDigest == pagesDigest, ct);
        return row is null ? null : ToDto(row);
    }

    public async Task<GccBankedPartnerExtractionDto> UpsertAsync(
        BankGccPartnerExtractionCommand command, CancellationToken ct = default)
    {
        var row = await db.GccPartnerExtractions
            .FirstOrDefaultAsync(e => e.PartnerHost == command.PartnerHost && e.PagesDigest == command.PagesDigest, ct);

        if (row is null)
        {
            row = new GccBankedPartnerExtraction
            {
                PartnerHost = command.PartnerHost,
                PagesDigest = command.PagesDigest,
            };
            db.GccPartnerExtractions.Add(row);
        }

        row.CreateId = command.CreateId;
        row.ProductName = command.ProductName;
        row.ExtractionJson = command.ExtractionJson;
        row.PagesAttempted = command.PagesAttempted;
        row.ExtractedAtUtc = DateTime.UtcNow;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (row.Id != Guid.Empty)
        {
            // Two generates banked the same host and digest at the same moment, and the unique
            // index refused the second insert. Both extracted the same pages for the same product,
            // so the row that won is this row's content; read it back rather than failing a
            // generate over a duplicate of its own result.
            db.Entry(row).State = EntityState.Detached;
            var winner = await db.GccPartnerExtractions
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.PartnerHost == command.PartnerHost && e.PagesDigest == command.PagesDigest, ct);
            if (winner is not null) return ToDto(winner);
            throw;
        }

        return ToDto(row);
    }

    private static GccBankedPartnerExtractionDto ToDto(GccBankedPartnerExtraction row) => new(
        row.Id,
        row.PartnerHost,
        row.PagesDigest,
        row.CreateId,
        row.ProductName,
        row.ExtractionJson,
        row.PagesAttempted,
        row.ExtractedAtUtc);
}
