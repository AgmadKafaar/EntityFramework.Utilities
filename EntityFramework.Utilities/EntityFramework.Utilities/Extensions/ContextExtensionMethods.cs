using System;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq.Expressions;
using EntityFramework.Utilities.Expressions;

namespace EntityFramework.Utilities.Extensions
{
    public static class ContextExtensionMethods
    {
        public class AttachAndModifyContext<T> where T : class{
            private readonly DbEntityEntry<T> _entry;

            public AttachAndModifyContext(DbEntityEntry<T> entry)
            {
                _entry = entry;
            }
            public AttachAndModifyContext<T> Set<TProp>(Expression<Func<T, TProp>> property, TProp value){

                var setter = ExpressionHelper.PropertyExpressionToSetter(property);
                setter(_entry.Entity, value);
                _entry.Property(property).IsModified = true;
                return this;
            }
        }
        public static AttachAndModifyContext<T> AttachAndModify<T>(this DbContext source, T item) where T : class
        {
            var set = source.Set<T>();
            set.Attach(item);
            var entry = source.Entry(item);

            return new AttachAndModifyContext<T>(entry);
        }
    }
}
