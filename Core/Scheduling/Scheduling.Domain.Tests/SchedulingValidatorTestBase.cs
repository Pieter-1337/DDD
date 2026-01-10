using BuildingBlocks.Application.Interfaces;
using BuildingBlocks.Tests;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Scheduling.Application;
using Scheduling.Domain.Patients;

namespace Scheduling.Tests;

/// <summary>
/// Base class for validator unit tests in the Scheduling bounded context.
/// Uses mocked IUnitOfWork - configure UnitOfWorkMock for validators that need entity existence checks.
/// </summary>
public abstract class SchedulingValidatorTestBase : ValidatorTestBase
{
    protected Mock<IRepository<Patient>> PatientRepositoryMock { get; private set; } = null!;

    protected override void RegisterServices(IServiceCollection services)
    {
        services.AddSchedulingApplication();

        // Setup repository mocks for validators that need them
        PatientRepositoryMock = new Mock<IRepository<Patient>>();
        UnitOfWorkMock.Setup(u => u.RepositoryFor<Patient>()).Returns(PatientRepositoryMock.Object);
    }

    /// <summary>
    /// Configure the mock to return true for ExistsAsync for the given patient ID.
    /// </summary>
    protected void SetupPatientExists(Guid patientId)
    {
        PatientRepositoryMock
            .Setup(r => r.ExistsAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    /// <summary>
    /// Configure the mock to return false for ExistsAsync for the given patient ID.
    /// </summary>
    protected void SetupPatientNotExists(Guid patientId)
    {
        PatientRepositoryMock
            .Setup(r => r.ExistsAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }
}
