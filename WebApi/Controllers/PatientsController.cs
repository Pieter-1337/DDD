using Microsoft.AspNetCore.Mvc;
using BuildingBlocks.Application;
using Scheduling.Domain.Patients;
using MediatR;
using Scheduling.Application.Patients.Commands;
using Scheduling.Application.Patients.Dtos;
using Scheduling.Application.Patients.Queries;

namespace WebApi.Controllers
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
        public async Task<IActionResult> GetPatientAsync([FromBody] Guid patientId)
        {
            var response = await _mediator.Send(new GetPatientQuery { Id = patientId });

            return Ok(response);
        }

        [HttpGet("")]
        [ProducesResponseType<IEnumerable<PatientDto>>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> GetAllPatientsAsync(PatientStatus status)
        {
            var response = await _mediator.Send(new GetAllPatientsQuery { Status = status });

            return Ok(response);
        }

        [HttpPost("")]
        [ProducesResponseType<bool>(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreatePatientAsync(CreatePatientRequest request)
        {
            var response = await _mediator.Send(new CreatePatientCommand(request));
            return CreatedAtAction(nameof(GetPatientAsync), new { patientId = response.PatientDto }, response);
        }
    }
}
