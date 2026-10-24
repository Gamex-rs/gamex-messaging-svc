using Gamex.Messaging.Domain.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gamex.Messaging.Infrastructure.Persistence.Configurations;

internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.ListingGameTitle).HasMaxLength(255).IsRequired();
        builder.Property(c => c.InitiatorName).HasMaxLength(100).IsRequired();
        builder.Property(c => c.InitiatorAvatarUrl).HasMaxLength(500);
        builder.Property(c => c.RecipientName).HasMaxLength(100).IsRequired();
        builder.Property(c => c.RecipientAvatarUrl).HasMaxLength(500);

        // One conversation per user per listing (SPEC-messaging §5).
        builder.HasIndex(c => new { c.ListingId, c.InitiatorId }).IsUnique();
        builder.HasIndex(c => new { c.InitiatorId, c.LastMessageAt });
        builder.HasIndex(c => new { c.RecipientId, c.LastMessageAt });
    }
}

internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Body).HasColumnType("text").IsRequired();
        builder.HasIndex(m => new { m.ConversationId, m.CreatedAt });
        builder.HasIndex(m => new { m.ConversationId, m.ReadAt });
        builder.HasOne<Conversation>().WithMany().HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
    }
}
