using BuildingBlocks.Tests;
using FizzWare.NBuilder;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scheduling.Application.Patients.Commands;
using Scheduling.Domain.Patients;
using Scheduling.Infrastructure.Persistence;
using Shouldly;

namespace Scheduling.Tests.ApplicationTests.HandlerTests;

[TestClass]
public class CreatePatientCommandHandlerTests : SchedulingDbTestBase
{
    [TestMethod]
    public async Task Handle_Should_CreatePatient_ForValidRequest()
    {
        // Arrange
        var request = Builder<CreatePatientRequest>.CreateNew()
            .With(p => p.FirstName = "John")
            .With(p => p.LastName = "Doe")
            .With(p => p.Email = "john.doe@example.com")
            .With(p => p.DateOfBirth = new DateTime(1990, 1, 15))
            .With(p => p.PhoneNumber = "+1234567890")
            .Build();

        var command = new CreatePatientCommand(request);

        // Act
        StartStopwatch();
        var response = await GetMediator().Send(command);
        StopStopwatch();

        // Assert
        response.ShouldNotBeNull();
        response.Success.ShouldBeTrue();
        response.Message.ShouldNotBeNullOrEmpty();
        response.PatientDto.ShouldNotBeNull();
        response.PatientDto.Id.ShouldNotBe(default);

        // Verify persisted to database
        var reloadedPatient = await Uow.RepositoryFor<Patient>().GetByIdAsync(response.PatientDto.Id);
        reloadedPatient.ShouldNotBeNull();
        reloadedPatient!.FirstName.ShouldBe("John");
        reloadedPatient.LastName.ShouldBe("Doe");
        reloadedPatient.Email.ShouldBe("john.doe@example.com");
        reloadedPatient.Status.ShouldBe(PatientStatus.Active);

        ElapsedSeconds().ShouldBeLessThan(1M);
    }

    [TestMethod]
    public async Task Handle_Should_CreatePatient_WithoutPhoneNumber()
    {
        // Arrange
        var request = Builder<CreatePatientRequest>.CreateNew()
            .With(p => p.FirstName = "Jane")
            .With(p => p.LastName = "Smith")
            .With(p => p.Email = "jane.smith@example.com")
            .With(p => p.DateOfBirth = new DateTime(1985, 6, 20))
            .With(p => p.PhoneNumber = null)
            .Build();

        var command = new CreatePatientCommand(request);

        // Act
        var response = await GetMediator().Send(command);

        // Assert
        response.ShouldNotBeNull();
        response.Success.ShouldBeTrue();
        response.PatientDto.PhoneNumber.ShouldBeNull();

        var reloadedPatient = await Uow.RepositoryFor<Patient>().GetByIdAsync(response.PatientDto.Id);
        reloadedPatient.ShouldNotBeNull();
        reloadedPatient!.PhoneNumber.ShouldBeNull();
    }

    [TestMethod]
    public async Task Handle_Should_NormalizeEmail_ToLowerCase()
    {
        // Arrange
        var request = Builder<CreatePatientRequest>.CreateNew()
            .With(p => p.FirstName = "Test")
            .With(p => p.LastName = "User")
            .With(p => p.Email = "TEST.USER@EXAMPLE.COM")
            .With(p => p.DateOfBirth = new DateTime(2000, 1, 1))
            .Build();

        var command = new CreatePatientCommand(request);

        // Act
        var response = await GetMediator().Send(command);

        // Assert
        response.PatientDto.Email.ShouldBe("test.user@example.com");

        var reloadedPatient = await Uow.RepositoryFor<Patient>().GetByIdAsync(response.PatientDto.Id);
        reloadedPatient!.Email.ShouldBe("test.user@example.com");
    }
}
