using System;
using System.Data;

namespace MyDotNetApp.Data.UnitOfWork
{
    /// <summary>
    /// Interface for Unit of Work pattern - manages transactions and connections
    /// </summary>
    public interface IUnitOfWork : IDisposable
    {
        /// <summary>
        /// Gets the database connection
        /// </summary>
        IDbConnection Connection { get; }

        /// <summary>
        /// Gets the current transaction (if active)
        /// </summary>
        IDbTransaction? Transaction { get; }

        /// <summary>
        /// Begins a new transaction
        /// </summary>
        void BeginTransaction();

        /// <summary>
        /// Commits the current transaction
        /// </summary>
        void Commit();

        /// <summary>
        /// Rollbacks the current transaction
        /// </summary>
        void Rollback();
    }
}
