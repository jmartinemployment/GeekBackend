using GeekBackend.Data.Data;
using GeekBackend.Data.Models;
using GeekBackend.Data.Results;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories;

public class DeviceRepository : IDeviceRepository
{
    private readonly AppDbContext _context;

    public DeviceRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Result<Device>> FindByIdAsync(string id)
    {
        try
        {
            var device = await _context.DevicesAuth.FirstOrDefaultAsync(d => d.Id == id);
            return device != null ? Result<Device>.Success(device) : Result<Device>.NotFound("Device not found");
        }
        catch (Exception ex)
        {
            return Result<Device>.Failure(ex.Message);
        }
    }

    public async Task<Result<Device>> FindByUserAndDeviceIdAsync(string userId, string deviceId)
    {
        try
        {
            var device = await _context.DevicesAuth
                .FirstOrDefaultAsync(d => d.UserId == userId && d.DeviceId == deviceId);
            return device != null ? Result<Device>.Success(device) : Result<Device>.NotFound("Device not found");
        }
        catch (Exception ex)
        {
            return Result<Device>.Failure(ex.Message);
        }
    }

    public async Task<Result<List<Device>>> FindByUserIdAsync(string userId)
    {
        try
        {
            var devices = await _context.DevicesAuth
                .Where(d => d.UserId == userId)
                .ToListAsync();
            return Result<List<Device>>.Success(devices);
        }
        catch (Exception ex)
        {
            return Result<List<Device>>.Failure(ex.Message);
        }
    }

    public async Task<Result<Device>> UpsertAsync(UpsertDeviceRequest request)
    {
        try
        {
            var device = await _context.DevicesAuth
                .FirstOrDefaultAsync(d => d.UserId == request.UserId && d.DeviceId == request.DeviceId);

            if (device == null)
            {
                device = new Device
                {
                    Id = Guid.NewGuid().ToString(),
                    UserId = request.UserId,
                    DeviceId = request.DeviceId,
                    UserAgent = request.UserAgent,
                    IpAddress = request.IpAddress,
                    Status = "active",
                    MfaVerifiedAt = request.MfaVerifiedAt,
                    LastLoginAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.DevicesAuth.Add(device);
            }
            else
            {
                if (!string.IsNullOrEmpty(request.UserAgent)) device.UserAgent = request.UserAgent;
                if (!string.IsNullOrEmpty(request.IpAddress)) device.IpAddress = request.IpAddress;
                if (request.MfaVerifiedAt.HasValue) device.MfaVerifiedAt = request.MfaVerifiedAt;
                device.LastLoginAt = DateTime.UtcNow;
                device.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            return Result<Device>.Success(device);
        }
        catch (Exception ex)
        {
            return Result<Device>.Failure(ex.Message);
        }
    }

    public async Task<Result<Device>> UpdateAsync(string id, UpdateDeviceRequest request)
    {
        try
        {
            var device = await _context.DevicesAuth.FirstOrDefaultAsync(d => d.Id == id);
            if (device == null) return Result<Device>.NotFound("Device not found");

            if (!string.IsNullOrEmpty(request.Status)) device.Status = request.Status;
            if (request.MfaVerifiedAt.HasValue) device.MfaVerifiedAt = request.MfaVerifiedAt;
            if (request.MfaExpiresAt.HasValue) device.MfaExpiresAt = request.MfaExpiresAt;
            if (request.LastLoginAt.HasValue) device.LastLoginAt = request.LastLoginAt.Value;
            device.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Result<Device>.Success(device);
        }
        catch (Exception ex)
        {
            return Result<Device>.Failure(ex.Message);
        }
    }

    public async Task<Result<Unit>> DeleteAsync(string id)
    {
        try
        {
            var device = await _context.DevicesAuth.FirstOrDefaultAsync(d => d.Id == id);
            if (device == null) return Result<Unit>.NotFound("Device not found");

            _context.DevicesAuth.Remove(device);
            await _context.SaveChangesAsync();

            return Result<Unit>.Success(new Unit());
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure(ex.Message);
        }
    }
}
