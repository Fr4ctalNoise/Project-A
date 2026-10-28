// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Fr4ctalNoise and Project A contributors.

using Jellyfin.Plugin.ProjectA.Data;

namespace Jellyfin.Plugin.ProjectA.Api;

/// <summary>
/// Checks and normalises the markdown text of a post (and, in step 3, a comment) before it
/// is stored. Rendering safety is the page's job (markdown-it with raw HTML off, then
/// DOMPurify); this keeps out what has no business in text at all.
/// </summary>
/// <remarks>
/// Error codes are fixed strings the page maps to messages:
/// <c>body_required</c>, <c>body_too_long</c>, <c>body_invalid</c>.
/// </remarks>
public static class PostInput
{
    /// <summary>The text is missing or only whitespace.</summary>
    public const string BodyRequired = "body_required";

    /// <summary>The text is over the limit after normalising.</summary>
    public const string BodyTooLong = "body_too_long";

    /// <summary>The text holds control characters or broken Unicode.</summary>
    public const string BodyInvalid = "body_invalid";

    /// <summary>
    /// Normalises line endings to <c>\n</c>, trims surrounding whitespace, then checks
    /// length (in UTF-16 code units, matching the page's character counter) and content.
    /// </summary>
    /// <param name="raw">The text as sent.</param>
    /// <param name="maxLength">Longest allowed text.</param>
    /// <param name="normalized">The text to store, when valid.</param>
    /// <returns>Null when valid, otherwise an error code.</returns>
    public static string? Validate(string? raw, int maxLength, out string normalized)
    {
        normalized = string.Empty;
        if (raw is null)
        {
            return BodyRequired;
        }

        // Cheap guard before any copying: an over-long body can't become valid by trimming
        // more than a whitespace border, and the request size limit caps it anyway.
        if (raw.Length > maxLength * 2)
        {
            return BodyTooLong;
        }

        var text = raw.Replace("\r\n", "\n", System.StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

        if (text.Length == 0)
        {
            return BodyRequired;
        }

        if (text.Length > maxLength)
        {
            return BodyTooLong;
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    i++;
                    continue;
                }

                return BodyInvalid; // lone high surrogate
            }

            if (char.IsLowSurrogate(c))
            {
                return BodyInvalid; // lone low surrogate
            }

            // C0 controls except tab and newline, DEL, and C1 controls. Includes NUL.
            if ((c < 0x20 && c != '\t' && c != '\n') || (c >= 0x7F && c <= 0x9F))
            {
                return BodyInvalid;
            }
        }

        normalized = text;
        return null;
    }

    /// <summary>Validates a post body with the post limit.</summary>
    /// <param name="raw">The text as sent.</param>
    /// <param name="normalized">The text to store, when valid.</param>
    /// <returns>Null when valid, otherwise an error code.</returns>
    public static string? ValidatePost(string? raw, out string normalized) =>
        Validate(raw, PostStore.MaxBodyLength, out normalized);
}
