using FizzWare.NBuilder;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scheduling.Application.Patients.Commands;
using Scheduling.Application.Patients.Queries;
using Scheduling.Domain.Patients;
using Shouldly;

namespace Scheduling.Tests.ApplicationTests.HandlerTests;

[TestClass]
public class GetAllPatientsQueryHandlerTests : SchedulingDbTestBase
{
    [TestMethod]
    public async Task Handle_Should_ReturnActivePatients_WhenFilteredByActiveStatus()
    {
        // Arrange - Create active patients
        var patient1 = Builder<CreatePatientRequest>.CreateNew()
            .With(p => p.FirstName = "Active")
            .With(p => p.LastName = "Patient1")
            .With(p => p.Email = "active1@example.com")
            .With(p => p.DateOfBirth = new DateTime(1990, 1, 1))
            .With(p => p.Status = PatientStatus.Active.Name)
            .Build();

        var patient2 = Builder<CreatePatientRequest>.CreateNew()
            .With(p => p.FirstName = "Active")
            .With(p => p.LastName = "Patient2")
            .With(p => p.Email = "active2@example.com")
            .With(p => p.DateOfBirth = new DateTime(1985, 5, 10))
            .With(p => p.Status = PatientStatus.Active.Name)
            .Build();

        await GetMediator().Send(new CreatePatientCommand(patient1));
        await GetMediator().Send(new CreatePatientCommand(patient2));

        var query = new GetAllPatientsQuery { Status = PatientStatus.Active.Name };

        // Act
        StartStopwatch();
        var result = await GetMediator().Send(query);
        StopStopwatch();

        // Assert
        result.ShouldNotBeNull();
        result.Count().ShouldBe(2);
        result.ShouldAllBe(p => p.Status == PatientStatus.Active);

        ElapsedSeconds().ShouldBeLessThan(0.5M);
    }

    [TestMethod]
    public async Task Handle_Should_ReturnSuspendedPatients_WhenFilteredBySuspendedStatus()
    {
        // Arrange - Create and suspend a patient
        var patientRequest = Builder<CreatePatientRequest>.CreateNew()
            .With(p => p.FirstName = "Suspended")
            .With(p => p.LastName = "Patient")
            .With(p => p.Email = "suspended@example.com")
            .With(p => p.DateOfBirth = new DateTime(1990, 1, 1))
            .With(p => p.Status = PatientStatus.Active.Name)
            .Build();

        var createResponse = await GetMediator().Send(new CreatePatientCommand(patientRequest));

        // Suspend the patient
        await GetMediator().Send(new SuspendPatientCommand { Id = createResponse.PatientId });

        var query = new GetAllPatientsQuery { Status = PatientStatus.Suspended.Name };

        // Act
        var result = await GetMediator().Send(query);

        // Assert
        result.ShouldNotBeNull();
        result.Count().ShouldBe(1);
        result.First().Email.ShouldBe("suspended@example.com");
        result.First().Status.ShouldBe(PatientStatus.Suspended);
    }

    [TestMethod]
    public async Task Handle_Should_ReturnEmptyCollection_WhenNoMatchingPatients()
    {
        // Arrange - Create only active patients
        var patientRequest = Builder<CreatePatientRequest>.CreateNew()
            .With(p => p.FirstName = "Active")
            .With(p => p.LastName = "Only")
            .With(p => p.Email = "active.only@example.com")
            .With(p => p.DateOfBirth = new DateTime(1990, 1, 1))
            .With(p => p.Status = PatientStatus.Active.Name)
            .Build();

        await GetMediator().Send(new CreatePatientCommand(patientRequest));

        // Query for inactive patients (none exist)
        var query = new GetAllPatientsQuery { Status = PatientStatus.Inactive.Name };

        // Act
        var result = await GetMediator().Send(query);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBeEmpty();
    }

    [TestMethod]
    public async Task Handle_Should_OnlyReturnPatientsWithRequestedStatus()
    {
        // Arrange - Create mix of active and suspended patients
        var activeRequest = Builder<CreatePatientRequest>.CreateNew()
            .With(p => p.FirstName = "Still")
            .With(p => p.LastName = "Active")
            .With(p => p.Email = "still.active@example.com")
            .With(p => p.DateOfBirth = new DateTime(1990, 1, 1))
            .With(p => p.Status = PatientStatus.Active.Name)
            .Build();

        var toSuspendRequest = Builder<CreatePatientRequest>.CreateNew()
            .With(p => p.FirstName = "Will Be")
            .With(p => p.LastName = "Suspended")
            .With(p => p.Email = "will.suspend@example.com")
            .With(p => p.DateOfBirth = new DateTime(1985, 5, 10))
            .With(p => p.Status = PatientStatus.Active.Name)
            .Build();

        await GetMediator().Send(new CreatePatientCommand(activeRequest));
        var suspendResponse = await GetMediator().Send(new CreatePatientCommand(toSuspendRequest));

        // Suspend one patient
        await GetMediator().Send(new SuspendPatientCommand { Id = suspendResponse.PatientId });

        // Query for active only
        var query = new GetAllPatientsQuery { Status = PatientStatus.Active.Name };

        // Act
        var result = await GetMediator().Send(query);

        // Assert
        result.ShouldNotBeNull();
        result.Count().ShouldBe(1);
        result.First().Email.ShouldBe("still.active@example.com");
        result.ShouldAllBe(p => p.Status == PatientStatus.Active);
    }
}
