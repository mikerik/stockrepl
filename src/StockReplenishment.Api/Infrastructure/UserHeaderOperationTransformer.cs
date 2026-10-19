using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using StockReplenishment.Contracts.Common;

namespace StockReplenishment.Api.Infrastructure;

/// <summary>
/// Documents the <see cref="ApiConventions.UserHeader"/> header on every API operation, so it shows up as an
/// input field in Swagger UI (e.g. to act as a reviewer when approving).
/// </summary>
internal sealed class UserHeaderOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = ApiConventions.UserHeader,
            In = ParameterLocation.Header,
            Required = false,
            Description = "Acting user (stand-in for authentication). Requests cannot be reviewed by their requester. Defaults to \"anonymous\".",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
            Example = System.Text.Json.Nodes.JsonValue.Create("Erik Johansson"),
        });
        return Task.CompletedTask;
    }
}
