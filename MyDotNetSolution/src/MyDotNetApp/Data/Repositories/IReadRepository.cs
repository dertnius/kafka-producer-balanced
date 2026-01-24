using System.Collections.Generic;
using System.Threading.Tasks;

namespace MyDotNetApp.Data.Repositories
{
    /// <summary>
    /// Read-only repository interface for querying entities
    /// </summary>
    public interface IReadRepository<T> where T : class
    {
        /// <summary>
        /// Gets an entity by its key(s)
        /// </summary>
        /// <param name="keys">One or more key values (in order of [Key] attributes)</param>
        Task<T?> GetByKeyAsync(params object[] keys);

        /// <summary>
        /// Gets all entities
        /// </summary>
        Task<IEnumerable<T>> GetAllAsync();

        /// <summary>
        /// Executes a custom query
        /// </summary>
        /// <param name="sql">SQL query</param>
        /// <param name="param">Query parameters</param>
        Task<IEnumerable<T>> QueryAsync(string sql, object? param = null);

        /// <summary>
        /// Executes a custom query returning first or default result
        /// </summary>
        Task<T?> QueryFirstOrDefaultAsync(string sql, object? param = null);
    }
}
