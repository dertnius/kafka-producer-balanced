using Microsoft.Extensions.DependencyInjection;
using MyDotNetApp.Data.Concrete;
using MyDotNetApp.Data.Repositories;
using MyDotNetApp.Data.UnitOfWork;
using System;
using System.Data;
using Microsoft.Data.SqlClient;

namespace MyDotNetApp.Data
{
    /// <summary>
    /// Extension methods for registering data access dependencies
    /// </summary>
    public static class DataExtensions
    {
        /// <summary>
        /// Registers all repository and Unit of Work dependencies
        /// </summary>
        public static IServiceCollection AddDataAccess(
            this IServiceCollection services,
            string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentNullException(nameof(connectionString));

            // Register connection factory
            services.AddScoped<IDbConnection>(sp =>
                new SqlConnection(connectionString));

            // Register Unit of Work
            services.AddScoped<IUnitOfWork>(sp =>
                new UnitOfWork.UnitOfWork(connectionString));

            // Register OutboxMessage repositories
            services.AddScoped<IOutboxReadRepository, OutboxReadRepository>();
            services.AddScoped<IOutboxWriteRepository, OutboxWriteRepository>();

            return services;
        }
    }
}
