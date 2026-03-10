using BuildingBlocks.Application.Dtos;
using BuildingBlocks.Application.Cqrs;
using BuildingBlocks.Application.Validators;
using BuildingBlocks.Enumerations;
using FluentValidation;
using FluentValidation.Validators;
using Scheduling.Domain.Patients;

namespace Scheduling.Application.Patients.Commands
{
    public record CreatePatientCommand(CreatePatientRequest Patient) : Command<CreatePatientCommandResponse>;

    public class CreatePatientRequest
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public DateTime DateOfBirth { get; set; }
        public string? PhoneNumber { get; set; }
        public string Status { get; set; }
    }

    public class CreatePatientCommandResponse : SuccessOrFailureDto
    {
        public Guid PatientId { get; set; }
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
                .WithErrorCode(ErrorCode.FirstNameRequired.Value)
                .WithMessage(ErrorCode.FirstNameRequired.Message);

            RuleFor(p => p.LastName)
                .NotEmpty()
                .WithErrorCode(ErrorCode.LastNameRequired.Value)
                .WithMessage(ErrorCode.LastNameRequired.Message);

            RuleFor(p => p.Email)
                .NotEmpty()
                .WithErrorCode(ErrorCode.EmailRequired.Value)
                .WithMessage(ErrorCode.EmailRequired.Message)
                .EmailAddress(EmailValidationMode.AspNetCoreCompatible)
                .WithErrorCode(ErrorCode.InvalidEmail.Value)
                .WithMessage(ErrorCode.InvalidEmail.Message);

            RuleFor(p => p.DateOfBirth)
                .NotEmpty()
                .WithErrorCode(ErrorCode.DateOfBirthRequired.Value)
                .WithMessage(ErrorCode.DateOfBirthRequired.Message);

            RuleFor(p => p.Status)
                .Must(PatientStatus.IsInEnum)
                .WithErrorCode(ErrorCode.InvalidStatus.Value)
                .WithMessage(ErrorCode.InvalidStatus.Message);
        }
    }
    #endregion Validators
}
