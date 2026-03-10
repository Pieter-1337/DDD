using Billing.Domain.BillingProfiles.Events;
using BuildingBlocks.Domain;

namespace Billing.Domain.BillingProfiles
{
    public class BillingProfile : Entity
    {
        public Guid PatientId { get; private set; }
        public string Email { get; private set; }
        public string FullName { get; private set; }
        public string? BillingAddress { get; private set; }
        public PaymentMethod? PaymentMethod { get; private set; }
        public DateTime CreatedAt { get; private set; }

        private BillingProfile() { }

        public static BillingProfile Create(
            Guid patientId,
            string email,
            string fullName)
        {
            var profile = new BillingProfile
            {
                Id = Guid.NewGuid(),
                PatientId = patientId,
                Email = email,
                FullName = fullName,
                CreatedAt = DateTime.UtcNow,
            };

            profile.AddDomainEvent(new BillingProfileCreatedEvent(
                profile.Id,
                patientId,
                email,
                fullName));

            return profile;
        }

        public void UpdateBillingAddress(string address)
        {
            BillingAddress = address;
        }

        public void UpdatePaymentMethod(PaymentMethod paymentMethod)
        {
            PaymentMethod = paymentMethod;
        }
    }
}
