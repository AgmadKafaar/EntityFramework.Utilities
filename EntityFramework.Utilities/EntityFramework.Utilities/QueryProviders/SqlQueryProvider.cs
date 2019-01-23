using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.Entity.Core.Objects;
using System.Data.SqlClient;
using System.Linq;
using System.Text.RegularExpressions;
using EntityFramework.Utilities.EfQuery;
using EntityFramework.Utilities.Expressions;
using EntityFramework.Utilities.Helpers;
using EntityFramework.Utilities.Mapping;

namespace EntityFramework.Utilities.QueryProviders
{
    public class SqlQueryProvider : IQueryProvider
    {
        public bool CanDelete => true;
        public bool CanUpdate => true;
        public bool CanInsert => true;
        public bool CanBulkUpdate => true;

        public string GetDeleteQuery(QueryInformation queryInfo) => $"DELETE FROM [{queryInfo.Schema}].[{queryInfo.Table}] {queryInfo.WhereSql}";

        public string GetUpdateQuery(QueryInformation predicateQueryInfo, QueryInformation modificationQueryInfo)
        {
            var msql = modificationQueryInfo.WhereSql.Replace("WHERE ", "");
            var indexOfAnd = msql.IndexOf("AND", StringComparison.OrdinalIgnoreCase);
            var update = indexOfAnd == -1 ? msql : msql.Substring(0, indexOfAnd).Trim();

            var updateRegex = new Regex(@"(\[[^\]]+\])[^=]+=(.+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            var match = updateRegex.Match(update);
            string updateSql;
            if (match.Success)
            {
                var col = match.Groups[1];
                var rest = match.Groups[2].Value;

                rest = SqlStringHelper.FixParantheses(rest);

                updateSql = $"{col.Value} = {rest}";
            }
            else
            {
                updateSql = string.Join(" = ", update.Split(new[] { " = " }, StringSplitOptions.RemoveEmptyEntries).Reverse());
            }

            return $"UPDATE [{predicateQueryInfo.Schema}].[{predicateQueryInfo.Table}] SET {updateSql} {predicateQueryInfo.WhereSql}";
        }

        public void InsertItems<T>(IEnumerable<T> items, string schema, string tableName, IList<ColumnMapping> properties, DbConnection storeConnection, int? batchSize, bool isUpdate = false)
        {
            using (var reader = new EfDataReader<T>(items, properties))
            {
                var con = storeConnection as SqlConnection;
                if (con != null && con.State != ConnectionState.Open)
                {
                    con.Open();
                }
                if (con == null) return;
                using (SqlBulkCopy copy = new SqlBulkCopy(con))
                {
                    copy.BatchSize = Math.Min(reader.RecordsAffected, batchSize ?? 15000); //default batch size
                    copy.DestinationTableName = !string.IsNullOrWhiteSpace(schema) ? $"[{schema}].[{tableName}]" : $"[{tableName}]";
                    copy.NotifyAfter = 0;
                    foreach (var i in Enumerable.Range(0, reader.FieldCount))
                    {
                        copy.ColumnMappings.Add(i, properties[i].NameInDatabase);
                    }
                    copy.WriteToServer(reader);
                    copy.Close();
                }
            }
        }

        public void UpdateItems<T>(IEnumerable<T> items, string schema, string tableName, IList<ColumnMapping> properties, DbConnection storeConnection, int? batchSize, UpdateSpecification<T> updateSpecification)
        {
            var tempTableName = $"temp_{tableName}_{DateTime.Now.Ticks}";
            var columnsToUpdate = updateSpecification.Properties.Select(p => p.GetPropertyName()).ToDictionary(x => x);
            var filtered = properties.Where(p => columnsToUpdate.ContainsKey(p.NameOnObject) || p.IsPrimaryKey).ToList();
            var columns = filtered.Select(c => $"[{c.NameInDatabase}] {c.DataType}");
            var pkConstraint = string.Join(", ", properties.Where(p => p.IsPrimaryKey).Select(c => $"[{c.NameInDatabase}]"));

            var str = $"CREATE TABLE {schema}.[{tempTableName}]({string.Join(", ", columns)}, PRIMARY KEY ({pkConstraint}))";

            var con = storeConnection as SqlConnection;
            if (con != null && con.State != ConnectionState.Open)
            {
                con.Open();
            }

            var setters = string.Join(",", filtered.Where(c => !c.IsPrimaryKey).Select(c => $"[{c.NameInDatabase}] = TEMP.[{c.NameInDatabase}]"));
            var pks = properties.Where(p => p.IsPrimaryKey).Select(x => $"ORIG.[{x.NameInDatabase}] = TEMP.[{x.NameInDatabase}]");
            var filter = string.Join(" and ", pks);
            var mergeCommand = $@"UPDATE [{tableName}] SET {setters} FROM [{tableName}] ORIG INNER JOIN [{tempTableName}] TEMP ON {filter}";

            using (var createCommand = new SqlCommand(str, con))
            using (var mCommand = new SqlCommand(mergeCommand, con))
            using (var dCommand = new SqlCommand($"DROP table {schema}.[{tempTableName}]", con))
            {
                createCommand.ExecuteNonQuery();
                InsertItems(items, schema, tempTableName, filtered, storeConnection, batchSize);
                mCommand.ExecuteNonQuery();
                dCommand.ExecuteNonQuery();
            }
        }

        public bool CanHandle(DbConnection storeConnection)
        {
            return storeConnection is SqlConnection;
        }

        public QueryInformation GetQueryInformation<T>(ObjectQuery<T> query)
        {
            var fromRegex = new Regex(@"FROM \[([^\]]+)\]\.\[([^\]]+)\] AS (\[[^\]]+\])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            var queryInfo = new QueryInformation();

            var str = query.ToTraceString();
            var match = fromRegex.Match(str);
            queryInfo.Schema = match.Groups[1].Value;
            queryInfo.Table = match.Groups[2].Value;
            queryInfo.Alias = match.Groups[3].Value;

            var i = str.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase);
            if (i > 0)
            {
                var whereClause = str.Substring(i);
                queryInfo.WhereSql = whereClause.Replace($"{queryInfo.Alias}.", "");
            }
            return queryInfo;
        }
    }
}