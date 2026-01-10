using BuildingBlocks.Application.Dtos;
using BuildingBlocks.Application.Validators;
using FluentValidation;
using FluentValidation.Validators;
using MediatR;
using Scheduling.Application.Patients.Dtos;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Commands
{
    public record CreatePatientCommand(CreatePatientRequest Patient) : IRequest<CreatePatientCommandResponse>;

    public class CreatePatientRequest
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public DateTime DateOfBirth { get; set; }
        public string? PhoneNumber { get; set; }
        public PatientStatus Status { get; set; }
    }

    public class CreatePatientCommandResponse : SuccessOrFailureDto
    {
        public PatientDto PatientDto { get; set; }
    }

    #region Validators
    internal class CreatePatientCommandValidator : UserValidator<CreatePatientCommand>
    {
        public CreatePatientCommandValidator(IValidator<CreatePatientRequest> createPatientRequestValidator)
        {
            RuleFor(c => c.Patient).Cascade(CascadeMode.Stop)
                .NotNull()
                .SetValidator(createPatientRequestValidator);
        }
    }

    internal class CreatePatientRequestValidator : AbstractValidator<CreatePatientRequest>
    {
        public CreatePatientRequestValidator()
        {
          
            RuleFor(p => p.FirstName)
                .NotEmpty()
                .WithMessage("FirstName cannot be empty");

            RuleFor(p => p.LastName)
                .NotEmpty()
                .WithMessage("LastName cannot be empty");

            RuleFor(p => p.Email)
                .NotEmpty()
                .WithMessage("Email cannot be empty")
                .EmailAddress(EmailValidationMode.AspNetCoreCompatible)
                .WithMessage("Invalid email address");

            RuleFor(p => p.DateOfBirth)
                .NotEmpty()
                .WithMessage("Date of birth cannot be empty");  
        }
    }
    #endregion Validators
}
