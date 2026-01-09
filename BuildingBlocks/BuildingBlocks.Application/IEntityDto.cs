using System.Linq.Expressions;

namespace BuildingBlocks.Application
{
    public interface IEntityDto<TEntity, TDto> where TDto : IEntityDto<TEntity, TDto>
    {
        static abstract Expression<Func<TEntity, TDto>> Project { get; }
        static abstract TDto ToDto(TEntity entity);
    }
}
