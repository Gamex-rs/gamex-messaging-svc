using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.LocalStack;
using Testcontainers.MySql;

namespace Gamex.Messaging.IntegrationTests.Infrastructure;

/// <summary>API against MySQL and LocalStack SQS; tokens are signed here like the User Library Service does. Requires Docker.</summary>
public sealed class MessagingApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Issuer = "gamex-user-library";
    public const string Audience = "gamex";
    private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048));

    private readonly MySqlContainer _mySql = new MySqlBuilder("mysql:8.0").WithDatabase("messaging_db").Build();
    private readonly LocalStackContainer _localStack = new LocalStackBuilder("localstack/localstack:3").Build();
    private readonly ConcurrentQueue<JsonElement> _activityEvents = new();
    private readonly CancellationTokenSource _stop = new();

    public string ActivityEventsUrl { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_mySql.StartAsync(), _localStack.StartAsync());
        using (var sqs = CreateSqsClient())
        {
            ActivityEventsUrl = (await sqs.CreateQueueAsync("gamex-activity-events")).QueueUrl;
        }

        _ = Services;
        _ = Task.Run(DrainAsync);
    }

    private AmazonSQSClient CreateSqsClient() => new(
        new BasicAWSCredentials("test", "test"),
        new AmazonSQSConfig { ServiceURL = _localStack.GetConnectionString(), AuthenticationRegion = "eu-central-1" });

    /// <summary>Collects published events in memory so tests can wait for theirs without starving each other.</summary>
    private async Task DrainAsync()
    {
        using var sqs = CreateSqsClient();
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var response = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest { QueueUrl = ActivityEventsUrl, MaxNumberOfMessages = 10, WaitTimeSeconds = 1 }, _stop.Token);
                foreach (var message in response.Messages ?? [])
                {
                    _activityEvents.Enqueue(JsonDocument.Parse(message.Body).RootElement.Clone());
                    await sqs.DeleteMessageAsync(ActivityEventsUrl, message.ReceiptHandle, _stop.Token);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public async Task<JsonElement?> WaitForEventAsync(Func<JsonElement, bool> match, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            if (_activityEvents.FirstOrDefault(match) is { ValueKind: not JsonValueKind.Undefined } found)
            {
                return found;
            }

            await Task.Delay(100);
        }

        return null;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("DB_CONNECTION_STRING", _mySql.GetConnectionString());
        builder.UseSetting("DB_MIGRATE_ON_STARTUP", "true");
        builder.UseSetting("JWT_PUBLIC_KEY", SigningKey.Rsa.ExportSubjectPublicKeyInfoPem());
        builder.UseSetting("JWT_ISSUER", Issuer);
        builder.UseSetting("JWT_AUDIENCE", Audience);
        builder.UseSetting("AWS_SERVICE_URL", _localStack.GetConnectionString());
        builder.UseSetting("SQS_ACTIVITY_EVENTS_URL", ActivityEventsUrl);
        builder.UseSetting("SEND_RATE_LIMIT_PER_MINUTE", "100000");
    }

    public TestUser CreateUser(string name)
    {
        var id = Guid.CreateVersion7();
        var token = AccessToken(id, name);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new TestUser(id, name, token, client);
    }

    /// <summary>Access token as issued by the User Library Service.</summary>
    public static string AccessToken(Guid userId, string name) => Handler.CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Issuer,
        Audience = Audience,
        Expires = DateTime.UtcNow.AddMinutes(15),
        Subject = new ClaimsIdentity([new Claim("sub", userId.ToString()), new Claim("name", name), new Claim("role", "user")]),
        SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256),
    });

    /// <summary>Listing contact token as issued by <c>GET /listings/{id}</c> of the User Library Service.</summary>
    public static string ContactToken(Guid listingId, TestUser owner, string gameTitle) => Handler.CreateToken(new SecurityTokenDescriptor
    {
        TokenType = "gx-contact+jwt",
        Issuer = Issuer,
        Audience = "gamex-listing-contact",
        Expires = DateTime.UtcNow.AddHours(24),
        Claims = new Dictionary<string, object>
        {
            ["listing_id"] = listingId.ToString(),
            ["owner_id"] = owner.Id.ToString(),
            ["owner_name"] = owner.Name,
            ["owner_avatar"] = "https://cdn.gamex.test/avatar.jpg",
            ["game_title"] = gameTitle,
        },
        SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256),
    });

    private static JsonWebTokenHandler Handler => new();

    /// <summary>SignalR client over the in-memory test server (long polling; TestServer has no real sockets).</summary>
    public async Task<HubConnection> ConnectHubAsync(TestUser user)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(Server.BaseAddress, "/hubs/messaging"), options =>
            {
                options.HttpMessageHandlerFactory = _ => Server.CreateHandler();
                options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(user.Token);
            })
            .Build();
        await connection.StartAsync();
        return connection;
    }

    public override async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _stop.Dispose();
        await base.DisposeAsync();
        await _mySql.DisposeAsync();
        await _localStack.DisposeAsync();
    }
}

public sealed record TestUser(Guid Id, string Name, string Token, HttpClient Client);

[CollectionDefinition(Name)]
public sealed class MessagingApiFixture : ICollectionFixture<MessagingApiFactory>
{
    public const string Name = "MessagingApi";
}
