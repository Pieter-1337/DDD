using BuildingBlocks.Enumerations;
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
    public async Task Invalid_When_StatusIsNull()
    {
        // Arrange
        var query = new GetAllPatientsQuery
        {
            Status = null!
        };

        // Act
        StartStopwatch();
        var result = await ValidatorFor<GetAllPatientsQuery>().ValidateAsync(query);
        StopStopwatch();

        // Assert
        result.Errors.ShouldContainValidation(nameof(GetAllPatientsQuery.Status), ErrorCode.InvalidStatus.Value);
        result.Errors.Count.ShouldBe(1);
        ElapsedSeconds().ShouldBeLessThan(0.1M);
    }

    [TestMethod]
    public async Task Invalid_When_StatusIsInvalidValue()
    {
        // Arrange - Invalid SmartEnum value is now caught by validator
        var query = new GetAllPatientsQuery
        {
            Status = "InvalidStatus"
        };

        // Act
        StartStopwatch();
        var result = await ValidatorFor<GetAllPatientsQuery>().ValidateAsync(query);
        StopStopwatch();

        // Assert
        result.Errors.ShouldContainValidation(nameof(GetAllPatientsQuery.Status), ErrorCode.InvalidStatus.Value);
        result.Errors.Count.ShouldBe(1);
        ElapsedSeconds().ShouldBeLessThan(0.1M);
    }

    [TestMethod]
    public async Task Valid_When_StatusIsActive()
    {
        // Arrange
        var query = new GetAllPatientsQuery { Status = PatientStatus.Active.Name };

        // Act
        var result = await ValidatorFor<GetAllPatientsQuery>().ValidateAsync(query);

        // Assert
        result.IsValid.ShouldBeTrue();
    }

    [TestMethod]
    public async Task Valid_When_StatusIsDeleted()
    {
        // Arrange
        var query = new GetAllPatientsQuery { Status = PatientStatus.Deleted.Name };

        // Act
        var result = await ValidatorFor<GetAllPatientsQuery>().ValidateAsync(query);

        // Assert
        result.IsValid.ShouldBeTrue();
    }

    [TestMethod]
    public async Task Valid_When_StatusIsSuspended()
    {
        // Arrange
        var query = new GetAllPatientsQuery { Status = PatientStatus.Suspended.Name };

        // Act
        var result = await ValidatorFor<GetAllPatientsQuery>().ValidateAsync(query);

        // Assert
        result.IsValid.ShouldBeTrue();
    }
}
