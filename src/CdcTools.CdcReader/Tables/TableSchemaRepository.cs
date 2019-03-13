using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CdcTools.CdcReader.Tables
{
    public class TableSchemaRepository : ITableSchemaRepository
    {
        private string _connString;

        public TableSchemaRepository(string connectionString)
        {
            _connString = connectionString;
        }

        public async Task<TableSchema> GetTableSchemaAsync(string catalog, string tableName)
        {
            var columns = await GetTableColumnsAsync(catalog,tableName);
            var primaryKeys = await GetTablePrimaryKeysAsync(tableName);

            var table = columns.GroupBy(x => new {x.Schema, x.TableName, x.Database}).First();

            var tableSchema = new TableSchema();
            tableSchema.Database = table.Key.Database;
            tableSchema.Schema = table.Key.Schema;
            tableSchema.TableName = table.Key.TableName;
            tableSchema.Columns = table.Select(x => new ColumnSchema()
            {
                DataType = x.DataType,
                MaxCharsLength = x.MaxCharsLength,
                Name = x.ColumnName,
                OrdinalPosition = x.OrdinalPosition,
                IsNullable = x.IsNullable
            })
            .ToList();

            tableSchema.PrimaryKeys = primaryKeys.Where(x => x.TableName.Equals(table.Key.TableName))
                                        .Select(x => new PrimaryKeyColumn() { ColumnName = x.ColumnName, OrdinalPosition = x.OrdinalPosition })
                                        .ToList();

            return tableSchema;
        }

        private async Task<List<TableColumn>> GetTableColumnsAsync(string catalog, string tableName)
        {
            var columns = new List<TableColumn>();

            using (var conn = await GetOpenConnectionAsync())
            {
                using (var command = conn.CreateCommand())
                {
                    command.CommandText = TableSchemaQueryBuilder.GetColumnsOfTableQuery(catalog);
                    command.CommandTimeout = 30;
                    command.Parameters.Add("TableName", SqlDbType.VarChar).Value = tableName;

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var column = GetTableColumn(reader);
                            if (column.ColumnName.IndexOf("msrepl", StringComparison.OrdinalIgnoreCase) == -1)
                                columns.Add(column);
                        }
                    }
                }
            }

            return columns;
        }

        private TableColumn GetTableColumn(SqlDataReader reader)
        {
            var column = new TableColumn();
            column.Database = reader["TABLE_CATALOG"].ToString();
            column.Schema = reader["TABLE_SCHEMA"].ToString();
            column.TableName = reader["TABLE_NAME"].ToString();
            column.ColumnName = reader["COLUMN_NAME"].ToString();
            column.OrdinalPosition = (int)reader["ORDINAL_POSITION"];
            column.DataType = reader["DATA_TYPE"].ToString();
            column.IsNullable = reader["IS_NULLABLE"].ToString().ToLower().Equals("yes") ? true : false;

            if (reader["CHARACTER_MAXIMUM_LENGTH"] != DBNull.Value)
                column.MaxCharsLength = (int)reader["CHARACTER_MAXIMUM_LENGTH"];

            if (reader["NUMERIC_SCALE"] != DBNull.Value)
                column.NumericScale = (int)reader["NUMERIC_SCALE"];

            if (reader["NUMERIC_PRECISION"] != DBNull.Value)
                column.NumericPrecision = (int)(byte)reader["NUMERIC_PRECISION"];

            return column;
        }

        private async Task<List<TablePrimaryKey>> GetTablePrimaryKeysAsync()
        {
            var primaryKeys = new List<TablePrimaryKey>();

            using (var conn = await GetOpenConnectionAsync())
            {
                using (var command = conn.CreateCommand())
                {
                    command.CommandText = TableSchemaQueryBuilder.GetPrimaryKeysQuery;
                    command.CommandTimeout = 30;

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var pk = GetPrimaryKey(reader);
                            primaryKeys.Add(pk);
                        }
                    }
                }
            }

            return primaryKeys;
        }

        private async Task<List<TablePrimaryKey>> GetTablePrimaryKeysAsync(string tableName)
        {
            var primaryKeys = new List<TablePrimaryKey>();

            using (var conn = await GetOpenConnectionAsync())
            {
                using (var command = conn.CreateCommand())
                {
                    command.CommandText = TableSchemaQueryBuilder.GetPrimaryKeyColumnsOfTableQuery();
                    command.CommandTimeout = 30;
                    command.Parameters.Add("TableName", SqlDbType.VarChar).Value = tableName;

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var pk = GetPrimaryKey(reader);
                            primaryKeys.Add(pk);
                        }
                    }
                }
            }

            return primaryKeys;
        }

        public async Task<List<string>> GetCdcTables(string databaseRegex, string tableRegex)
        {
            var cdcTables = new List<string>();

            var allDatabases = GetAllDatabases();
            allDatabases.Result.ForEach(c =>
            {
                if (Regex.IsMatch(c, databaseRegex))
                {
                    var table = GetAllTable(c);
                    table.Result.ForEach(t =>
                    {
                        if (Regex.IsMatch(t, tableRegex))
                        {
                            cdcTables.Add(c + ".dbo." + t);
                        }
                    });
                }

            });
            return cdcTables;
        }

        private async Task<List<string>> GetAllDatabases()
        {
            var databases = new List<string>();

            using (var conn = await GetOpenConnectionAsync())
            {
                using (var command = conn.CreateCommand())
                {
                    command.CommandText = "SELECT name FROM sys.databases WITH (nolock) WHERE state_desc = 'ONLINE'";
                    command.CommandTimeout = 30;

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var name = reader.GetString(0);
                            databases.Add(name);
                        }
                    }
                }
            }

            return databases;
        }

        //find all tables cdc enabled
        private async Task<List<string>> GetAllTable(string database)
        {
            var databases = new List<string>();

            using (var conn = await GetOpenConnectionAsync())
            {
                using (var command = conn.CreateCommand())
                {
                    command.CommandText = String.Format(
                        "SELECT name FROM {0}.sys.tables t WITH (nolock) JOIN {0}.[cdc].[change_tables] ct WITH (nolock) ON t.object_id = ct.source_object_id"
                        , database);
                    command.CommandTimeout = 30;

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var name = reader.GetString(0);
                            databases.Add(name);
                        }
                    }
                }
            }

            return databases;
        }

        private TablePrimaryKey GetPrimaryKey(SqlDataReader reader)
        {
            var pk = new TablePrimaryKey();
            pk.TableName = reader["TableName"].ToString();
            pk.ColumnName = reader["ColumnName"].ToString();
            pk.OrdinalPosition = (int)(byte)reader["OrdinalPosition"];

            return pk;
        }

        private async Task<SqlConnection> GetOpenConnectionAsync()
        {
            var conn = new SqlConnection(_connString);
            await conn.OpenAsync();

            return conn;
        }
    }
}
