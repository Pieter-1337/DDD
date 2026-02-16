using Microsoft.AspNetCore.Builder;
using Scalar.AspNetCore;

namespace BuildingBlocks.WebApplications.OpenApi;

public static class OpenApiExtensions
{
    public static IApplicationBuilder UseOpenApiWithScalar(this WebApplication app, string title)
    {
        app.MapOpenApi();
        app.MapScalarApiReference(options =>
        {
            options.WithTitle(title);
            options.WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
        });

        return app;
    }
}
