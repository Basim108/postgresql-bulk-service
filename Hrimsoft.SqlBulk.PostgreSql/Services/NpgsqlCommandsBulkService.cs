﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Hrimsoft.SqlBulk.PostgreSql
{
    /// <summary>
    /// Implementation by using Npgsql DbCommand
    /// </summary>
    public class NpgsqlCommandsBulkService : IPostgreSqlBulkService
    {
        private readonly ILogger<NpgsqlCommandsBulkService> _logger;
        private readonly BulkServiceOptions _options;
        private readonly IInsertSqlCommandBuilder _insertCommandBuilder;

        public NpgsqlCommandsBulkService(
            BulkServiceOptions options,
            ILoggerFactory loggerFactory,
            IInsertSqlCommandBuilder insertCommandBuilder)
        {
            _options = options;
            _insertCommandBuilder = insertCommandBuilder;
            _logger = loggerFactory.CreateLogger<NpgsqlCommandsBulkService>();
        }

        /// <summary>
        /// Inserts elements
        /// </summary>
        /// <param name="connection">Connection to a database</param>
        /// <param name="elements">Elements that have to be inserted</param>
        /// <param name="cancellationToken"></param>
        /// <typeparam name="TEntity">Type of instances that have to be inserted</typeparam>
        /// <returns>Returns items that were inserted with updated autogenerated ids</returns>
        public async Task<ICollection<TEntity>> InsertAllAsync<TEntity>(
            [NotNull] NpgsqlConnection connection,
            [NotNull] ICollection<TEntity> elements,
            CancellationToken cancellationToken)
            where TEntity : class
        {
            var entityType = typeof(TEntity);
            if (!_options.SupportedEntityTypes.ContainsKey(entityType))
                throw new ArgumentException($"Mapping for type '{entityType.FullName}' was not found.", nameof(elements));

            var entityProfile = _options.SupportedEntityTypes[entityType];
            var maximumEntitiesPerSent = entityProfile.MaximumSentElements > 0
                ? entityProfile.MaximumSentElements
                : _options.MaximumSentElements;

            var result = new List<TEntity>(elements.Count);

            if (maximumEntitiesPerSent == 0)
            {
                var subset = await InsertPortionAsync(connection, elements, entityProfile, cancellationToken);
                result.AddRange(subset);
            }
            else
            {
                var iterations = Math.Round((decimal) elements.Count / maximumEntitiesPerSent, MidpointRounding.AwayFromZero);
                for (var i = 0; i < iterations; i++)
                {
                    var portion = elements.Skip(i * maximumEntitiesPerSent).Take(maximumEntitiesPerSent).ToList();
                    var subset = await InsertPortionAsync(connection, portion, entityProfile, cancellationToken);
                    result.AddRange(subset);
                }
            }

            return result;
        }

        /// <summary>
        /// Makes a real command executaion
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="elements"></param>
        /// <param name="entityProfile"></param>
        /// <param name="cancellationToken"></param>
        /// <typeparam name="TEntity"></typeparam>
        /// <returns></returns>
        public async Task<ICollection<TEntity>> InsertPortionAsync<TEntity>(
            [NotNull] NpgsqlConnection connection,
            [NotNull] ICollection<TEntity> elements,
            [NotNull] EntityProfile entityProfile,
            CancellationToken cancellationToken)
            where TEntity : class
        {
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            var (commandText, parameters) = _insertCommandBuilder.Generate(elements, entityProfile, cancellationToken);
            using (var command = new NpgsqlCommand(commandText, connection))
            {
                foreach (var param in parameters)
                {
                    command.Parameters.Add(param);
                }

                var transaction = connection.BeginTransaction();
                try
                {
                    using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                    using (var elementsEnumerator = elements.GetEnumerator())
                    {
                        while (await reader.ReadAsync(cancellationToken))
                        {
                            if (!elementsEnumerator.MoveNext())
                            {
                                var message =
                                    $"There is no more items in the elements collection, but reader still has tuples to read.elements.{nameof(elements.Count)}: {elements.Count}";
                                _logger.LogError(message);
                                throw new SqlBulkServiceException(message);
                            }

                            await UpdateItemWithReturnedValuesAsync(reader, elementsEnumerator.Current, entityProfile.Properties, cancellationToken);
                        }

                        await reader.CloseAsync();
                    }

                    await transaction.CommitAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
            }

            return elements;
        }

        /// <summary>
        /// Updates item's property from returning clause 
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="item"></param>
        /// <param name="propertyProfiles"></param>
        /// <param name="cancellationToken"></param>
        /// <typeparam name="TEntity"></typeparam>
        /// <returns></returns>
        public async Task UpdateItemWithReturnedValuesAsync<TEntity>(
            NpgsqlDataReader reader,
            TEntity item,
            IDictionary<string, PropertyProfile> propertyProfiles,
            CancellationToken cancellationToken)
            where TEntity : class

        {
            foreach (var propInfoPair in propertyProfiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!propInfoPair.Value.IsUpdatedAfterInsert)
                    continue;
                var value = reader[propInfoPair.Key];
                propInfoPair.Value.SetPropertyValue(item, value);
            }
        }
    }
}