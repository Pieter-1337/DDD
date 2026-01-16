using FluentValidation;
using FluentValidation.Results;
using MediatR.Pipeline;

namespace BuildingBlocks.Application
{
    public class RequestValidationProcessor<TRequest> : IRequestPreProcessor<TRequest> where TRequest : notnull
    {
        private readonly IValidator<TRequest>[] _validators;
        public RequestValidationProcessor(IValidator<TRequest>[] validators)
        {
            _validators = validators;
        }

        public async Task Process(TRequest request, CancellationToken cancellationToken)
        {
            if (_validators.Length == 0)
                return;

            var validationContext = new ValidationContext<TRequest>(request);
            var validationFailures = new List<ValidationFailure>();

            foreach (var validator in _validators) 
            {
                var validationResult = await validator.ValidateAsync(validationContext, cancellationToken);

                if (!validationResult.IsValid)
                {
                    validationFailures.AddRange(validationResult.Errors.Where(f => f is not null));
                }
            }

            if(validationFailures.Count > 0)
            {
                throw new ValidationException(validationFailures);
            }
        }
    }
}
