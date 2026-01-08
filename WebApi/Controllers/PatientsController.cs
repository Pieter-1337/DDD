using Microsoft.AspNetCore.Mvc;
using BuildingBlocks.Domain.Interfaces;
using Scheduling.Domain.Patients;
using MediatR;
using Scheduling.Application.Patients.Commands;
using Scheduling.Application.Patients.Dtos;

namespace WebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PatientsController : ControllerBase
    {
        private readonly IUnitOfWork _uow;
        private readonly IMediator _mediator;
        public PatientsController(IUnitOfWork uow, IMediator mediator)
        {
            _uow = uow;
            _mediator = mediator;
        }

        [HttpPost("create")]
        [ProducesResponseType<bool>(StatusCodes.Status200OK)]
        [ProducesResponseType<bool>(StatusCodes.Status400BadRequest)]
        public async Task<Patient> Create(PatientDto dto)
        {
            var result = await _mediator.Send(new CreatePatientCommand(dto));

            return result;
        }
    }
}
