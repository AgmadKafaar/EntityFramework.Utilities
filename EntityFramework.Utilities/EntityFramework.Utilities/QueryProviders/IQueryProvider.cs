using System.Collections.Generic;
using System.Data.Common;
using System.Data.Entity.Core.Objects;
using EntityFramework.Utilities.Mapping;

namespace EntityFramework.Utilities.QueryProviders
{
    public interface IQueryProvider
    {
        bool CanDelete { get; }
        bool CanUpdate { get; }
        bool CanInsert { get; }
        bool CanBulkUpdate { get; }

        string GetDeleteQuery(QueryInformation queryInformation);

        string GetUpdateQuery(QueryInformation predicateQueryInfo, QueryInformation modificationQueryInfo);

        void InsertItems<T>(IEnumerable<T> items, string schema, string tableName, IList<ColumnMapping> properties, DbConnection storeConnection, int? batchSize, bool isUpdate = false);

        void UpdateItems<T>(IEnumerable<T> items, string schema, string tableName, IList<ColumnMapping> properties, DbConnection storeConnection, int? batchSize, UpdateSpecification<T> updateSpecification);

        bool CanHandle(DbConnection storeConnection);

        QueryInformation GetQueryInformation<T>(ObjectQuery<T> query);
    }
}