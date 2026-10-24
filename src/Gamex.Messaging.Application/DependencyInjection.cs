using Gamex.Messaging.Application.Conversations;
using Microsoft.Extensions.DependencyInjection;

namespace Gamex.Messaging.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IConversationService, ConversationService>();
        return services;
    }
}
