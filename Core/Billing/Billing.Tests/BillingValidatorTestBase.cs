using BuildingBlocks.Application.Interfaces;
using BuildingBlocks.Tests;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Billing.Application;
using Billing.Domain.BillingProfiles;

namespace Billing.Tests;

public abstract class BillingValidatorTestBase : ValidatorTestBase
{
    protected Mock<IRepository<BillingProfile>> BillingProfileRepositoryMock { get; private set; } = null!;

    protected override void RegisterServices(IServiceCollection services)
    {
        services.AddBillingApplication();

        BillingProfileRepositoryMock = new Mock<IRepository<BillingProfile>>();
        UnitOfWorkMock.Setup(u => u.RepositoryFor<BillingProfile>()).Returns(BillingProfileRepositoryMock.Object);
    }

    protected void SetupBillingProfileExistsForPatient(Guid patientId)
    {
        BillingProfileRepositoryMock
            .Setup(r => r.ExistsAsync(It.IsAny<System.Linq.Expressions.Expression<Func<BillingProfile, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    protected void SetupBillingProfileNotExistsForPatient(Guid patientId)
    {
        BillingProfileRepositoryMock
            .Setup(r => r.ExistsAsync(It.IsAny<System.Linq.Expressions.Expression<Func<BillingProfile, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }
}
