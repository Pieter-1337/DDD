using BuildingBlocks.Application.Interfaces;
using Billing.Domain.BillingProfiles;
using MediatR;

namespace Billing.Application.BillingProfiles.Commands
{
    public class CreateBillingProfileCommandHandler : IRequestHandler<CreateBillingProfileCommand, CreateBillingProfileCommandResponse>
    {
        private IUnitOfWork _uow;

        public CreateBillingProfileCommandHandler(IUnitOfWork uow)
        {
            _uow = uow;
        }
        public async Task<CreateBillingProfileCommandResponse> Handle(CreateBillingProfileCommand cmd, CancellationToken cancellationToken)
        {
            var request = cmd.billingProfile;

            var existingProfile = await _uow.RepositoryFor<BillingProfile>().FirstOrDefaultAsync(bp => bp.PatientId == request.PatientId, cancellationToken);
            if (existingProfile != null) 
            {
                return new CreateBillingProfileCommandResponse
                {
                    Success = true,
                    BillingProfileId = existingProfile.Id,
                    Message = "Billing profile already existed"
                };
            }

            var profile = BillingProfile.Create(request.PatientId, request.Email, request.FullName);
            _uow.RepositoryFor<BillingProfile>().Add(profile);

            await _uow.SaveChangesAsync(cancellationToken);

            return new CreateBillingProfileCommandResponse
            {
                Success = true,
                BillingProfileId = profile.Id,
                Message = "Billing profile succesfully created"
            };
        }
    }
}
