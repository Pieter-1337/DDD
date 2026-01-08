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
        private readonly IUnitOfWork _uow;
        private readonly IMediator _mediator;
        public PatientsController(IUnitOfWork uow, IMediator mediator)
        {
            _uow = uow;
            _mediator = mediator;
        }

        [HttpGet("{patientId}")]
        [ProducesResponseType<PatientDto>(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetPatientAsync(Guid patientId)
        {
            var response = await _mediator.Send(new GetPatientQuery { Id = patientId });

            return Ok(response);
        }

        [HttpPost("")]
        [ProducesResponseType<bool>(StatusCodes.Status201Created)]
        [ProducesResponseType<bool>(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreatePatientAsync(CreatePatientRequest request)
        {
            var response = await _mediator.Send(new CreatePatientCommand(request));
            return CreatedAtAction(nameof(GetPatientAsync), new { patientId = response.PatientDto }, response);
        }
    }
}
