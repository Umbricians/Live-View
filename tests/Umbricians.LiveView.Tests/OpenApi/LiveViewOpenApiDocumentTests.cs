using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Umbricians.LiveView.Controllers;
using Umbricians.LiveView.OpenApi;
using Umbricians.LiveView.Tests.Configuration;
#if UMBRACO_17
using Swashbuckle.AspNetCore.SwaggerGen;
#else
using Microsoft.AspNetCore.OpenApi;
#endif

namespace Umbricians.LiveView.Tests.OpenApi;

/// <summary>The package's own document in the backoffice's API docs, and the token "Try it out" sends.</summary>
public class LiveViewOpenApiDocumentTests
{
#if UMBRACO_17
    [Test]
    public void The_composer_adds_the_package_document_with_its_security_filter()
    {
        SwaggerGenOptions options = Services().GetRequiredService<IOptions<SwaggerGenOptions>>().Value;

        Assert.That(options.SwaggerGeneratorOptions.SwaggerDocs.Keys, Does.Contain(Constants.ApiName));
        Assert.That(options.SwaggerGeneratorOptions.SwaggerDocs[Constants.ApiName].Title, Is.EqualTo(LiveViewOpenApi.Title));
        Assert.That(options.OperationFilterDescriptors.Select(f => f.Type), Has.Member(typeof(LiveViewSecurityRequirementsOperationFilter)));
    }

    [Test]
    public void Operations_in_the_package_document_send_the_backoffice_users_token()
    {
        var operation = new OpenApiOperation();
        var method = typeof(DashboardController).GetMethod(nameof(DashboardController.Snapshot))!;
        var context = new OperationFilterContext(Description(method), null!, new SchemaRepository(Constants.ApiName), new OpenApiDocument(), method);

        new LiveViewSecurityRequirementsOperationFilter().Apply(operation, context);

        Assert.That(operation.Security ?? [], Has.Some.Matches<OpenApiSecurityRequirement>(requirement => requirement?.Keys.Any(scheme => scheme.Reference.Id == "Backoffice-User") == true));
    }
#else
    [Test]
    public void The_composer_adds_the_package_document()
    {
        OpenApiOptions options = Services().GetRequiredService<IOptionsMonitor<OpenApiOptions>>().Get(Constants.ApiName);

        Assert.That(options.DocumentName, Is.EqualTo(Constants.ApiName));
    }

    [Test]
    public void The_package_document_lists_the_package_controllers_only()
    {
        OpenApiOptions options = Services().GetRequiredService<IOptionsMonitor<OpenApiOptions>>().Get(Constants.ApiName);
        var method = typeof(DashboardController).GetMethod(nameof(DashboardController.Snapshot))!;

        Assert.That(options.ShouldInclude(Description(method)), Is.True);
        Assert.That(options.ShouldInclude(Description(typeof(object).GetMethod(nameof(ToString))!)), Is.False);
    }
#endif

    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        ChannelPrecedenceTests.ServicesOnlyBuilder.Create(services).AddLiveViewOpenApiDocument();
        return services.BuildServiceProvider();
    }

    // The description the API explorer gives an action, with the attributes MVC puts in its endpoint metadata.
    private static ApiDescription Description(MethodInfo method) => new()
    {
        ActionDescriptor = new ControllerActionDescriptor
        {
            MethodInfo = method,
            ControllerTypeInfo = method.DeclaringType!.GetTypeInfo(),
            EndpointMetadata = [.. method.DeclaringType!.GetCustomAttributes(inherit: true), .. method.GetCustomAttributes(inherit: true)],
        },
    };
}
