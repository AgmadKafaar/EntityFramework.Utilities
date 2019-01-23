using EntityFramework.Utilities.Mapping;
using System;
using System.Linq.Expressions;

namespace EntityFramework.Utilities.Expressions
{
    internal static class ExpressionHelper
    {
        internal static Expression<Func<T, bool>> CombineExpressions<T, TP>(Expression<Func<T, TP>> prop, Expression<Func<T, TP>> modifier) where T : class
        {
            var propRewritten = new ReplaceVisitor(prop.Parameters[0], modifier.Parameters[0]).Visit(prop.Body);
            var expr = Expression.Equal(propRewritten, modifier.Body);
            var final = Expression.Lambda<Func<T, bool>>(expr, modifier.Parameters[0]);
            return final;
        }

        public static string GetPropertyName<TSource, TProperty>(this Expression<Func<TSource, TProperty>> propertyLambda)
        {
            var temp = propertyLambda.Body;
            while (temp is UnaryExpression)
            {
                temp = (temp as UnaryExpression).Operand;
            }
            MemberExpression member = temp as MemberExpression;
            return member?.Member.Name;
        }

        //http://stackoverflow.com/a/2824409/507279
        internal static Action<T, TP> PropertyExpressionToSetter<T, TP>(Expression<Func<T, TP>> prop)
        {
            // re-write in .NET 4.0 as a "set"
            var member = (MemberExpression)prop.Body;
            var param = Expression.Parameter(typeof(TP), "value");
            var set = Expression.Lambda<Action<T, TP>>(
                Expression.Assign(member, param), prop.Parameters[0], param);

            // compile it
            return set.Compile();
        }

        /// <summary>
        /// https://stackoverflow.com/questions/15177443/parsing-a-single-statement-boolean-expression-tree
        /// </summary>
        /// <param name="expression"></param>
        /// <param name="providerEnum"></param>
        /// <returns>where clause string</returns>
        public static string GetSqlExpression(Expression expression, ProviderEnum providerEnum)
        {
            if (expression is BinaryExpression)
            {
                return string.Format("({0} {1} {2})",
                    GetSqlExpression(((BinaryExpression)expression).Left, providerEnum),
                    GetBinaryOperator((BinaryExpression)expression),
                    GetSqlExpression(((BinaryExpression)expression).Right, providerEnum));
            }

            var memberExpression = expression as MemberExpression;
            if (memberExpression != null)
            {
                MemberExpression member = memberExpression;

                // it is somewhat naive to make a bool member into "Member = TRUE"
                // since the expression "Member == true" will turn into "(Member = TRUE) = TRUE"
                if (member.Type == typeof(bool))
                {
                    switch (providerEnum)
                    {
                        case ProviderEnum.MySql: return $"(`{member.Member.Name}` = TRUE)";
                        case ProviderEnum.SqlServer: return $"([{member.Member.Name}] = TRUE)";
                    }
                }
                switch (providerEnum)
                {
                    case ProviderEnum.MySql: return $"(`{member.Member.Name}`)";
                    case ProviderEnum.SqlServer: return $"([{member.Member.Name}])";
                }
            }

            var constantExpression = expression as ConstantExpression;
            if (constantExpression != null)
            {
                ConstantExpression constant = constantExpression;

                // create a proper SQL representation for each type
                if (constant.Type == typeof(int))
                {
                    return constant.Value.ToString();
                }
                if (constant.Type == typeof(string))
                {
                    return $"'{constant.Value}'";
                }

                if (constant.Type == typeof(bool))
                {
                    return (bool)constant.Value ? "TRUE" : "FALSE";
                }

                throw new ArgumentException();
            }
            var unaryExpression = expression as UnaryExpression;
            if (unaryExpression == null) throw new ArgumentException();

            var unary = unaryExpression;
            return GetSqlExpression(unary.Operand, providerEnum);
        }

        public static string GetBinaryOperator(BinaryExpression expression)
        {
            switch (expression.NodeType)
            {
                case ExpressionType.Equal:
                    return "=";

                case ExpressionType.NotEqual:
                    return "<>";

                case ExpressionType.OrElse:
                    return "OR";

                case ExpressionType.AndAlso:
                    return "AND";

                case ExpressionType.LessThan:
                    return "<";

                case ExpressionType.GreaterThan:
                    return ">";

                default:
                    throw new ArgumentException();
            }
        }
    }
}