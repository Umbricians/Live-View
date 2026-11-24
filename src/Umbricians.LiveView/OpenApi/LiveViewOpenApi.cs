using Umbraco.Cms.Core.DependencyInjection;
#if UMBRACO_17
using Microsoft.Extensions.DependencyInjection;
#else
using Umbraco.Cms.Api.Common.OpenApi;
using Umbraco.Cms.Api.Management.OpenApi;
#endif

namespace Umbricians.LiveView.OpenApi;

/// <summary>
/// Adds the package's Management API document to the backoffice's API docs, where "Try it out" sends the backoffice
/// user's token. Umbraco 17 builds its docs with Swashbuckle; Umbraco 18 moved to ASP.NET Core's OpenAPI documents.
/// </summary>
internal static class LiveViewOpenApi
{
    /// <summary>The document's title in the backoffice's API docs.</summary>
    public const string Title = "Umbricians Live View";

    public static IUmbracoBuilder AddLiveViewOpenApiDocument(this IUmbracoBuilder builder)
    {
#if UMBRACO_17
        builder.Services.ConfigureOptions<LiveViewSwaggerGenOptions>();
#else
        builder.AddBackOfficeOpenApiDocument(Constants.ApiName, document => document
            .WithTitle(Title)
            .WithBackOfficeAuthentication());
#endif
        return builder;
    }
}
