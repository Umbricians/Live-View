using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using Umbricians.LiveView.Composers;
using Umbricians.LiveView.Controllers;
using Umbricians.LiveView.OpenApi;

namespace Umbricians.LiveView.Tests.OpenApi;

/// <summary>The package's own document in the backoffice's Swagger UI, and the token "Try it out" sends.</summary>
public class LiveViewSwaggerDocumentTests
{
    [Test]
    public void The_composer_adds_the_package_document_with_its_security_filter()
    {
        var services = new ServiceCollection();
        services.AddLiveView(new ConfigurationBuilder().Build());

        SwaggerGenOptions options = services.BuildServiceProvider().GetRequiredService<IOptions<SwaggerGenOptions>>().Value;

        Assert.That(options.SwaggerGeneratorOptions.SwaggerDocs.Keys, Does.Contain(Constants.ApiName));
        Assert.That(options.OperationFilterDescriptors.Select(f => f.Type), Has.Member(typeof(LiveViewSecurityRequirementsOperationFilter)));
    }

    [Test]
    public void Operations_in_the_package_document_send_the_backoffice_users_token()
    {
        var operation = new OpenApiOperation();

        new LiveViewSecurityRequirementsOperationFilter().Apply(operation, Context(nameof(DashboardController.Snapshot)));

        Assert.That(operation.Security ?? [], Has.Some.Matches<OpenApiSecurityRequirement>(requirement => requirement.Keys.Any(scheme => scheme.Reference.Id == "Backoffice-User")));
    }

    private static OperationFilterContext Context(string action)
    {
        var method = typeof(DashboardController).GetMethod(action)!;
        var description = new ApiDescription
        {
            ActionDescriptor = new ControllerActionDescriptor
            {
                MethodInfo = method,
                ControllerTypeInfo = typeof(DashboardController).GetTypeInfo(),
            },
        };

        return new OperationFilterContext(description, null!, new SchemaRepository(Constants.ApiName), new OpenApiDocument(), method);
    }
}
