using GeekApplication.Models.ContentCreator;

namespace GeekAPI.HttpClients;

/// <summary>
/// The bank of partner extractions, as the generate service sees it: look one up by host and
/// digest before paying for it, and bank the one just paid for.
/// </summary>
public interface IGccPartnerExtractionBank
{
    Task<GccBankedPartnerExtractionDto?> FindBankedAsync(string partnerHost, string pagesDigest, CancellationToken ct = default);
    Task<GccBankedPartnerExtractionDto> BankAsync(BankGccPartnerExtractionCommand command, CancellationToken ct = default);
}
