using MediatR;

namespace BuildingBlocks.Domain.Events;

public interface IDomainEvent : INotification
{
    DateTime OccurredOn { get; }
}
