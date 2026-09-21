namespace HookRelay.Rendering;

public interface IRazorViewRenderer
{
    Task<string> RenderToStringAsync(string viewPath, object? model = null, CancellationToken ct = default);
}
