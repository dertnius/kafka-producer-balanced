using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using MyDotNetApp.Data.Helpers;

namespace MyDotNetApp.Data.Repositories
{
    /// <summary>
    /// Base read repository implementation using Dapper
    /// </summary>
    public class ReadRepository<T> : IReadRepository<T> where T : class
    {
        protected readonly IDbConnection _connection;

        public ReadRepository(IDbConnection connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        public virtual async Task<T?> GetByKeyAsync(params object[] keys)
        {
            KeyHelper.ValidateKeyCount<T>(keys.Length);

            var keyProps = KeyHelper.GetKeyProperties<T>();
            var tableName = KeyHelper.GetTableName<T>();

            var whereClauseParts = new System.Collections.Generic.List<string>();
            for (int i = 0; i < keyProps.Length; i++)
            {
                whereClauseParts.Add($"{keyProps[i].Name} = @Key{i}");
            }
            var whereClause = string.Join(" AND ", whereClauseParts);

            var sql = $"SELECT * FROM {tableName} WHERE {whereClause}";

            var parameters = new DynamicParameters();
            for (int i = 0; i < keys.Length; i++)
            {
                parameters.Add($"@Key{i}", keys[i]);
            }

            return await _connection.QueryFirstOrDefaultAsync<T>(sql, parameters);
        }

        public virtual async Task<IEnumerable<T>> GetAllAsync()
        {
            var tableName = KeyHelper.GetTableName<T>();
            var sql = $"SELECT * FROM {tableName}";
            return await _connection.QueryAsync<T>(sql);
        }

        public async Task<IEnumerable<T>> QueryAsync(string sql, object? param = null)
        {
            if (string.IsNullOrWhiteSpace(sql))
                throw new ArgumentNullException(nameof(sql));

            return await _connection.QueryAsync<T>(sql, param);
        }

        public async Task<T?> QueryFirstOrDefaultAsync(string sql, object? param = null)
        {
            if (string.IsNullOrWhiteSpace(sql))
                throw new ArgumentNullException(nameof(sql));

            return await _connection.QueryFirstOrDefaultAsync<T>(sql, param);
        }
    }
}
