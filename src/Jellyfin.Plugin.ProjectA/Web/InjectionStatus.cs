// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Fr4ctalNoise and Project A contributors.

using System;
using System.Threading;

namespace Jellyfin.Plugin.ProjectA.Web;

/// <summary>
/// Remembers when the script was last added to the web page, so the health check
/// can report whether injection is working. Shared singleton; thread-safe.
/// </summary>
public sealed class InjectionStatus
{
    private long _lastInjectedTicks;

    /// <summary>Gets when the script was last injected (UTC), or null if not since Jellyfin started.</summary>
    public DateTimeOffset? LastInjected
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastInjectedTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <summary>Records an injection now.</summary>
    public void MarkInjected() => Interlocked.Exchange(ref _lastInjectedTicks, DateTimeOffset.UtcNow.UtcTicks);
}
