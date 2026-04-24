using Auth;
using BuildingBlocks.Enumerations;
using BuildingBlocks.Tests;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scheduling.Application.Patients.Commands;
using Shouldly;

namespace Scheduling.Tests.ApplicationTests.ValidatorTests;

[TestClass]
public class DeletePatientCommandValidatorTests : SchedulingValidatorTestBase
{
    [TestMethod]
    public async Task Invalid_When_PatientDoesNotExist()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        SetupUserRoles(AppRoles.Admin);
        SetupPatientNotExists(patientId);

        var command = new DeletePatientCommand { Id = patientId };

        // Act
        StartStopwatch();
        var result = await ValidatorFor<DeletePatientCommand>().ValidateAsync(command);
        StopStopwatch();

        // Assert
        result.Errors.ShouldContainValidation(nameof(DeletePatientCommand.Id), ErrorCode.NotFound.Value);
        result.Errors.Count.ShouldBe(1);
        ElapsedSeconds().ShouldBeLessThan(0.1M);
    }

    [TestMethod]
    public async Task Valid_When_PatientExists()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        SetupUserRoles(AppRoles.Admin);
        SetupPatientExists(patientId);

        var command = new DeletePatientCommand { Id = patientId };

        // Act
        StartStopwatch();
        var result = await ValidatorFor<DeletePatientCommand>().ValidateAsync(command);
        StopStopwatch();

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Errors.Count.ShouldBe(0);
        ElapsedSeconds().ShouldBeLessThan(0.1M);
    }

    [TestMethod]
    public async Task Valid_When_UserIsAdmin()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        SetupUserRoles(AppRoles.Admin);
        SetupPatientExists(patientId);
        var command = new DeletePatientCommand { Id = patientId };

        // Act
        var result = await ValidatorFor<DeletePatientCommand>().ValidateAsync(command);

        // Assert
        result.IsValid.ShouldBeTrue();
    }

    [TestMethod]
    public async Task Invalid_When_UserHasNoAllowedRole()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        SetupPatientExists(patientId);
        // No SetupUserRoles call — the mock user has no roles by default
        var command = new DeletePatientCommand { Id = patientId };

        // Act
        var result = await ValidatorFor<DeletePatientCommand>().ValidateAsync(command);

        // Assert
        result.Errors.ShouldContain(e => e.ErrorCode == ErrorCode.Forbidden.Value);
    }
}
