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
        var command = new CreatePatientCommand(new CreatePatientRequest
        {
            FirstName = "",
            LastName = "",
            Email = "",
            DateOfBirth = default
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

        ElapsedSeconds().ShouldBeLessThan(0.1M);
    }

    [TestMethod]
    public async Task Invalid_When_EmailIsInvalid()
    {
        // Arrange
        var command = new CreatePatientCommand(new CreatePatientRequest
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "not-an-email",
            DateOfBirth = new DateTime(1990, 1, 15)
        });

        // Act
        var result = await ValidatorFor<CreatePatientCommand>().ValidateAsync(command);

        // Assert
        result.Errors.ShouldContainValidation(nameof(CreatePatientRequest.Email), ErrorCode.InvalidEmail.Value);
        result.Errors.Count.ShouldBe(1);
    }

    [TestMethod]
    public async Task Valid_When_AllFieldsAreValid()
    {
        // Arrange
        var command = new CreatePatientCommand(new CreatePatientRequest
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john.doe@example.com",
            DateOfBirth = new DateTime(1990, 1, 15)
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
        var command = new CreatePatientCommand(new CreatePatientRequest
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john.doe@example.com",
            DateOfBirth = new DateTime(1990, 1, 15),
            PhoneNumber = null
        });

        // Act
        var result = await ValidatorFor<CreatePatientCommand>().ValidateAsync(command);

        // Assert
        result.IsValid.ShouldBeTrue();
    }
}
