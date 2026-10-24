using Gamex.Messaging.Domain.Conversations;

namespace Gamex.Messaging.UnitTests;

public sealed class ConversationTests
{
    private static readonly DateTime T0 = new(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Participant Buyer = new(Guid.NewGuid(), "Buyer", null);
    private static readonly Participant Owner = new(Guid.NewGuid(), "Owner", "https://cdn.example/o.jpg");

    private static Conversation Start() => Conversation.Start(Guid.NewGuid(), "Hades", Buyer, Owner, T0);

    [Fact]
    public void Start_OwnerWithThemselves_Throws()
    {
        Should.Throw<InvalidOperationException>(() => Conversation.Start(Guid.NewGuid(), "Hades", Owner, Owner, T0));
    }

    [Fact]
    public void Other_ReturnsTheCounterpart()
    {
        var conversation = Start();

        conversation.Other(Buyer.Id).ShouldBe(Owner);
        conversation.Other(Owner.Id).Id.ShouldBe(Buyer.Id);
        conversation.IsParticipant(Guid.NewGuid()).ShouldBeFalse();
    }

    [Fact]
    public void Post_TrimsBodyAndMovesLastMessageAt()
    {
        var conversation = Start();

        var message = conversation.Post(Owner.Id, "Owner Renamed", "  Still available?  ", T0.AddMinutes(5));

        message.Body.ShouldBe("Still available?");
        message.ReadAt.ShouldBeNull();
        conversation.LastMessageAt.ShouldBe(T0.AddMinutes(5));
        conversation.RecipientName.ShouldBe("Owner Renamed");
    }

    [Theory]
    [InlineData("   ")]
    [InlineData(null)]
    public void Post_EmptyBody_Throws(string? body)
    {
        Should.Throw<ArgumentException>(() => Start().Post(Buyer.Id, "Buyer", body!, T0));
    }

    [Fact]
    public void Post_TooLong_Throws()
    {
        Should.Throw<ArgumentException>(() => Start().Post(Buyer.Id, "Buyer", new string('x', Conversation.MaxBodyLength + 1), T0));
    }

    [Fact]
    public void Post_ByStranger_Throws()
    {
        Should.Throw<InvalidOperationException>(() => Start().Post(Guid.NewGuid(), "Stranger", "hi", T0));
    }
}
