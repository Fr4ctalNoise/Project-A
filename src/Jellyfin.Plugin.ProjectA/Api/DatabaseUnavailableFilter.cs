// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Fr4ctalNoise and Project A contributors.

using Jellyfin.Plugin.ProjectA.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ProjectA.Api;

/// <summary>
/// Turns a database failure in any Project A endpoint into a fixed 503 answer
/// <c>{"Error":"database_unavailable","Problem":"locked"}</c>. The page shows a "try again"
/// message; the details (which may include file paths) go only to Jellyfin's log.
/// Also answers ASP.NET's own request errors (an oversized body while reading the JSON) with
/// their real status, 413, instead of letting Jellyfin's exception handler turn them into 500.
/// Everything else is left alone for Jellyfin's handling.
/// </summary>
public sealed class DatabaseUnavailableFilter : IExceptionFilter
{
    private readonly ILogger<DatabaseUnavailableFilter> _logger;

    /// <summary>Initializes a new instance of the <see cref="DatabaseUnavailableFilter"/> class.</summary>
    /// <param name="logger">Logger.</param>
    public DatabaseUnavailableFilter(ILogger<DatabaseUnavailableFilter> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void OnException(ExceptionContext context)
    {
        string problem;
        switch (context.Exception)
        {
            case StoreUnavailableException unavailable:
                problem = unavailable.Problem;
                break;
            case SqliteException:
                problem = SocialStore.Classify(context.Exception);
                break;
            case BadHttpRequestException bad:
                context.Result = new StatusCodeResult(bad.StatusCode);
                context.ExceptionHandled = true;
                return;
            default:
                return;
        }

        _logger.LogWarning(context.Exception, "Project A: database error ({Problem}) in {Path}", problem, context.HttpContext.Request.Path);
        context.Result = new ObjectResult(new ErrorResponse("database_unavailable", problem))
        {
            StatusCode = StatusCodes.Status503ServiceUnavailable,
        };
        context.ExceptionHandled = true;
    }
}

/// <summary>An error answer: a fixed code, never exception text.</summary>
/// <param name="Error">What went wrong, as a fixed code.</param>
/// <param name="Problem">Optional detail code.</param>
public sealed record ErrorResponse(string Error, string? Problem = null);
