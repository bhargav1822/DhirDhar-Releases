using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Profiles;
using DhirDhar.Domain.Entities;
using DhirDhar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Infrastructure.Profiles;

/// <summary>
/// Persists the created user's/profile's display name in the application settings table.
/// </summary>
public sealed class ProfileService : IProfileService
{
    public const string ProfileNameSettingKey = "Profile.Name";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProfileService> _logger;

    public ProfileService(
        IServiceScopeFactory scopeFactory,
        ILogger<ProfileService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<string?> GetProfileNameAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

            var setting = await dbContext.ApplicationSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == ProfileNameSettingKey, cancellationToken)
                .ConfigureAwait(false);

            var name = setting?.Value?.Trim();
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to load the profile name.");
            return null;
        }
    }

    public async Task SetProfileNameAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

        var setting = await dbContext.ApplicationSettings
            .FirstOrDefaultAsync(s => s.Key == ProfileNameSettingKey, cancellationToken)
            .ConfigureAwait(false);

        if (setting is null)
        {
            dbContext.ApplicationSettings.Add(new ApplicationSetting(ProfileNameSettingKey, name.Trim()));
        }
        else
        {
            setting.UpdateValue(name.Trim());
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Profile name saved.");
    }
}
