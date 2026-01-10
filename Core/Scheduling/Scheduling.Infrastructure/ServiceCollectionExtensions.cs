using BuildingBlocks.Application.Interfaces;
using BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scheduling.Infrastructure.Persistence;

namespace Scheduling.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSchedulingInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<SchedulingDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IUnitOfWork, UnitOfWork<SchedulingDbContext>>();

        return services;
    }
}
