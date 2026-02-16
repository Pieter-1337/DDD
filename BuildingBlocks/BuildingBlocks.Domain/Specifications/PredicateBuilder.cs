using System.Linq.Expressions;

namespace BuildingBlocks.Domain.Specifications;

/// <summary>
/// Enables the efficient, dynamic composition of query predicates.
/// See http://www.albahari.com/expressions for information and examples.
/// </summary>
public static class PredicateBuilder
{
    /// <summary>
    /// Creates a predicate that evaluates to true. (starting point for an AND clause)
    /// </summary>
    public static Expression<Func<T, bool>> BaseAnd<T>()
    {
        return f => true;
    }

    /// <summary>
    /// Creates a predicate that evaluates to false. (starting point for an OR clause)
    /// </summary>
    public static Expression<Func<T, bool>> BaseOr<T>()
    {
        return f => false;
    }

    /// <summary>
    /// Creates a predicate expression from the specified lambda expression.
    /// </summary>
    public static Expression<Func<T, bool>> Create<T>(Expression<Func<T, bool>> predicate)
    {
        return predicate;
    }

    /// <summary>
    /// Combines the first predicate with the second using the logical "or".
    /// </summary>
    public static Expression<Func<T, bool>> Or<T>(this Expression<Func<T, bool>> expr1, Expression<Func<T, bool>> expr2)
    {
        var secondBody = expr2.Body.Replace(expr2.Parameters[0], expr1.Parameters[0]);
        return Expression.Lambda<Func<T, bool>>(Expression.OrElse(expr1.Body, secondBody), expr1.Parameters);
    }

    /// <summary>
    /// Combines the first predicate with the second using the logical "and".
    /// </summary>
    public static Expression<Func<T, bool>> And<T>(this Expression<Func<T, bool>> expr1, Expression<Func<T, bool>> expr2)
    {
        var secondBody = expr2.Body.Replace(expr2.Parameters[0], expr1.Parameters[0]);
        return Expression.Lambda<Func<T, bool>>(Expression.AndAlso(expr1.Body, secondBody), expr1.Parameters);
    }

    /// <summary>
    /// Negates the predicate.
    /// </summary>
    public static Expression<Func<T, bool>> Not<T>(this Expression<Func<T, bool>> expression)
    {
        var negated = Expression.Not(expression.Body);
        return Expression.Lambda<Func<T, bool>>(negated, expression.Parameters);
    }

    /// <summary>
    /// Check if the expression equals to the base And filter. If so, it should not be used to query the database.
    /// </summary>
    public static bool EqualsBaseAnd<T>(this Expression<Func<T, bool>> expression)
    {
        return expression.Body.ToString().Equals(BaseAnd<T>().Body.ToString());
    }

    /// <summary>
    /// Check if the expression equals to the base Or filter. If so, it should not be used to query the database.
    /// </summary>
    public static bool EqualsBaseOr<T>(this Expression<Func<T, bool>> expression)
    {
        return expression.Body.ToString().Equals(BaseOr<T>().Body.ToString());
    }

    private static Expression Replace(this Expression expression, Expression searchEx, Expression replaceEx)
    {
        return new ReplaceVisitor(searchEx, replaceEx).Visit(expression);
    }

    internal class ReplaceVisitor(Expression from, Expression to) : ExpressionVisitor
    {
        public override Expression Visit(Expression? node)
        {
            return node == from ? to : base.Visit(node)!;
        }
    }
}
