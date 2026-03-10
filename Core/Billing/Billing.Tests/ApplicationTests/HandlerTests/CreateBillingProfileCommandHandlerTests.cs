using BuildingBlocks.Tests;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Billing.Application.BillingProfiles.Commands;
using Billing.Domain.BillingProfiles;
using Shouldly;

namespace Billing.Tests.ApplicationTests.HandlerTests;

[TestClass]
public class CreateBillingProfileCommandHandlerTests : BillingDbTestBase
{
    [TestMethod]
    public async Task Handle_Should_CreateBillingProfile_ForValidRequest()
    {
        // Arrange
        var command = new CreateBillingProfileCommand(new CreateBillingProfileRequest
        {
            PatientId = Guid.NewGuid(),
            Email = "john.doe@example.com",
            FullName = "John Doe"
        });

        // Act
        StartStopwatch();
        var response = await GetMediator().Send(command);
        StopStopwatch();

        // Assert
        response.ShouldNotBeNull();
        response.Success.ShouldBeTrue();
        response.BillingProfileId.ShouldNotBe(Guid.Empty);

        // Verify persisted to database
        var profile = await Uow.RepositoryFor<BillingProfile>().GetByIdAsync(response.BillingProfileId);
        profile.ShouldNotBeNull();
        profile!.Email.ShouldBe("john.doe@example.com");
        profile.FullName.ShouldBe("John Doe");

        ElapsedSeconds().ShouldBeLessThan(1M);
    }

    [TestMethod]
    public async Task Handle_Should_ReturnExistingProfile_WhenDuplicate()
    {
        // Arrange - create first profile
        var patientId = Guid.NewGuid();
        var command = new CreateBillingProfileCommand(new CreateBillingProfileRequest
        {
            PatientId = patientId,
            Email = "john@example.com",
            FullName = "John Doe"
        });

        var firstResponse = await GetMediator().Send(command);

        // Act - send same patient ID again
        var duplicateCommand = new CreateBillingProfileCommand(new CreateBillingProfileRequest
        {
            PatientId = patientId,
            Email = "john@example.com",
            FullName = "John Doe"
        });

        var secondResponse = await GetMediator().Send(duplicateCommand);

        // Assert - should return same profile (idempotent)
        secondResponse.Success.ShouldBeTrue();
        secondResponse.BillingProfileId.ShouldBe(firstResponse.BillingProfileId);
    }
}
