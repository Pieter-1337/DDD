using BuildingBlocks.Application;
using MediatR;

namespace Scheduling.Application.Patients.Commands
{
    public class SuspendPatientCommand : IRequest<SuspendPatientCommandResponse>
    {
        public Guid Id { get; set; }
    }

    public class SuspendPatientCommandResponse: SuccessOrFailureDto { }
}
