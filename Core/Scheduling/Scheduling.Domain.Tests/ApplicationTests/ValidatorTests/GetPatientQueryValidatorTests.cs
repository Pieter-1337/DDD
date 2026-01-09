using BuildingBlocks.Tests;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scheduling.Application.Patients.Queries;
using Shouldly;

namespace Scheduling.Tests.ApplicationTests.ValidatorTests;

[TestClass]
public class GetPatientQueryValidatorTests : SchedulingValidatorTestBase
{
    [TestMethod]
    public async Task Invalid_When_PatientDoesNotExist()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        SetupPatientNotExists(patientId);

        var query = new GetPatientQuery { Id = patientId };

        // Act
        StartStopwatch();
        var result = await ValidatorFor<GetPatientQuery>().ValidateAsync(query);
        StopStopwatch();

        // Assert
        result.Errors.ShouldContainValidation(nameof(GetPatientQuery.Id), VALIDATION_ASYNCPREDICATE_VALIDATOR);
        result.Errors.Count.ShouldBe(1);
        ElapsedSeconds().ShouldBeLessThan(0.1M);
    }

    [TestMethod]
    public async Task Valid_When_PatientExists()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        SetupPatientExists(patientId);

        var query = new GetPatientQuery { Id = patientId };

        // Act
        StartStopwatch();
        var result = await ValidatorFor<GetPatientQuery>().ValidateAsync(query);
        StopStopwatch();

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Errors.Count.ShouldBe(0);
        ElapsedSeconds().ShouldBeLessThan(0.1M);
    }
}
