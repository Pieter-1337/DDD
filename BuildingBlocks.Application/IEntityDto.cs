using System.Linq.Expressions;

namespace BuildingBlocks.Application
{
    public interface IEntityDto<TEntity, TDto> where TDto : IEntityDto<TEntity, TDto>
    {
        static abstract Expression<Func<TEntity, TDto>> ToDto { get; }
        static abstract TDto FromEntity(TEntity entity);
    }
}
