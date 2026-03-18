using Microsoft.AspNetCore.Mvc;
using MediatR;
using Scheduling.Application.Patients.Commands;
using Scheduling.Application.Patients.Dtos;
using Scheduling.Application.Patients.Queries;
using BuildingBlocks.Application.Interfaces;

namespace Scheduling.WebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PatientsController : ControllerBase
    {
        private readonly IMediator _mediator;
        public PatientsController(IUnitOfWork uow, IMediator mediator)
        {
            _mediator = mediator;
        }

        [HttpGet("{patientId}")]
        [ProducesResponseType<PatientDto>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> GetPatientAsync(Guid patientId)
        {
            var response = await _mediator.Send(new GetPatientQuery { Id = patientId });

            return Ok(response);
        }

        [HttpGet("")]
        [ProducesResponseType<IEnumerable<PatientDto>>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> GetAllPatientsAsync(string? status)
        {
            var response = await _mediator.Send(new GetAllPatientsQuery { Status = status });

            return Ok(response);
        }

        [HttpPost("")]
        [ProducesResponseType<CreatePatientCommandResponse>(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreatePatientAsync(CreatePatientRequest request)
        {
            var response = await _mediator.Send(new CreatePatientCommand(request));
            return CreatedAtAction(nameof(GetPatientAsync), new { patientId = response.PatientId }, response);
        }

        [HttpPost("{patientId}/suspend")]
        [ProducesResponseType<bool>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> SuspendPatientAsync(Guid patientId)
        {
            var response = await _mediator.Send(new SuspendPatientCommand { Id = patientId });
            return Ok(response);
        }

        [HttpPost("{patientId}/activate")]
        [ProducesResponseType<bool>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ActivatePatientAsync(Guid patientId)
        {
            var response = await _mediator.Send(new ActivatePatientCommand { Id = patientId });
            return Ok(response);
        }
    }
}
