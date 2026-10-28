// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Fr4ctalNoise and Project A contributors.

using System;
using Jellyfin.Plugin.ProjectA.Auth;
using Xunit;

namespace Jellyfin.Plugin.ProjectA.Tests;

/// <summary>
/// The access rules (design doc "Who can reach the feed", verification 1),
/// tested as explicit cases and then over every combination of inputs.
/// </summary>
public class SocialAccessTests
{
    private static readonly SocialEndpointRules Normal = new(AllowApiKey: false, RequireAdministrator: false);
    private static readonly SocialEndpointRules Health = new(AllowApiKey: true, RequireAdministrator: true);
    private static readonly SocialEndpointRules AdminOnly = new(AllowApiKey: false, RequireAdministrator: true);

    private static SocialCaller User(bool disabled = false, bool admin = false) => new(true, false, true, disabled, admin);

    [Fact]
    public void No_token_is_refused_with_401() =>
        Assert.Equal(SocialAccessDecision.Unauthorized, SocialAccess.Decide(new SocialCaller(false, false, false, false, false), Normal));

    [Fact]
    public void Ordinary_enabled_user_is_allowed() =>
        Assert.Equal(SocialAccessDecision.Allow, SocialAccess.Decide(User(), Normal));

    [Fact]
    public void Disabled_user_is_refused_with_403() =>
        Assert.Equal(SocialAccessDecision.Forbidden, SocialAccess.Decide(User(disabled: true), Normal));

    [Fact]
    public void Disabled_administrator_is_refused_too() =>
        Assert.Equal(SocialAccessDecision.Forbidden, SocialAccess.Decide(User(disabled: true, admin: true), Health));

    [Fact]
    public void Token_of_a_deleted_user_is_refused_with_401() =>
        Assert.Equal(SocialAccessDecision.Unauthorized, SocialAccess.Decide(new SocialCaller(true, false, false, false, false), Normal));

    [Fact]
    public void Api_key_is_refused_on_normal_endpoints() =>
        Assert.Equal(SocialAccessDecision.Forbidden, SocialAccess.Decide(new SocialCaller(true, true, false, false, false), Normal));

    [Fact]
    public void Api_key_is_refused_on_admin_endpoints_that_do_not_accept_keys() =>
        Assert.Equal(SocialAccessDecision.Forbidden, SocialAccess.Decide(new SocialCaller(true, true, false, false, false), AdminOnly));

    [Fact]
    public void Api_key_is_allowed_on_the_health_check() =>
        Assert.Equal(SocialAccessDecision.Allow, SocialAccess.Decide(new SocialCaller(true, true, false, false, false), Health));

    [Fact]
    public void Ordinary_user_is_refused_on_the_health_check() =>
        Assert.Equal(SocialAccessDecision.Forbidden, SocialAccess.Decide(User(), Health));

    [Fact]
    public void Administrator_is_allowed_on_the_health_check() =>
        Assert.Equal(SocialAccessDecision.Allow, SocialAccess.Decide(User(admin: true), Health));

    [Fact]
    public void Status_codes_are_401_and_403_and_allow_has_none()
    {
        Assert.Equal(401, SocialAccess.StatusCodeFor(SocialAccessDecision.Unauthorized));
        Assert.Equal(403, SocialAccess.StatusCodeFor(SocialAccessDecision.Forbidden));
        Assert.Throws<ArgumentOutOfRangeException>(() => SocialAccess.StatusCodeFor(SocialAccessDecision.Allow));
    }

    /// <summary>The safety properties hold for all 128 combinations, not just the cases above.</summary>
    [Fact]
    public void Safety_properties_hold_for_every_combination()
    {
        var flags = new[] { false, true };
        var checkedCount = 0;
        foreach (var authenticated in flags)
        foreach (var apiKey in flags)
        foreach (var exists in flags)
        foreach (var disabled in flags)
        foreach (var admin in flags)
        foreach (var allowKey in flags)
        foreach (var requireAdmin in flags)
        {
            var caller = new SocialCaller(authenticated, apiKey, exists, disabled, admin);
            var rules = new SocialEndpointRules(allowKey, requireAdmin);
            checkedCount++;
            if (SocialAccess.Decide(caller, rules) != SocialAccessDecision.Allow)
            {
                continue;
            }

            var where = $"{caller} {rules}";
            Assert.True(caller.IsAuthenticated, "never allows without a sign-in: " + where);
            if (caller.IsApiKey)
            {
                Assert.True(rules.AllowApiKey, "an API key only passes where keys are accepted: " + where);
            }
            else
            {
                Assert.True(caller.UserExists, "never allows a deleted user's token: " + where);
                Assert.False(caller.IsDisabled, "never allows a disabled user: " + where);
                Assert.True(!rules.RequireAdministrator || caller.IsAdministrator, "admin-only stays admin-only: " + where);
            }
        }

        Assert.Equal(128, checkedCount);
    }
}
