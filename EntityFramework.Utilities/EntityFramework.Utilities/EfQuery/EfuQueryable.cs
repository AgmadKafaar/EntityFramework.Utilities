using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using EntityFramework.Utilities.Mapping;
using EntityFramework.Utilities.QueryProviders;
using IQueryProvider = System.Linq.IQueryProvider;

namespace EntityFramework.Utilities.EfQuery
{
    public class EfuQueryable<T> : IOrderedQueryable<T>, IIncludeContainer
    {
        private readonly Expression _expression;
        private readonly EfuQueryProvider<T> _provider;
        private readonly List<IncludeExecuter> _includes = new List<IncludeExecuter>();

        public IEnumerable<IncludeExecuter> Includes => _includes;

        public EfuQueryable(IQueryable source)
        {
            _expression = Expression.Constant(this);
            _provider = new EfuQueryProvider<T>(source);
        }

        public EfuQueryable(IQueryable source, Expression e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            _expression = e;
            _provider = new EfuQueryProvider<T>(source);
        }

        public IEnumerator<T> GetEnumerator() => _provider.ExecuteEnumerable(_expression).Cast<T>().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _provider.ExecuteEnumerable(_expression).GetEnumerator();

        public EfuQueryable<T> Include(IncludeExecuter include)
        {
            _includes.Add(include);
            return this;
        }

        public Type ElementType => typeof(T);

        public Expression Expression => _expression;

        public IQueryProvider Provider => _provider;
    }
}