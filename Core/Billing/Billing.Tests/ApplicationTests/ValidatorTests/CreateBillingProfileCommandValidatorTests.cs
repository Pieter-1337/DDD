using BuildingBlocks.Enumerations;
using BuildingBlocks.Tests;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Billing.Application.BillingProfiles.Commands;
using Shouldly;

namespace Billing.Tests.ApplicationTests.ValidatorTests;

[TestClass]
public class CreateBillingProfileCommandValidatorTests : BillingValidatorTestBase
{
    [TestMethod]
    public async Task Invalid_When_BillingProfileIsNull()
    {
        var command = new CreateBillingProfileCommand(null!);

        var result = await ValidatorFor<CreateBillingProfileCommand>().ValidateAsync(command);

        result.Errors.ShouldContainValidation(nameof(CreateBillingProfileCommand.billingProfile), VALIDATION_NOT_NULL_VALIDATOR);
        result.Errors.Count.ShouldBe(1);
    }

    [TestMethod]
    public async Task Invalid_When_RequiredFieldsAreEmpty()
    {
        var command = new CreateBillingProfileCommand(new CreateBillingProfileRequest
        {
            PatientId = Guid.Empty,
            Email = "",
            FullName = ""
        });

        var result = await ValidatorFor<CreateBillingProfileCommand>().ValidateAsync(command);

        result.Errors.ShouldContainValidation(nameof(CreateBillingProfileRequest.PatientId), ErrorCode.Required.Value);
        result.Errors.ShouldContainValidation(nameof(CreateBillingProfileRequest.Email), ErrorCode.EmailRequired.Value);
        result.Errors.ShouldContainValidation(nameof(CreateBillingProfileRequest.Email), ErrorCode.InvalidEmail.Value);
        result.Errors.ShouldContainValidation(nameof(CreateBillingProfileRequest.FullName), ErrorCode.Required.Value);
    }

    [TestMethod]
    public async Task Invalid_When_EmailIsInvalid()
    {
        SetupBillingProfileNotExistsForPatient(Guid.NewGuid());

        var command = new CreateBillingProfileCommand(new CreateBillingProfileRequest
        {
            PatientId = Guid.NewGuid(),
            Email = "not-an-email",
            FullName = "John Doe"
        });

        var result = await ValidatorFor<CreateBillingProfileCommand>().ValidateAsync(command);

        result.Errors.ShouldContainValidation(nameof(CreateBillingProfileRequest.Email), ErrorCode.InvalidEmail.Value);
        result.Errors.Count.ShouldBe(1);
    }

    [TestMethod]
    public async Task Invalid_When_BillingProfileAlreadyExists()
    {
        var patientId = Guid.NewGuid();
        SetupBillingProfileExistsForPatient(patientId);

        var command = new CreateBillingProfileCommand(new CreateBillingProfileRequest
        {
            PatientId = patientId,
            Email = "john@example.com",
            FullName = "John Doe"
        });

        var result = await ValidatorFor<CreateBillingProfileCommand>().ValidateAsync(command);

        result.Errors.ShouldContainValidation(nameof(CreateBillingProfileRequest.PatientId), ErrorCode.Conflict.Value);
    }

    [TestMethod]
    public async Task Valid_When_AllFieldsAreValid()
    {
        SetupBillingProfileNotExistsForPatient(Guid.NewGuid());

        var command = new CreateBillingProfileCommand(new CreateBillingProfileRequest
        {
            PatientId = Guid.NewGuid(),
            Email = "john.doe@example.com",
            FullName = "John Doe"
        });

        var result = await ValidatorFor<CreateBillingProfileCommand>().ValidateAsync(command);

        result.IsValid.ShouldBeTrue();
        result.Errors.Count.ShouldBe(0);
    }
}
