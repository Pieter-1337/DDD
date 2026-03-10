using BuildingBlocks.Tests;
using Microsoft.Extensions.DependencyInjection;
using Billing.Application;
using Billing.Infrastructure.Persistence;

namespace Billing.Tests;

public class BillingDbTestBase : TestBase<BillingDbContext>
{
    protected override void RegisterBoundedContextServices(IServiceCollection services)
    {
        services.AddBillingApplication();
    }
}
