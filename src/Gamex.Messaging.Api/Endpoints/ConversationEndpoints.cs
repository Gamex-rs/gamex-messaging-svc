using System.Security.Claims;
using Gamex.Messaging.Api.Auth;
using Gamex.Messaging.Application.Common;
using Gamex.Messaging.Application.Conversations;

namespace Gamex.Messaging.Api.Endpoints;

internal static class ConversationEndpoints
{
    private const string TypeBase = "https://gamex.rs/errors/";

    public sealed record StartRequest(Guid? ListingId, string? ContactToken, string? InitialMessage);

    public sealed record SendRequest(string? Body);

    public static IEndpointRouteBuilder MapConversationEndpoints(this IEndpointRouteBuilder app)
    {
        var conversations = app.MapGroup("/api/v1/conversations").WithTags("Conversations").RequireAuthorization();

        conversations.MapGet("/", async (ClaimsPrincipal user, IConversationService service, CancellationToken ct, string? cursor = null, int? pageSize = null) =>
            ToResult(await service.ListAsync(user.UserId(), cursor, pageSize, ct)));

        conversations.MapPost("/", async (StartRequest request, ClaimsPrincipal user, IConversationService service, CancellationToken ct) =>
        {
            var result = await service.StartAsync(Caller(user), new StartConversationCommand(request.ListingId, request.ContactToken, request.InitialMessage), ct);
            return result.Status == OperationStatus.Ok
                ? Results.Created($"/api/v1/conversations/{result.Value!.Id}", result.Value)
                : ToResult(result);
        }).RequireRateLimiting(AuthSetup.SendRateLimit);

        conversations.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, IConversationService service, CancellationToken ct) =>
            ToResult(await service.GetAsync(user.UserId(), id, ct)));

        conversations.MapPut("/{id:guid}/read", async (Guid id, ClaimsPrincipal user, IConversationService service, CancellationToken ct) =>
        {
            var result = await service.MarkReadAsync(user.UserId(), id, ct);
            return result.Status == OperationStatus.Ok ? Results.NoContent() : ToResult(result);
        });

        conversations.MapGet("/{id:guid}/messages", async (Guid id, ClaimsPrincipal user, IConversationService service, CancellationToken ct, string? cursor = null, int? pageSize = null) =>
            ToResult(await service.GetMessagesAsync(user.UserId(), id, cursor, pageSize, ct)));

        conversations.MapPost("/{id:guid}/messages", async (Guid id, SendRequest request, ClaimsPrincipal user, IConversationService service, CancellationToken ct) =>
        {
            var result = await service.SendAsync(Caller(user), id, request.Body, ct);
            return result.Status == OperationStatus.Ok
                ? Results.Created($"/api/v1/conversations/{id}/messages", result.Value)
                : ToResult(result);
        }).RequireRateLimiting(AuthSetup.SendRateLimit);

        return app;
    }

    private static Caller Caller(ClaimsPrincipal user) => new(user.UserId(), user.DisplayName());

    private static IResult ToResult<T>(OperationResult<T> result) => result.Status switch
    {
        OperationStatus.Ok => Results.Ok(result.Value),
        OperationStatus.Invalid => Results.ValidationProblem(result.Errors!.ToDictionary(), type: TypeBase + "validation", title: "One or more fields are invalid."),
        OperationStatus.NotFound => Results.Problem(type: TypeBase + "not-found", title: "Not found", detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
        OperationStatus.Forbidden => Results.Problem(type: TypeBase + "forbidden", title: "Access denied", detail: result.Detail, statusCode: StatusCodes.Status403Forbidden),

        // SPEC-messaging §3.1: an existing conversation is returned with the 409.
        OperationStatus.Conflict => Results.Problem(
            type: TypeBase + "conflict", title: "Conflict", detail: result.Detail, statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?> { ["conversation"] = result.Value }),
        OperationStatus.Unauthorized => Results.Problem(type: TypeBase + "unauthorized", title: "Unauthorized", detail: result.Detail, statusCode: StatusCodes.Status401Unauthorized),
        _ => throw new ArgumentOutOfRangeException(nameof(result), result.Status, null),
    };
}
