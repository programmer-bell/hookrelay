using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace HookRelay.Middleware;

public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        context.Response.Headers[HeaderNames.XFrameOptions] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";

        return _next(context);
    }
}
