using Auth;
using BuildingBlocks.Enumerations;
using BuildingBlocks.Tests;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scheduling.Application.Patients.Commands;
using Scheduling.Domain.Patients;
using Shouldly;

namespace Scheduling.Tests.ApplicationTests.ValidatorTests;

[TestClass]
public class CreatePatientCommandValidatorTests : SchedulingValidatorTestBase
{
    [TestMethod]
    public async Task Invalid_When_PatientIsNull()
    {
        // Arrange
        var command = new CreatePatientCommand(null!);
        SetupUserRoles(AppRoles.Admin);

        // Act
        StartStopwatch();
        var result = await ValidatorFor<CreatePatientCommand>().ValidateAsync(command);
        StopStopwatch();

        // Assert
        result.Errors.ShouldContainValidation(nameof(CreatePatientCommand.Patient), VALIDATION_NOT_NULL_VALIDATOR);
        result.Errors.Count.ShouldBe(1);
        ElapsedSeconds().ShouldBeLessThan(0.1M);
    }

    [TestMethod]
    public async Task Invalid_When_RequiredFieldsAreEmpty()
    {
        // Arrange
        SetupUserRoles(AppRoles.Admin);

        var command = new CreatePatientCommand(new CreatePatientRequest
        {
            FirstName = "",
            LastName = "",
            Email = "",
            DateOfBirth = default,
            Status = null!
        });

        // Act
        StartStopwatch();
        var result = await ValidatorFor<CreatePatientCommand>().ValidateAsync(command);
        StopStopwatch();

        // Assert
        result.Errors.ShouldContainValidation(nameof(CreatePatientRequest.FirstName), ErrorCode.FirstNameRequired.Value);
        result.Errors.ShouldContainValidation(nameof(CreatePatientRequest.LastName), ErrorCode.LastNameRequired.Value);
        result.Errors.ShouldContainValidation(nameof(CreatePatientRequest.Email), ErrorCode.EmailRequired.Value);
        result.Errors.ShouldContainValidation(nameof(CreatePatientRequest.Email), ErrorCode.InvalidEmail.Value);
        result.Errors.ShouldContainValidation(nameof(CreatePatientRequest.DateOfBirth), ErrorCode.DateOfBirthRequired.Value);
        result.Errors.ShouldContainValidation(nameof(CreatePatientRequest.Status), ErrorCode.InvalidStatus.Value);

        ElapsedSeconds().ShouldBeLessThan(0.1M);
    }

    [TestMethod]
    public async Task Invalid_When_EmailIsInvalid()
    {
        // Arrange
        SetupUserRoles(AppRoles.Admin);

        var command = new CreatePatientCommand(new CreatePatientRequest
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "not-an-email",
            DateOfBirth = new DateTime(1990, 1, 15),
            Status = PatientStatus.Active.Name
        });

        // Act
        var result = await ValidatorFor<CreatePatientCommand>().ValidateAsync(command);

        // Assert
        result.Errors.ShouldContainValidation(nameof(CreatePatientRequest.Email), ErrorCode.InvalidEmail.Value);
        result.Errors.Count.ShouldBe(1);
    }

    [TestMethod]
    public async Task Invalid_When_StatusIsInvalid()
    {
        // Arrange
        SetupUserRoles(AppRoles.Admin);

        var command = new CreatePatientCommand(new CreatePatientRequest
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john.doe@example.com",
            DateOfBirth = new DateTime(1990, 1, 15),
            Status = "InvalidStatus"
        });

        // Act
        var result = await ValidatorFor<CreatePatientCommand>().ValidateAsync(command);

        // Assert
        result.Errors.ShouldContainValidation(nameof(CreatePatientRequest.Status), ErrorCode.InvalidStatus.Value);
        result.Errors.Count.ShouldBe(1);
    }

    [TestMethod]
    public async Task Valid_When_AllFieldsAreValid()
    {
        // Arrange
        SetupUserRoles(AppRoles.Admin);

        var command = new CreatePatientCommand(new CreatePatientRequest
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john.doe@example.com",
            DateOfBirth = new DateTime(1990, 1, 15),
            Status = PatientStatus.Active.Name
        });

        // Act
        StartStopwatch();
        var result = await ValidatorFor<CreatePatientCommand>().ValidateAsync(command);
        StopStopwatch();

        // Assert
        result.IsValid.ShouldBeTrue();
        result.Errors.Count.ShouldBe(0);
        ElapsedSeconds().ShouldBeLessThan(0.1M);
    }

    [TestMethod]
    public async Task Valid_When_PhoneNumberIsNull()
    {
        // Arrange - PhoneNumber is optional
        SetupUserRoles(AppRoles.Admin);

        var command = new CreatePatientCommand(new CreatePatientRequest
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john.doe@example.com",
            DateOfBirth = new DateTime(1990, 1, 15),
            PhoneNumber = null,
            Status = PatientStatus.Active.Name
        });

        // Act
        var result = await ValidatorFor<CreatePatientCommand>().ValidateAsync(command);

        // Assert
        result.IsValid.ShouldBeTrue();
    }

    [TestMethod]
    public async Task Valid_When_UserIsNurse()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        SetupUserRoles(AppRoles.Nurse);
        SetupPatientExists(patientId);
        var command = new CreatePatientCommand(new CreatePatientRequest
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john.doe@example.com",
            DateOfBirth = new DateTime(1990, 1, 15),
            PhoneNumber = null,
            Status = PatientStatus.Active.Name
        });

        // Act
        var result = await ValidatorFor<CreatePatientCommand>().ValidateAsync(command);

        // Assert
        result.IsValid.ShouldBeTrue();
    }

    [TestMethod]
    public async Task Valid_When_UserIsAdmin()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        SetupUserRoles(AppRoles.Admin);
        SetupPatientExists(patientId);
        var command = new CreatePatientCommand(new CreatePatientRequest
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john.doe@example.com",
            DateOfBirth = new DateTime(1990, 1, 15),
            PhoneNumber = null,
            Status = PatientStatus.Active.Name
        });

        // Act
        var result = await ValidatorFor<CreatePatientCommand>().ValidateAsync(command);

        // Assert
        result.IsValid.ShouldBeTrue();
    }

    [TestMethod]
    public async Task Valid_When_UserIsDoctor()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        SetupUserRoles(AppRoles.Doctor);
        SetupPatientExists(patientId);
        var command = new CreatePatientCommand(new CreatePatientRequest
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john.doe@example.com",
            DateOfBirth = new DateTime(1990, 1, 15),
            PhoneNumber = null,
            Status = PatientStatus.Active.Name
        });

        // Act
        var result = await ValidatorFor<CreatePatientCommand>().ValidateAsync(command);

        // Assert
        result.IsValid.ShouldBeTrue();
    }

    [TestMethod]
    public async Task Invalid_When_UserHasNoAllowedRole()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        SetupPatientExists(patientId);
      
        var command = new CreatePatientCommand(new CreatePatientRequest
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john.doe@example.com",
            DateOfBirth = new DateTime(1990, 1, 15),
            PhoneNumber = null,
            Status = PatientStatus.Active.Name
        });

        // Act
        var result = await ValidatorFor<CreatePatientCommand>().ValidateAsync(command);

        // Assert
        result.Errors.ShouldContain(e => e.ErrorCode == ErrorCode.Forbidden.Value);
    }
}
