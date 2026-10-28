// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Fr4ctalNoise and Project A contributors.

using System;

namespace Jellyfin.Plugin.ProjectA.Auth;

/// <summary>The outcome of an access check.</summary>
public enum SocialAccessDecision
{
    /// <summary>Let the request through.</summary>
    Allow,

    /// <summary>Not signed in, or the account behind the token no longer exists (401).</summary>
    Unauthorized,

    /// <summary>Signed in, but not allowed (403).</summary>
    Forbidden,
}

/// <summary>What the gate knows about the caller of one request.</summary>
/// <param name="IsAuthenticated">Jellyfin accepted the token.</param>
/// <param name="IsApiKey">The token is an API key, with no user behind it.</param>
/// <param name="UserExists">A user account matches the token.</param>
/// <param name="IsDisabled">That account is disabled.</param>
/// <param name="IsAdministrator">That account is an administrator.</param>
public readonly record struct SocialCaller(bool IsAuthenticated, bool IsApiKey, bool UserExists, bool IsDisabled, bool IsAdministrator);

/// <summary>What an endpoint allows.</summary>
/// <param name="AllowApiKey">API keys may call it. Only the health check sets this.</param>
/// <param name="RequireAdministrator">Only administrators (or an allowed API key) may call it.</param>
public readonly record struct SocialEndpointRules(bool AllowApiKey, bool RequireAdministrator);

/// <summary>
/// The access rules for every Project A endpoint except the anonymous script.
/// They run after Jellyfin's own sign-in check, which lets API keys through and
/// does not look at disabled accounts; this closes both gaps.
/// </summary>
public static class SocialAccess
{
    /// <summary>Decides whether a caller may use an endpoint.</summary>
    /// <param name="caller">The caller.</param>
    /// <param name="rules">The endpoint's rules.</param>
    /// <returns>The decision.</returns>
    public static SocialAccessDecision Decide(SocialCaller caller, SocialEndpointRules rules)
    {
        if (!caller.IsAuthenticated)
        {
            return SocialAccessDecision.Unauthorized;
        }

        if (caller.IsApiKey)
        {
            // An API key stands in for an administrator, so it passes an admin-only
            // endpoint only when that endpoint explicitly accepts API keys.
            return rules.AllowApiKey ? SocialAccessDecision.Allow : SocialAccessDecision.Forbidden;
        }

        if (!caller.UserExists)
        {
            // A token whose user was deleted: the sign-in is no longer valid.
            return SocialAccessDecision.Unauthorized;
        }

        if (caller.IsDisabled)
        {
            return SocialAccessDecision.Forbidden;
        }

        if (rules.RequireAdministrator && !caller.IsAdministrator)
        {
            return SocialAccessDecision.Forbidden;
        }

        return SocialAccessDecision.Allow;
    }

    /// <summary>The HTTP status code for a refusal.</summary>
    /// <param name="decision">A decision other than <see cref="SocialAccessDecision.Allow"/>.</param>
    /// <returns>401 or 403.</returns>
    public static int StatusCodeFor(SocialAccessDecision decision) => decision switch
    {
        SocialAccessDecision.Unauthorized => 401,
        SocialAccessDecision.Forbidden => 403,
        _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "Allow has no refusal status."),
    };
}
