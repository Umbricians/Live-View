#if UMBRACO_17
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Umbricians.LiveView.OpenApi;

/// <summary>
/// Adds the package's Management API document to the backoffice's Swagger UI, signed in as the backoffice user.
/// </summary>
internal sealed class LiveViewSwaggerGenOptions : IConfigureOptions<SwaggerGenOptions>
{
    public void Configure(SwaggerGenOptions options)
    {
        options.SwaggerDoc(Constants.ApiName, new OpenApiInfo { Title = LiveViewOpenApi.Title, Version = "1.0" });
        options.OperationFilter<LiveViewSecurityRequirementsOperationFilter>();
    }
}
#endif
