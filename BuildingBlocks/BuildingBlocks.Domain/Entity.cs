namespace BuildingBlocks.Domain;

public abstract class Entity : IEntityBase
{
    public Guid Id { get; set; }
}
