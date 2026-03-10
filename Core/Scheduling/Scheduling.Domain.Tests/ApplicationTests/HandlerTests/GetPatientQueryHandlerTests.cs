using FizzWare.NBuilder;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scheduling.Application.Patients.Commands;
using Scheduling.Application.Patients.Queries;
using Scheduling.Domain.Patients;
using Shouldly;

namespace Scheduling.Tests.ApplicationTests.HandlerTests;

[TestClass]
public class GetPatientQueryHandlerTests : SchedulingDbTestBase
{
    [TestMethod]
    public async Task Handle_Should_ReturnPatient_WhenExists()
    {
        // Arrange - Create a patient first
        var createRequest = Builder<CreatePatientRequest>.CreateNew()
            .With(p => p.FirstName = "John")
            .With(p => p.LastName = "Doe")
            .With(p => p.Email = "john.doe@example.com")
            .With(p => p.DateOfBirth = new DateTime(1990, 1, 15))
            .With(p => p.PhoneNumber = "+1234567890")
            .With(p => p.Status = PatientStatus.Active.Name)
            .Build();

        var createResponse = await GetMediator().Send(new CreatePatientCommand(createRequest));
        var patientId = createResponse.PatientId;

        var query = new GetPatientQuery { Id = patientId };

        // Act
        StartStopwatch();
        var result = await GetMediator().Send(query);
        StopStopwatch();

        // Assert
        result.ShouldNotBeNull();
        result!.Id.ShouldBe(patientId);
        result.FirstName.ShouldBe("John");
        result.LastName.ShouldBe("Doe");
        result.Email.ShouldBe("john.doe@example.com");

        ElapsedSeconds().ShouldBeLessThan(0.5M);
    }

    [TestMethod]
    public async Task Handle_Should_ReturnNull_WhenPatientDoesNotExist()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var query = new GetPatientQuery { Id = nonExistentId };

        // Act
        var result = await GetMediator().Send(query);

        // Assert
        result.ShouldBeNull();
    }

    [TestMethod]
    public async Task Handle_Should_ReturnCorrectPatient_WhenMultipleExist()
    {
        // Arrange - Create multiple patients
        var patient1Request = Builder<CreatePatientRequest>.CreateNew()
            .With(p => p.FirstName = "Patient")
            .With(p => p.LastName = "One")
            .With(p => p.Email = "patient.one@example.com")
            .With(p => p.DateOfBirth = new DateTime(1990, 1, 1))
            .With(p => p.Status = PatientStatus.Active.Name)
            .Build();

        var patient2Request = Builder<CreatePatientRequest>.CreateNew()
            .With(p => p.FirstName = "Patient")
            .With(p => p.LastName = "Two")
            .With(p => p.Email = "patient.two@example.com")
            .With(p => p.DateOfBirth = new DateTime(1985, 5, 10))
            .With(p => p.Status = PatientStatus.Active.Name)
            .Build();

        var response1 = await GetMediator().Send(new CreatePatientCommand(patient1Request));
        var response2 = await GetMediator().Send(new CreatePatientCommand(patient2Request));

        var query = new GetPatientQuery { Id = response2.PatientId };

        // Act
        var result = await GetMediator().Send(query);

        // Assert
        result.ShouldNotBeNull();
        result!.Id.ShouldBe(response2.PatientId);
        result.LastName.ShouldBe("Two");
        result.Email.ShouldBe("patient.two@example.com");
    }
}
