using GeekBackend.Data.Data;
using GeekBackend.Data.Models;
using GeekBackend.Data.Results;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories;

public class PendingVerificationRepository : IPendingVerificationRepository
{
    private readonly AppDbContext _context;

    public PendingVerificationRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Result<PendingVerification>> FindByEmailAsync(string email)
    {
        try
        {
            var verification = await _context.PendingVerifications
                .FirstOrDefaultAsync(pv => pv.Email == email);
            return verification != null
                ? Result<PendingVerification>.Success(verification)
                : Result<PendingVerification>.NotFound("PendingVerification not found");
        }
        catch (Exception ex)
        {
            return Result<PendingVerification>.Failure(ex.Message);
        }
    }

    public async Task<Result<PendingVerification>> UpsertAsync(UpsertPendingVerificationRequest request)
    {
        try
        {
            var verification = await _context.PendingVerifications
                .FirstOrDefaultAsync(pv => pv.Email == request.Email);

            if (verification == null)
            {
                verification = new PendingVerification
                {
                    Id = Guid.NewGuid().ToString(),
                    Email = request.Email,
                    OtpHash = request.OtpHash,
                    Attempts = 0,
                    ExpiresAt = request.ExpiresAt,
                    CreatedAt = DateTime.UtcNow
                };
                _context.PendingVerifications.Add(verification);
            }
            else
            {
                verification.OtpHash = request.OtpHash;
                verification.Attempts = 0;
                verification.ExpiresAt = request.ExpiresAt;
            }

            await _context.SaveChangesAsync();
            return Result<PendingVerification>.Success(verification);
        }
        catch (Exception ex)
        {
            return Result<PendingVerification>.Failure(ex.Message);
        }
    }

    public async Task<Result<PendingVerification>> IncrementAttemptsAsync(string id)
    {
        try
        {
            var verification = await _context.PendingVerifications
                .FirstOrDefaultAsync(pv => pv.Id == id);
            if (verification == null)
                return Result<PendingVerification>.NotFound("PendingVerification not found");

            verification.Attempts++;
            await _context.SaveChangesAsync();

            return Result<PendingVerification>.Success(verification);
        }
        catch (Exception ex)
        {
            return Result<PendingVerification>.Failure(ex.Message);
        }
    }

    public async Task<Result<Unit>> DeleteAsync(string id)
    {
        try
        {
            var verification = await _context.PendingVerifications
                .FirstOrDefaultAsync(pv => pv.Id == id);
            if (verification == null) return Result<Unit>.NotFound("PendingVerification not found");

            _context.PendingVerifications.Remove(verification);
            await _context.SaveChangesAsync();

            return Result<Unit>.Success(new Unit());
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure(ex.Message);
        }
    }
}
