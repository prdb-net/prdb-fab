using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Scheduling;

namespace Prdb.Fab.Infrastructure.Automation;

public static class AutomationServiceCollectionExtensions
{
    public static IServiceCollection AddFabAutomation(this IServiceCollection services)
    {
        services.AddScoped<AutomationRuleSettings>();
        services.AddScoped<AutomaticEligibility>();
        services.AddScoped<WantedAutopilot>();
        services.AddScoped<WantedAutopilotRoutine>();
        services.AddScoped<IRoutine>(provider => provider.GetRequiredService<WantedAutopilotRoutine>());
        services.AddScoped<AutomaticDecisionRoutine>();
        services.AddScoped<IRoutine>(provider => provider.GetRequiredService<AutomaticDecisionRoutine>());
        return services;
    }
}
