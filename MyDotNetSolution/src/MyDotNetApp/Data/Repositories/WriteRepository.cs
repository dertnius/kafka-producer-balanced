using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using MyDotNetApp.Data.Helpers;
using MyDotNetApp.Data.UnitOfWork;

namespace MyDotNetApp.Data.Repositories
{
    /// <summary>
    /// Base write repository implementation using Dapper
    /// </summary>
    public class WriteRepository<T> : IWriteRepository<T> where T : class
    {
        protected readonly IUnitOfWork _unitOfWork;

        public WriteRepository(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public virtual async Task<object> InsertAsync(T entity)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            var tableName = KeyHelper.GetTableName<T>();
            var keyProps = KeyHelper.GetKeyProperties<T>();
            var nonKeyProps = KeyHelper.GetNonKeyProperties<T>();

            if (nonKeyProps.Length == 0)
                throw new InvalidOperationException($"No non-key properties found on {typeof(T).Name}");

            var columns = string.Join(", ", nonKeyProps.Select(p => p.Name));
            var values = string.Join(", ", nonKeyProps.Select(p => $"@{p.Name}"));

            // For auto-increment keys, return the generated ID
            var isAutoIncrement = keyProps.Length == 1 && keyProps[0].PropertyType == typeof(int);
            var returnClause = isAutoIncrement ? "; SELECT CAST(SCOPE_IDENTITY() as int)" : "";

            var sql = $"INSERT INTO {tableName} ({columns}) VALUES ({values}){returnClause}";

            if (isAutoIncrement)
            {
                var id = await _unitOfWork.Connection.ExecuteScalarAsync<int>(
                    sql, entity, _unitOfWork.Transaction);
                return id;
            }
            else
            {
                await _unitOfWork.Connection.ExecuteAsync(sql, entity, _unitOfWork.Transaction);
                return KeyHelper.GetKeyValue(entity);
            }
        }

        public virtual async Task<bool> UpdateAsync(T entity)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            var tableName = KeyHelper.GetTableName<T>();
            var keyProps = KeyHelper.GetKeyProperties<T>();
            var nonKeyProps = KeyHelper.GetNonKeyProperties<T>();

            if (nonKeyProps.Length == 0)
                throw new InvalidOperationException($"No non-key properties found on {typeof(T).Name}");

            var setClause = string.Join(", ",
                nonKeyProps.Select(p => $"{p.Name} = @{p.Name}"));
            var whereClause = string.Join(" AND ",
                keyProps.Select(p => $"{p.Name} = @{p.Name}"));

            var sql = $"UPDATE {tableName} SET {setClause} WHERE {whereClause}";

            var result = await _unitOfWork.Connection.ExecuteAsync(
                sql, entity, _unitOfWork.Transaction);
            return result > 0;
        }

        public virtual async Task<bool> DeleteAsync(params object[] keys)
        {
            KeyHelper.ValidateKeyCount<T>(keys.Length);

            var tableName = KeyHelper.GetTableName<T>();
            var keyProps = KeyHelper.GetKeyProperties<T>();

            var whereClause = string.Join(" AND ",
                keyProps.Select((prop, idx) => $"{prop.Name} = @Key{idx}"));

            var sql = $"DELETE FROM {tableName} WHERE {whereClause}";

            var parameters = new DynamicParameters();
            for (int i = 0; i < keys.Length; i++)
            {
                parameters.Add($"@Key{i}", keys[i]);
            }

            var result = await _unitOfWork.Connection.ExecuteAsync(
                sql, parameters, _unitOfWork.Transaction);
            return result > 0;
        }

        public async Task<int> ExecuteAsync(string sql, object? param = null)
        {
            if (string.IsNullOrWhiteSpace(sql))
                throw new ArgumentNullException(nameof(sql));

            return await _unitOfWork.Connection.ExecuteAsync(
                sql, param, _unitOfWork.Transaction);
        }
    }
}
