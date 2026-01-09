using BuildingBlocks.Tests;
using Microsoft.Extensions.DependencyInjection;
using Scheduling.Application;
using Scheduling.Infrastructure.Persistence;


namespace Scheduling.Tests
{
    public class SchedulingDbTestBase : TestBase<SchedulingDbContext>
    {
        protected override void RegisterBoundedContextServices(IServiceCollection services)
        {
            services.AddSchedulingApplication();
        }
    }
}
