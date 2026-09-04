using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Repositories;

public class RefreshTokenRepository(AppDbContext context) : IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        context.Set<RefreshToken>().FirstOrDefaultAsync(r => r.TokenHash == tokenHash, cancellationToken);

    public Task<List<RefreshToken>> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.Set<RefreshToken>()
            .Where(r => r.UserId == userId && r.RevokedAt == null && r.ExpiresAt > DateTimeOffset.UtcNow)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default) =>
        await context.Set<RefreshToken>().AddAsync(refreshToken, cancellationToken);

    public void Update(RefreshToken refreshToken) => context.Set<RefreshToken>().Update(refreshToken);
}
