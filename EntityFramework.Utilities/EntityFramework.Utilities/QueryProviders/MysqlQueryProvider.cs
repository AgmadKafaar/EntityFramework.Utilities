using EntityFramework.Utilities.Expressions;
using EntityFramework.Utilities.Helpers;
using EntityFramework.Utilities.Mapping;
using EntityFramework.Utilities.Reflection;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.Entity.Core.Objects;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace EntityFramework.Utilities.QueryProviders
{
    public class MysqlQueryProvider : IQueryProvider
    {
        public bool CanDelete => true;
        public bool CanUpdate => true;
        public bool CanInsert => true;
        public bool CanBulkUpdate => true;

        public string GetDeleteQuery(QueryInformation queryInformation) => $"DELETE FROM `{queryInformation.Table}` {queryInformation.WhereSql}";

        public string GetUpdateQuery(QueryInformation predicateQueryInfo, QueryInformation modificationQueryInfo)
        {
            var msql = modificationQueryInfo.WhereSql.Replace("WHERE ", "");
            var indexOfAnd = msql.IndexOf("AND", StringComparison.OrdinalIgnoreCase);
            var update = indexOfAnd == -1 ? msql : msql.Substring(0, indexOfAnd).Trim();

            var updateRegex = new Regex(@"(`[^`].+`)[^=]+=(.+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            var match = updateRegex.Match(update);
            string updateSql;
            if (match.Success)
            {
                var col = match.Groups[1];
                var rest = match.Groups[2].Value;

                rest = SqlStringHelper.FixParantheses(rest);

                updateSql = col.Value + " = " + rest;
            }
            else
            {
                updateSql = string.Join(" = ", update.Split(new[] { " = " }, StringSplitOptions.RemoveEmptyEntries).Reverse());
            }

            return $"UPDATE `{predicateQueryInfo.Table}` `{predicateQueryInfo.Alias}` SET {updateSql} {predicateQueryInfo.WhereSql}";
        }

        public void InsertItems<T>(IEnumerable<T> items, string schema, string tableName, IList<ColumnMapping> properties, DbConnection storeConnection, int? batchSize, bool isUpdate = false)
        {
            var itemsToInsert = items as T[] ?? items.ToArray();
            var con = storeConnection as MySqlConnection;
            const int buffer = 65536;

            int batchTimes = batchSize.HasValue ? itemsToInsert.Length / batchSize.Value + 1 : 1;
            batchSize = batchTimes == 1 ? itemsToInsert.Length : batchSize;
            for (int i = 0; i < batchTimes; i++)
            {
                //1. write to temp csv file.
                var dbColumns = !isUpdate ? properties.Where(x => !x.IsPrimaryKey).ToList() : properties.ToList();
                string path = $"{Path.GetTempPath()}{Guid.NewGuid()}.csv";
                string columns = string.Join(",", dbColumns.Select(x => x.NameInDatabase));
                File.WriteAllText(path, $"{columns}{Environment.NewLine}");

                var reflectedObjs = new object[dbColumns.Count];
                using (var stream = new StreamWriter(path, true, Encoding.UTF8, buffer))
                {
                    foreach (var x in itemsToInsert
                        .Skip(batchSize.GetValueOrDefault() * i) // Arbitrary
                        .Take(batchSize.GetValueOrDefault())) // Batching

                    {
                        for (var index = 0; index < dbColumns.Count; index++)
                        {
                            var columnMapping = dbColumns[index];
                            reflectedObjs[index] = PropertyCallAdapterProvider<T>
                                .GetInstance(columnMapping.NameOnObject)
                                .InvokeGet(x);
                            var val = reflectedObjs[index]?.ToString();
                            if (val != null && columnMapping.DataType.Equals("bool", StringComparison.OrdinalIgnoreCase) &&
                                (val.Equals("False") || val.Equals("True")))
                            {
                                reflectedObjs[index] = val.Equals("False") ? 0 : 1;
                            }

                            if (columnMapping.DataType.Equals("varchar", StringComparison.OrdinalIgnoreCase) || columnMapping.DataType.Equals("text", StringComparison.OrdinalIgnoreCase))
                            {
                                reflectedObjs[index] = $@"""{reflectedObjs[index]}""";
                            }
                            if (!columnMapping.DataType.Equals("datetime", StringComparison.OrdinalIgnoreCase))
                                continue;
                            var reflectedObj = reflectedObjs[index];
                            if (reflectedObj != null)
                                reflectedObjs[index] = ((DateTime)reflectedObj).ToString("yyyy-MM-dd HH:mm:ss.fff");
                        }
                        stream.WriteLine(string.Join(",", reflectedObjs));
                    }
                }

                if (con != null && con.State != ConnectionState.Open)
                {
                    con.Open();
                }

                //2. bulk import mysql from file
                MySqlBulkLoader loader = new MySqlBulkLoader(con)
                {
                    TableName = tableName,
                    FieldTerminator = ",",
                    LineTerminator = Environment.NewLine,
                    FileName = path,
                    NumberOfLinesToSkip = 1,
                    FieldQuotationCharacter = '"'
                };

                // we do not know columns a priori
                // and Columns is readonly - so this.
                loader.Columns.AddRange(dbColumns.Select(x => x.NameInDatabase).ToList());

                loader.Load();

                //3. remove temporary file after usage
                File.Delete(path);
            }
        }

        public void UpdateItems<T>(IEnumerable<T> items, string schema, string tableName, IList<ColumnMapping> properties, DbConnection storeConnection, int? batchSize, UpdateSpecification<T> updateSpecification)
        {
            var tempTableName = $"temp_{tableName}_{DateTime.Now.Ticks}";
            var columnsToUpdate = updateSpecification.Properties.Select(p => p.GetPropertyName()).ToDictionary(x => x);
            var filtered = properties.Where(p => columnsToUpdate.ContainsKey(p.NameOnObject) || p.IsPrimaryKey).ToList();
            var columns = filtered.Select(c => $"`{c.NameInDatabase}` {c.DataType}");
            var pkConstraint = string.Join(", ", properties.Where(p => p.IsPrimaryKey).Select(c => $"`{c.NameInDatabase}`"));

            var str = $"CREATE TABLE {tempTableName} ({string.Join(", ", columns)}, PRIMARY KEY ({pkConstraint}))";

            var con = storeConnection as MySqlConnection;
            if (con != null && con.State != ConnectionState.Open)
            {
                con.Open();
            }

            var setters = string.Join(",", filtered.Where(c => !c.IsPrimaryKey).Select(c => $" ORIG.`{c.NameInDatabase}` = TEMP.`{c.NameInDatabase}`"));
            var pks = properties.Where(p => p.IsPrimaryKey).Select(x => $"ORIG.`{x.NameInDatabase}` = TEMP.`{x.NameInDatabase}`");
            var filter = string.Join(" and ", pks);
            var mergeCommand = $@"UPDATE {tableName} ORIG INNER JOIN {tempTableName} TEMP ON {filter} SET {setters} ";

            using (var createCommand = new MySqlCommand(str, con))
            using (var mCommand = new MySqlCommand(mergeCommand, con))
            using (var dCommand = new MySqlCommand($"DROP table {tempTableName}", con))
            {
                createCommand.ExecuteNonQuery();
                InsertItems(items, schema, tempTableName, filtered, storeConnection, batchSize, true);
                mCommand.ExecuteNonQuery();
                dCommand.ExecuteNonQuery();
            }
        }

        public bool CanHandle(DbConnection storeConnection)
        {
            return storeConnection is MySqlConnection;
        }

        public QueryInformation GetQueryInformation<T>(ObjectQuery<T> query)
        {
            var fromRegex = new Regex(@"FROM `([^`]+)` AS `([^`]+)`", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            var queryInfo = new QueryInformation();
            var str = query.ToTraceString();

            var match = fromRegex.Match(str);
            queryInfo.Table = match.Groups[1].Value;
            queryInfo.Alias = match.Groups[2].Value;

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