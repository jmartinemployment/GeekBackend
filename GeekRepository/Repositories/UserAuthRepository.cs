using GeekBackend.Data.Data;
using GeekBackend.Data.Models;
using GeekBackend.Data.Results;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories;

public class UserAuthRepository : IUserAuthRepository
{
    private readonly AppDbContext _context;

    public UserAuthRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Result<User>> FindByIdAsync(string id)
    {
        try
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
            return user != null ? Result<User>.Success(user) : Result<User>.NotFound("User not found");
        }
        catch (Exception ex)
        {
            return Result<User>.Failure(ex.Message);
        }
    }

    public async Task<Result<User>> FindByEmailAsync(string email)
    {
        try
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            return user != null ? Result<User>.Success(user) : Result<User>.NotFound("User not found");
        }
        catch (Exception ex)
        {
            return Result<User>.Failure(ex.Message);
        }
    }

    public async Task<Result<User>> FindBySlackIdAsync(string slackUserId)
    {
        try
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.SlackUserId == slackUserId);
            return user != null ? Result<User>.Success(user) : Result<User>.NotFound("User not found");
        }
        catch (Exception ex)
        {
            return Result<User>.Failure(ex.Message);
        }
    }

    public async Task<Result<User>> CreateAsync(CreateUserRequest request)
    {
        try
        {
            var user = new User
            {
                Id = Guid.NewGuid().ToString(),
                Email = request.Email,
                Name = request.Name,
                Password = request.Password,
                Plan = request.Plan,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return Result<User>.Success(user);
        }
        catch (Exception ex)
        {
            return Result<User>.Failure(ex.Message);
        }
    }

    public async Task<Result<User>> UpdateAsync(string id, UpdateUserRequest request)
    {
        try
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return Result<User>.NotFound("User not found");

            if (!string.IsNullOrEmpty(request.Name)) user.Name = request.Name;
            if (!string.IsNullOrEmpty(request.Password)) user.Password = request.Password;
            if (!string.IsNullOrEmpty(request.Plan)) user.Plan = request.Plan;
            if (!string.IsNullOrEmpty(request.SlackUserId)) user.SlackUserId = request.SlackUserId;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Result<User>.Success(user);
        }
        catch (Exception ex)
        {
            return Result<User>.Failure(ex.Message);
        }
    }

    public async Task<Result<Unit>> DeleteAsync(string id)
    {
        try
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return Result<Unit>.NotFound("User not found");

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            return Result<Unit>.Success(new Unit());
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure(ex.Message);
        }
    }
}
