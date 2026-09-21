using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;

namespace HookRelay.Rendering;

public sealed class RazorViewRenderer : IRazorViewRenderer
{
    private readonly IRazorViewEngine _viewEngine;
    private readonly ITempDataProvider _tempDataProvider;
    private readonly IServiceScopeFactory _scopeFactory;

    public RazorViewRenderer(IRazorViewEngine viewEngine, ITempDataProvider tempDataProvider, IServiceScopeFactory scopeFactory)
    {
        _viewEngine = viewEngine;
        _tempDataProvider = tempDataProvider;
        _scopeFactory = scopeFactory;
    }

    public async Task<string> RenderToStringAsync(string viewPath, object? model = null, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

        var viewResult = _viewEngine.GetView(executingFilePath: null, viewPath, isMainPage: false);
        if (!viewResult.Success)
        {
            var searchedLocations = string.Join(", ", viewResult.SearchedLocations);
            throw new InvalidOperationException($"Could not find view '{viewPath}'. Searched: {searchedLocations}");
        }

        var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = model
        };

        await using (var writer = new StringWriter())
        {
            var viewContext = new ViewContext(
                actionContext,
                viewResult.View,
                viewData,
                new TempDataDictionary(httpContext, _tempDataProvider),
                writer,
                new HtmlHelperOptions());

            ct.ThrowIfCancellationRequested();
            await viewResult.View.RenderAsync(viewContext);
            return writer.ToString();
        }
    }
}
