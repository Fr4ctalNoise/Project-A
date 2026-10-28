// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Fr4ctalNoise and Project A contributors.

using System;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.ProjectA.Auth;

/// <summary>
/// Required on every Project A endpoint except the anonymous script, together with
/// <c>[Authorize]</c>. A build test fails if an endpoint has neither this nor an
/// approved <c>[AllowAnonymous]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class SocialUserAttribute : TypeFilterAttribute
{
    /// <summary>Initializes a new instance of the <see cref="SocialUserAttribute"/> class.</summary>
    public SocialUserAttribute()
        : base(typeof(SocialUserFilter))
    {
        Arguments = [new SocialEndpointRules(false, false)];
    }

    /// <summary>Gets or sets a value indicating whether API keys may call this endpoint. Health check only.</summary>
    public bool AllowApiKey
    {
        get => ((SocialEndpointRules)Arguments![0]).AllowApiKey;
        set => Arguments = [((SocialEndpointRules)Arguments![0]) with { AllowApiKey = value }];
    }

    /// <summary>Gets or sets a value indicating whether only administrators may call this endpoint.</summary>
    public bool RequireAdministrator
    {
        get => ((SocialEndpointRules)Arguments![0]).RequireAdministrator;
        set => Arguments = [((SocialEndpointRules)Arguments![0]) with { RequireAdministrator = value }];
    }
}

/// <summary>
/// Applies <see cref="SocialAccess"/> to a request, re-reading the account on every
/// request so a disabled or deleted user loses access immediately.
/// </summary>
public sealed class SocialUserFilter : IAsyncAuthorizationFilter
{
    /// <summary>Key under which the checked caller's user id is stored in <c>HttpContext.Items</c>.</summary>
    public const string UserIdItemKey = "ProjectA.UserId";

    private readonly IAuthorizationContext _authorizationContext;
    private readonly IUserManager _userManager;
    private readonly SocialEndpointRules _rules;

    /// <summary>Initializes a new instance of the <see cref="SocialUserFilter"/> class.</summary>
    /// <param name="authorizationContext">Jellyfin's view of the request's token (cached per request).</param>
    /// <param name="userManager">Jellyfin's users.</param>
    /// <param name="rules">The endpoint's rules, from <see cref="SocialUserAttribute"/>.</param>
    public SocialUserFilter(IAuthorizationContext authorizationContext, IUserManager userManager, SocialEndpointRules rules)
    {
        _authorizationContext = authorizationContext;
        _userManager = userManager;
        _rules = rules;
    }

    /// <inheritdoc />
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var info = await _authorizationContext.GetAuthorizationInfo(context.HttpContext).ConfigureAwait(false);

        var user = !info.IsApiKey && info.UserId != Guid.Empty ? _userManager.GetUserById(info.UserId) : null;
        var caller = new SocialCaller(
            IsAuthenticated: info.IsAuthenticated,
            IsApiKey: info.IsApiKey,
            UserExists: user is not null,
            IsDisabled: user?.HasPermission(PermissionKind.IsDisabled) ?? false,
            IsAdministrator: user?.HasPermission(PermissionKind.IsAdministrator) ?? false);

        var decision = SocialAccess.Decide(caller, _rules);
        if (decision != SocialAccessDecision.Allow)
        {
            context.Result = new StatusCodeResult(SocialAccess.StatusCodeFor(decision));
            return;
        }

        if (user is not null)
        {
            context.HttpContext.Items[UserIdItemKey] = user.Id;
        }
    }
}
