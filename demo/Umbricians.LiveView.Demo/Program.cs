
using Umbricians.LiveView.Demo.DemoMembers;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers()
    .Build();

WebApplication app = builder.Build();


await app.BootUmbracoAsync();


app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();
    })
    .WithEndpoints(u =>
    {
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();

        // Demo only: lets member pages receive Live View messages instantly.
        u.EndpointRouteBuilder.MapHub<MemberMessagesHub>(MemberMessagesHub.Path);
    });

await app.RunAsync();
