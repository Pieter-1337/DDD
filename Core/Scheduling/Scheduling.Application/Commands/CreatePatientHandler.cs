using BuildingBlocks.Domain.Interfaces;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Commands
{
    public class CreatePatientHandler
    {
        private readonly IUnitOfWork _uow;
        public CreatePatientHandler(IUnitOfWork unitOfWork)
        {
            _uow = unitOfWork;
        }

        public async Task<Patient> Handle(CreatePatientCommand cmd, CancellationToken cancellationToken)
        {
            var patient = Patient.Create(cmd.FirstName, cmd.LastName, cmd.Email, cmd.DateOfBirth, cmd.PhoneNumber);
            _uow.RepositoryFor<Patient>().Add(patient);
            await _uow.SaveChangesAsync(cancellationToken);

            return patient;
        }

        public class CreatePatientCommand
        {
            public string? FirstName { get; set; }
            public string? LastName { get; set; }
            public string? Email { get; set; }
            public DateTime DateOfBirth { get; set; }
            public string? PhoneNumber { get; set; }
        }
    }
}
