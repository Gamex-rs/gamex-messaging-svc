using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gamex.Messaging.Application.Conversations;
using Gamex.Messaging.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR.Client;

namespace Gamex.Messaging.IntegrationTests;

[Collection(MessagingApiFixture.Name)]
public sealed class ConversationTests(MessagingApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<ConversationDto> StartAsync(TestUser buyer, Guid listingId, TestUser owner, string title = "Hades", string message = "Is it still available?")
    {
        var response = await buyer.Client.PostAsJsonAsync("/api/v1/conversations", new
        {
            listingId,
            contactToken = MessagingApiFactory.ContactToken(listingId, owner, title),
            initialMessage = message,
        }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<ConversationDto>(Ct))!;
    }

    [Fact]
    public async Task Start_CreatesConversationAndPublishesMessageSent()
    {
        var owner = factory.CreateUser("Ana Owner");
        var buyer = factory.CreateUser("Marko Buyer");
        var listingId = Guid.NewGuid();

        var conversation = await StartAsync(buyer, listingId, owner);

        conversation.ListingId.ShouldBe(listingId);
        conversation.GameTitle.ShouldBe("Hades");
        conversation.OtherUser.Id.ShouldBe(owner.Id);
        conversation.OtherUser.DisplayName.ShouldBe("Ana Owner");
        conversation.OtherUser.AvatarUrl.ShouldBe("https://cdn.gamex.test/avatar.jpg");

        var sent = await factory.WaitForEventAsync(e => e.GetProperty("conversationId").GetGuid() == conversation.Id);
        sent.ShouldNotBeNull();
        var evt = sent.Value;
        evt.GetProperty("eventType").GetString().ShouldBe("message.sent");
        evt.GetProperty("senderId").GetGuid().ShouldBe(buyer.Id);
        evt.GetProperty("senderName").GetString().ShouldBe("Marko Buyer");
        evt.GetProperty("recipientId").GetGuid().ShouldBe(owner.Id);
        evt.GetProperty("gameTitle").GetString().ShouldBe("Hades");
        evt.TryGetProperty("body", out _).ShouldBeFalse("the body is not part of the event");
    }

    [Fact]
    public async Task Start_Twice_Returns409WithExistingConversation()
    {
        var owner = factory.CreateUser("Owner");
        var buyer = factory.CreateUser("Buyer");
        var listingId = Guid.NewGuid();
        var first = await StartAsync(buyer, listingId, owner);

        var again = await buyer.Client.PostAsJsonAsync("/api/v1/conversations",
            new { listingId, contactToken = MessagingApiFactory.ContactToken(listingId, owner, "Hades"), initialMessage = "Hello again" }, Ct);

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await again.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("conversation").GetProperty("id").GetGuid().ShouldBe(first.Id);
    }

    [Fact]
    public async Task Start_OwnListing_Returns400()
    {
        var owner = factory.CreateUser("Owner");
        var listingId = Guid.NewGuid();

        var response = await owner.Client.PostAsJsonAsync("/api/v1/conversations",
            new { listingId, contactToken = MessagingApiFactory.ContactToken(listingId, owner, "Hades"), initialMessage = "Me?" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("access-token")]
    [InlineData("mismatch")]
    public async Task Start_BadContactToken_Returns400(string kind)
    {
        var owner = factory.CreateUser("Owner");
        var buyer = factory.CreateUser("Buyer");
        var listingId = Guid.NewGuid();
        var token = kind switch
        {
            "garbage" => "x.y.z",
            "access-token" => owner.Token,
            _ => MessagingApiFactory.ContactToken(Guid.NewGuid(), owner, "Hades"),
        };

        var response = await buyer.Client.PostAsJsonAsync("/api/v1/conversations", new { listingId, contactToken = token, initialMessage = "Hi" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task List_ShowsLastMessageUnreadAndOrdersByActivity()
    {
        var owner = factory.CreateUser("Owner");
        var older = await StartAsync(factory.CreateUser("Buyer 1"), Guid.NewGuid(), owner, "Celeste", "First!");
        var newer = await StartAsync(factory.CreateUser("Buyer 2"), Guid.NewGuid(), owner, "Inside", "Second!");

        var page = await owner.Client.GetFromJsonAsync<CursorPage<ConversationSummaryDto>>("/api/v1/conversations", Ct);

        page!.Items.Select(c => c.Id).ShouldBe([newer.Id, older.Id]);
        var top = page.Items[0];
        top.GameTitle.ShouldBe("Inside");
        top.OtherUser.DisplayName.ShouldBe("Buyer 2");
        top.LastMessage!.Body.ShouldBe("Second!");
        top.LastMessage.IsOwn.ShouldBeFalse();
        top.UnreadCount.ShouldBe(1);

        // Replying to the older conversation moves it to the top.
        (await owner.Client.PostAsJsonAsync($"/api/v1/conversations/{older.Id}/messages", new { body = "Yes, still available" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var reordered = await owner.Client.GetFromJsonAsync<CursorPage<ConversationSummaryDto>>("/api/v1/conversations?pageSize=1", Ct);
        reordered!.Items.ShouldHaveSingleItem().Id.ShouldBe(older.Id);
        reordered.Items[0].LastMessage!.IsOwn.ShouldBeTrue();
        reordered.NextCursor.ShouldNotBeNull();
        var next = await owner.Client.GetFromJsonAsync<CursorPage<ConversationSummaryDto>>($"/api/v1/conversations?pageSize=1&cursor={reordered.NextCursor}", Ct);
        next!.Items.ShouldHaveSingleItem().Id.ShouldBe(newer.Id);
    }

    [Fact]
    public async Task Messages_PaginateNewestFirstWithMessageIdCursor()
    {
        var owner = factory.CreateUser("Owner");
        var buyer = factory.CreateUser("Buyer");
        var conversation = await StartAsync(buyer, Guid.NewGuid(), owner, message: "m0");
        for (var i = 1; i <= 4; i++)
        {
            await (i % 2 == 0 ? buyer : owner).Client.PostAsJsonAsync($"/api/v1/conversations/{conversation.Id}/messages", new { body = $"m{i}" }, Ct);
        }

        var first = await buyer.Client.GetFromJsonAsync<CursorPage<MessageDto>>($"/api/v1/conversations/{conversation.Id}/messages?pageSize=3", Ct);
        first!.Items.Select(m => m.Body).ShouldBe(["m4", "m3", "m2"]);
        first.Items[0].IsOwn.ShouldBeTrue();
        first.NextCursor.ShouldBe(first.Items[^1].Id.ToString());

        var second = await buyer.Client.GetFromJsonAsync<CursorPage<MessageDto>>($"/api/v1/conversations/{conversation.Id}/messages?pageSize=3&cursor={first.NextCursor}", Ct);
        second!.Items.Select(m => m.Body).ShouldBe(["m1", "m0"]);
        second.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task NonParticipant_Gets403_UnknownGets404()
    {
        var conversation = await StartAsync(factory.CreateUser("Buyer"), Guid.NewGuid(), factory.CreateUser("Owner"));
        var stranger = factory.CreateUser("Stranger");

        (await stranger.Client.GetAsync(new Uri($"/api/v1/conversations/{conversation.Id}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await stranger.Client.GetAsync(new Uri($"/api/v1/conversations/{conversation.Id}/messages", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var post = await stranger.Client.PostAsJsonAsync($"/api/v1/conversations/{conversation.Id}/messages", new { body = "let me in" }, Ct);
        post.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await post.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Detail.ShouldBe("You are not a participant in this conversation");
        (await stranger.Client.GetAsync(new Uri($"/api/v1/conversations/{Guid.NewGuid()}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Send_TooLongBody_Returns400()
    {
        var buyer = factory.CreateUser("Buyer");
        var conversation = await StartAsync(buyer, Guid.NewGuid(), factory.CreateUser("Owner"));

        var response = await buyer.Client.PostAsJsonAsync($"/api/v1/conversations/{conversation.Id}/messages", new { body = new string('x', 2001) }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Requests_WithoutToken_Return401()
    {
        (await factory.CreateClient().GetAsync(new Uri("/api/v1/conversations", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SignalR_PushesMessagesAndReadReceipts()
    {
        var owner = factory.CreateUser("Owner");
        var buyer = factory.CreateUser("Buyer");
        await using var ownerHub = await factory.ConnectHubAsync(owner);
        await using var buyerHub = await factory.ConnectHubAsync(buyer);
        var received = new TaskCompletionSource<RealtimeMessageDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        ownerHub.On<RealtimeMessageDto>("MessageReceived", m => received.TrySetResult(m));
        buyerHub.On<JsonElement>("ConversationRead", e => read.TrySetResult(e));

        var conversation = await StartAsync(buyer, Guid.NewGuid(), owner, message: "Ping over SignalR");

        var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        message.ConversationId.ShouldBe(conversation.Id);
        message.SenderName.ShouldBe("Buyer");
        message.Body.ShouldBe("Ping over SignalR");

        (await owner.Client.PutAsync(new Uri($"/api/v1/conversations/{conversation.Id}/read", UriKind.Relative), null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await read.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct)).GetProperty("conversationId").GetGuid().ShouldBe(conversation.Id);

        var detail = await owner.Client.GetFromJsonAsync<ConversationDto>($"/api/v1/conversations/{conversation.Id}", Ct);
        detail!.UnreadCount.ShouldBe(0);
        var messages = await buyer.Client.GetFromJsonAsync<CursorPage<MessageDto>>($"/api/v1/conversations/{conversation.Id}/messages", Ct);
        messages!.Items.ShouldHaveSingleItem().ReadAt.ShouldNotBeNull();
    }
}
