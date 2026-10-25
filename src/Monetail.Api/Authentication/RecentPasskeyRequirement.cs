using Microsoft.AspNetCore.Authorization;

namespace Monetail.Api.Authentication;

/// <summary>重要操作に直近5分未満のパスキー本人確認を要求する。</summary>
public sealed class RecentPasskeyRequirement : IAuthorizationRequirement
{
    public const string PolicyName = "RecentPasskey";
}

/// <summary>要求ごとに検証したセッションの確認日時から、重要操作を許可する。</summary>
public sealed class RecentPasskeyHandler(TimeProvider time) : AuthorizationHandler<RecentPasskeyRequirement>
{
    /// <summary>未来の確認日時や期限切れを許可せず、5分ちょうどから再確認を要求する。</summary>
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, RecentPasskeyRequirement requirement)
    {
        if (context.Resource is HttpContext http && http.Items[typeof(CurrentSession)] is CurrentSession current)
        {
            var now = time.GetUtcNow().UtcDateTime;
            if (current.Session.RevokedAtUtc is null && current.Session.ExpiresAtUtc > now &&
                current.Session.VerifiedAtUtc <= now && current.Session.VerifiedAtUtc > now.AddMinutes(-5))
                context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}
