using Microsoft.AspNetCore.Mvc;
using BuildingBlocks.Domain.Interfaces;
using Scheduling.Domain.Patients;

namespace WebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PatientsController : ControllerBase
    {
        private readonly IUnitOfWork _uow;
        public PatientsController(IUnitOfWork uow)
        {
            _uow = uow;
        }

        [HttpPost("create")]
        [ProducesResponseType<bool>(StatusCodes.Status200OK)]
        [ProducesResponseType<bool>(StatusCodes.Status400BadRequest)]
        public async Task<bool> Create()
        {
            var patient = Patient.Create(
                "Pieter",
                "Bracke",
                "pieterbracke@msn.com",
                new DateTime(1989, 01, 21),
                null
            );

            _uow.RepositoryFor<Patient>().Add(patient);

            var result = await _uow.SaveChangesAsync();

            return result == 1;
        }
    }
}
