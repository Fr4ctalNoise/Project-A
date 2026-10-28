// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Fr4ctalNoise and Project A contributors.

using System;
using Jellyfin.Plugin.ProjectA.Users;
using Xunit;

namespace Jellyfin.Plugin.ProjectA.Tests;

/// <summary>Every combination of the one rule for whose posts a viewer sees.</summary>
public class AuthorVisibilityTests
{
    private static readonly Guid Author = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Other = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Theory]
    // disabled, hidden, viewer is author, viewer is admin, setting on -> sees
    [InlineData(false, false, false, false, false, true)]
    [InlineData(false, true, false, false, false, true)]   // hidden from login screen, setting off: shows
    [InlineData(false, true, false, false, true, false)]   // setting on: left out
    [InlineData(true, false, false, false, false, false)]  // disabled: left out for ordinary users
    [InlineData(true, true, false, false, false, false)]
    [InlineData(true, false, false, true, false, true)]    // admins see disabled authors
    [InlineData(false, true, false, true, true, true)]     // and hidden ones
    [InlineData(false, true, true, false, true, true)]     // authors always see their own
    [InlineData(true, true, true, false, true, true)]
    public void Rule(bool disabled, bool hidden, bool viewerIsAuthor, bool viewerIsAdmin, bool settingOn, bool sees) =>
        Assert.Equal(sees, AuthorVisibility.CanSee(Author, disabled, hidden, viewerIsAuthor ? Author : Other, viewerIsAdmin, settingOn));
}
