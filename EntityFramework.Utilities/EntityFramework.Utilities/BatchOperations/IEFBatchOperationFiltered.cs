using System;
using System.Linq.Expressions;

namespace EntityFramework.Utilities.BatchOperations
{
    public interface IEfBatchOperationFiltered<T>
    {
        int Delete();

        int Update<TP>(Expression<Func<T, TP>> prop, Expression<Func<T, TP>> modifier);
    }
}