using System.Threading.Tasks;

namespace MyDotNetApp.Data.Repositories
{
    /// <summary>
    /// Write repository interface for creating, updating, and deleting entities
    /// </summary>
    public interface IWriteRepository<T> where T : class
    {
        /// <summary>
        /// Inserts a new entity
        /// </summary>
        /// <returns>The generated key value (for auto-increment) or the entity's key value</returns>
        Task<object> InsertAsync(T entity);

        /// <summary>
        /// Updates an existing entity
        /// </summary>
        Task<bool> UpdateAsync(T entity);

        /// <summary>
        /// Deletes an entity by its key(s)
        /// </summary>
        /// <param name="keys">One or more key values (in order of [Key] attributes)</param>
        Task<bool> DeleteAsync(params object[] keys);

        /// <summary>
        /// Executes a custom SQL command
        /// </summary>
        /// <param name="sql">SQL command</param>
        /// <param name="param">Command parameters</param>
        /// <returns>Number of affected rows</returns>
        Task<int> ExecuteAsync(string sql, object? param = null);
    }
}
