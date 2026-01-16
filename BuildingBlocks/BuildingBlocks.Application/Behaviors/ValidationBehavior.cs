using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace BuildingBlocks.Application.Behaviors
{
    public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
    {
        protected readonly IEnumerable<IValidator<TRequest>> Validators;

        public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
        {
            Validators = validators;
        }

        public virtual async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            if (Validators.Any())
            {
                await ValidateAsync(request, cancellationToken);
            }

            return await next();
        }

        protected virtual async Task ValidateAsync(TRequest request, CancellationToken cancellationToken)
        {
            var context = new ValidationContext<TRequest>(request);
            var failures = new List<ValidationFailure>();

            foreach (var validator in Validators)
            {
                var result = await validator.ValidateAsync(context, cancellationToken);
                failures.AddRange(result.Errors.Where(f => f is not null));
            }

            if (failures.Count > 0)
            {
                OnValidationFailure(request, failures);
            }
        }

        protected virtual void OnValidationFailure(TRequest request, List<ValidationFailure> failures)
            => throw new ValidationException(failures);
    }
}
