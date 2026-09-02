using System.Net;
using NalpeironGrowthPlatformDemo.Nalpeiron.Generic;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter.Generated;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter;

public sealed class ZenmeterSubscriptionUserProvisioner(IZenmeterManagementClient zenmeter)
{
    public async Task<SubscriptionUserModel> EnsureUser(
        string subscriptionId,
        ZenmeterUserDetails user,
        CancellationToken cancellationToken)
    {
        var existing = await FindUser(subscriptionId, user.ExternalUserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await zenmeter.CreateUser(
                subscriptionId,
                user.ExternalUserId,
                user.FirstName,
                user.LastName,
                user.Email,
                cancellationToken) ?? throw new InvalidOperationException("Zenmeter user creation returned no user.");
        }
        catch (ManagementApiException ex) when (ex.ApiStatusCode == HttpStatusCode.Conflict)
        {
            return await ReloadAfterCreateConflict(subscriptionId, user.ExternalUserId, cancellationToken);
        }
        catch (ZenmeterManagementApiException ex) when (ex.StatusCode == (int)HttpStatusCode.Conflict)
        {
            return await ReloadAfterCreateConflict(subscriptionId, user.ExternalUserId, cancellationToken);
        }
    }

    private async Task<SubscriptionUserModel> ReloadAfterCreateConflict(
        string subscriptionId,
        string externalUserId,
        CancellationToken cancellationToken)
    {
        var afterConflict = await FindUser(subscriptionId, externalUserId, cancellationToken);
        if (afterConflict is not null)
        {
            return afterConflict;
        }

        throw new InvalidOperationException(
            $"Zenmeter user {externalUserId} already exists, but could not be reloaded.");
    }

    public async Task<SubscriptionUserModel?> FindUser(
        string subscriptionId,
        string externalUserId,
        CancellationToken cancellationToken)
    {
        var users = await zenmeter.ListUsers(subscriptionId, cancellationToken);

        return users.FirstOrDefault(user =>
            string.Equals(
                user.ExternalUserId,
                externalUserId,
                StringComparison.OrdinalIgnoreCase));
    }
}