using BuildingBlocks.Tests;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scheduling.Application.Patients.Queries;
using Scheduling.Domain.Patients;
using Shouldly;

namespace Scheduling.Tests.ApplicationTests.ValidatorTests;

[TestClass]
public class GetAllPatientsQueryValidatorTests : SchedulingValidatorTestBase
{
    [TestMethod]
    public async Task Invalid_When_StatusIsInvalidEnum()
    {
        // Arrange
        var query = new GetAllPatientsQuery
        {
            Status = (PatientStatus)999
        };

        // Act
        StartStopwatch();
        var result = await ValidatorFor<GetAllPatientsQuery>().ValidateAsync(query);
        StopStopwatch();

        // Assert
        result.Errors.ShouldContainValidation(nameof(GetAllPatientsQuery.Status), VALIDATION_ENUM_VALIDATOR);
        result.Errors.Count.ShouldBe(1);
        ElapsedSeconds().ShouldBeLessThan(0.1M);
    }

    [TestMethod]
    public async Task Valid_When_StatusIsActive()
    {
        // Arrange
        var query = new GetAllPatientsQuery { Status = PatientStatus.Active };

        // Act
        var result = await ValidatorFor<GetAllPatientsQuery>().ValidateAsync(query);

        // Assert
        result.IsValid.ShouldBeTrue();
    }

    [TestMethod]
    public async Task Valid_When_StatusIsInactive()
    {
        // Arrange
        var query = new GetAllPatientsQuery { Status = PatientStatus.Inactive };

        // Act
        var result = await ValidatorFor<GetAllPatientsQuery>().ValidateAsync(query);

        // Assert
        result.IsValid.ShouldBeTrue();
    }

    [TestMethod]
    public async Task Valid_When_StatusIsSuspended()
    {
        // Arrange
        var query = new GetAllPatientsQuery { Status = PatientStatus.Suspended };

        // Act
        var result = await ValidatorFor<GetAllPatientsQuery>().ValidateAsync(query);

        // Assert
        result.IsValid.ShouldBeTrue();
    }
}
