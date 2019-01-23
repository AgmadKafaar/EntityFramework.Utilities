using EntityFramework.Utilities.Config;
using EntityFramework.Utilities.Expressions;
using EntityFramework.Utilities.Factory;
using EntityFramework.Utilities.Helpers;
using EntityFramework.Utilities.Mapping;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.Entity;
using System.Data.Entity.Core.EntityClient;
using System.Data.Entity.Core.Objects;
using System.Data.Entity.Infrastructure;
using System.Data.SqlClient;
using System.Linq;
using System.Linq.Expressions;

namespace EntityFramework.Utilities.BatchOperations
{
    public static class EfBatchOperation
    {
        public static IEfBatchOperationBase<T> For<TContext, T>(TContext context, IDbSet<T> set)
            where TContext : DbContext
            where T : class
        {
            return EfBatchOperation<TContext, T>.For(context, set);
        }
    }

    public class EfBatchOperation<TContext, T> : IEfBatchOperationBase<T>, IEfBatchOperationFiltered<T>
        where T : class
        where TContext : DbContext
    {
        private readonly ObjectContext _context;
        private readonly DbContext _dbContext;
        private Expression<Func<T, bool>> _predicate;

        private EfBatchOperation(TContext context)
        {
            _dbContext = context;
            _context = (context as IObjectContextAdapter).ObjectContext;
        }

        public static IEfBatchOperationBase<T> For(TContext context, IDbSet<T> set) => new EfBatchOperation<TContext, T>(context);

        /// <summary>
        /// Bulk insert all items if the Provider supports it. Otherwise it will use the default insert unless Configuration.DisableDefaultFallback is set to true in which case it would throw an exception.
        /// </summary>
        /// <param name="items">The items to insert</param>
        /// <param name="connection">The DbConnection to use for the insert. Only needed when for example a profiler wraps the connection. Then you need to provide a connection of the type the provider use.</param>
        /// <param name="batchSize">The size of each batch. Default depends on the provider. SqlProvider uses 15000 as default</param>
        public void InsertAll<TEntity>(IEnumerable<TEntity> items, DbConnection connection = null, int? batchSize = null) where TEntity : class, T
        {
            var con = _context.Connection as EntityConnection;
            var itemsToInsert = items as TEntity[] ?? items.ToArray();
            if (con == null && connection == null)
            {
                Configuration.Log("No provider could be found because the Connection didn't implement System.Data.EntityClient.EntityConnection");
                Fallbacks.DefaultInsertAll(_context, itemsToInsert);
            }

            if (con == null) return;
            var connectionToUse = connection ?? con.StoreConnection;
            var currentType = typeof(TEntity);
            var provider = Configuration.Providers.FirstOrDefault(p => p.CanHandle(connectionToUse));
            if (provider != null && provider.CanInsert)
            {
                var mapping = EfMappingFactory.GetMappingsForContext(_dbContext);
                var typeMapping = mapping.TypeMappings[typeof(T)];
                var tableMapping = typeMapping.TableMappings.First();

                var properties = tableMapping.PropertyMappings
                    .Where(p => currentType.IsSubclassOf(p.ForEntityType) || p.ForEntityType == currentType)
                    .Select(p => new ColumnMapping { NameInDatabase = p.ColumnName, NameOnObject = p.PropertyName, DataType = p.DataType, IsPrimaryKey = p.IsPrimaryKey}).ToList();
                if (tableMapping.TphConfiguration != null)
                {
                    properties.Add(new ColumnMapping
                    {
                        NameInDatabase = tableMapping.TphConfiguration.ColumnName,
                        StaticValue = tableMapping.TphConfiguration.Mappings[typeof(TEntity)]
                    });
                }

                provider.InsertItems(itemsToInsert, tableMapping.Schema, tableMapping.TableName, properties, connectionToUse, batchSize);
            }
            else
            {
                Configuration.Log($"Found provider: {provider?.GetType().Name ?? "[]"} for {connectionToUse.GetType().Name}");
                Fallbacks.DefaultInsertAll(_context, itemsToInsert);
            }
        }

        public void UpdateAll<TEntity>(IEnumerable<TEntity> items, Action<UpdateSpecification<TEntity>> updateSpecification, DbConnection connection = null, int? batchSize = null) where TEntity : class, T
        {
            var con = _context.Connection as EntityConnection;
            var itemsToInsert = items as TEntity[] ?? items.ToArray();
            if (con == null && connection == null)
            {
                Configuration.Log("No provider could be found because the Connection didn't implement System.Data.EntityClient.EntityConnection");
                Fallbacks.DefaultInsertAll(_context, itemsToInsert);
            }

            if (con == null) return;
            var connectionToUse = connection ?? con.StoreConnection;
            var currentType = typeof(TEntity);
            var provider = Configuration.Providers.FirstOrDefault(p => p.CanHandle(connectionToUse));
            if (provider != null && provider.CanBulkUpdate)
            {
                var mapping = EfMappingFactory.GetMappingsForContext(_dbContext);
                var typeMapping = mapping.TypeMappings[typeof(T)];
                var tableMapping = typeMapping.TableMappings.First();

                var properties = tableMapping.PropertyMappings
                    .Where(p => currentType.IsSubclassOf(p.ForEntityType) || p.ForEntityType == currentType)
                    .Select(p => new ColumnMapping
                    {
                        NameInDatabase = p.ColumnName,
                        NameOnObject = p.PropertyName,
                        DataType = p.DataTypeFull,
                        IsPrimaryKey = p.IsPrimaryKey
                    }).ToList();

                var spec = new UpdateSpecification<TEntity>();
                updateSpecification(spec);
                provider.UpdateItems(itemsToInsert, tableMapping.Schema, tableMapping.TableName, properties, connectionToUse, batchSize, spec);
            }
            else
            {
                Configuration.Log("Found provider: " + (provider?.GetType().Name ?? "[]") + " for " + connectionToUse.GetType().Name);
                Fallbacks.DefaultInsertAll(_context, itemsToInsert);
            }
        }

        public IEfBatchOperationFiltered<T> Where(Expression<Func<T, bool>> predicate)
        {
            _predicate = predicate;
            return this;
        }

        public int Delete()
        {
            var con = _context.Connection as EntityConnection;
            if (con == null)
            {
                Configuration.Log("No provider could be found because the Connection didn't implement System.Data.EntityClient.EntityConnection");
                return Fallbacks.DefaultDelete(_context, _predicate);
            }

            var provider = Configuration.Providers.FirstOrDefault(p => p.CanHandle(con.StoreConnection));
            if (provider != null && provider.CanDelete)
            {
                var set = _context.CreateObjectSet<T>();
                var query = (ObjectQuery<T>)set.Where(_predicate);
                var whereClause = ExpressionHelper.GetSqlExpression(_predicate.Body, ProviderEnum.MySql);
                var queryInformation = provider.GetQueryInformation(query);
                queryInformation.WhereSql = $"WHERE {whereClause}";

                var delete = provider.GetDeleteQuery(queryInformation);
                var parameters = query.Parameters.Select(p => new SqlParameter { Value = p.Value, ParameterName = p.Name }).ToArray<object>();
                return _context.ExecuteStoreCommand(delete, parameters);
            }
            Configuration.Log($"Found provider: {(provider?.GetType().Name ?? "[]")} for {con.StoreConnection.GetType().Name}");
            return Fallbacks.DefaultDelete(_context, _predicate);
        }

        public int Update<TP>(Expression<Func<T, TP>> prop, Expression<Func<T, TP>> modifier)
        {
            var con = _context.Connection as EntityConnection;
            if (con == null)
            {
                Configuration.Log("No provider could be found because the Connection didn't implement System.Data.EntityClient.EntityConnection");
                return Fallbacks.DefaultUpdate(_context, _predicate, prop, modifier);
            }

            var provider = Configuration.Providers.FirstOrDefault(p => p.CanHandle(con.StoreConnection));
            if (provider != null && provider.CanUpdate)
            {
                var set = _context.CreateObjectSet<T>();

                var query = (ObjectQuery<T>)set.Where(_predicate);
                var whereClause = ExpressionHelper.GetSqlExpression(_predicate.Body, ProviderEnum.MySql);
                var queryInformation = provider.GetQueryInformation(query);

                var updateExpression = ExpressionHelper.CombineExpressions(prop, modifier);

                var mquery = (ObjectQuery<T>)_context.CreateObjectSet<T>().Where(updateExpression);
                var mqueryInfo = provider.GetQueryInformation(mquery);

                queryInformation.WhereSql = $"WHERE {whereClause}";
                var update = provider.GetUpdateQuery(queryInformation, mqueryInfo);

                var parameters = query.Parameters
                    .Concat(mquery.Parameters)
                    .Select(p => new SqlParameter { Value = p.Value, ParameterName = p.Name })
                    .ToArray<object>();

                return _context.ExecuteStoreCommand(update, parameters);
            }
            Configuration.Log($"Found provider: {provider?.GetType().Name ?? "[]"} for {con.StoreConnection.GetType().Name}");
            return Fallbacks.DefaultUpdate(_context, _predicate, prop, modifier);
        }
    }
}