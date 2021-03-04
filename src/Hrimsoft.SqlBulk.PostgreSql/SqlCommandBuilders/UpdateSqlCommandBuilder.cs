using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Hrimsoft.SqlBulk.PostgreSql
{
    /// <summary>
    /// Generates bulk update sql command 
    /// </summary>
    public class UpdateSqlCommandBuilder : IUpdateSqlCommandBuilder
    {
        private readonly ILogger<UpdateSqlCommandBuilder> _logger;

        /// <summary> </summary>
        public UpdateSqlCommandBuilder(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<UpdateSqlCommandBuilder>();
        }

        /// <summary>
        /// Generates bulk update sql command
        /// </summary>
        /// <param name="elements">elements that have to be updated</param>
        /// <param name="entityProfile">elements type profile (contains mapping and other options)</param>
        /// <param name="cancellationToken"></param>
        /// <returns>Returns a text of an sql update command and collection of database parameters</returns>
        public IList<SqlCommandBuilderResult> Generate<TEntity>(ICollection<TEntity> elements, EntityProfile entityProfile,
                                                                CancellationToken    cancellationToken)
            where TEntity : class
        {
            if (elements == null)
                throw new ArgumentNullException(nameof(elements));
            if (entityProfile == null)
                throw new ArgumentNullException(nameof(entityProfile));

            if (elements.Count == 0)
                throw new ArgumentException($"There is no elements in the collection. At least one element must be.", nameof(elements));

            _logger.LogTrace($"Generating update sql for {elements.Count} elements.");

            var result             = "";
            var allItemsParameters = new List<NpgsqlParameter>();

            if (_logger.IsEnabled(LogLevel.Debug)) {
                _logger.LogDebug($"{nameof(TEntity)}: {typeof(TEntity).FullName}");
                _logger.LogDebug($"{nameof(elements)}.Count: {elements.Count}");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var isThereReturningClause = false;
            var allElementsAreNull     = true;
            using (var elementsEnumerator = elements.GetEnumerator()) {
                var thereIsMoreElements = true;
                // ignore all null items until find the first not null item
                while (elementsEnumerator.Current == null && thereIsMoreElements) {
                    thereIsMoreElements = elementsEnumerator.MoveNext();
                }
                if (thereIsMoreElements) {
                    allElementsAreNull = false;

                    var (commandForOneItem, itemParameters, hasReturningClause)
                        = GenerateForItem(entityProfile, elementsEnumerator.Current, null, 0);
                    isThereReturningClause = hasReturningClause;
                    allItemsParameters.AddRange(itemParameters);

                    var entireCommandLength = commandForOneItem.Length * elements.Count;

                    if (_logger.IsEnabled(LogLevel.Debug))
                        _logger.LogDebug($"entire command length: {entireCommandLength}");

                    var resultBuilder = new StringBuilder(entireCommandLength);
                    resultBuilder.AppendLine(commandForOneItem);
                    var elementIndex = 0;
                    while (elementsEnumerator.MoveNext()) {
                        // ignore all null items 
                        if (elementsEnumerator.Current == null)
                            continue;
                        elementIndex++;
                        (commandForOneItem, itemParameters, hasReturningClause) 
                            = GenerateForItem(entityProfile, elementsEnumerator.Current, resultBuilder, elementIndex);

                        allItemsParameters.AddRange(itemParameters);
                        resultBuilder.AppendLine(commandForOneItem);
                    }

                    result = resultBuilder.ToString();
                    if (_logger.IsEnabled(LogLevel.Debug))
                        _logger.LogDebug($"result command: {result}");
                }
            }
            if (allElementsAreNull)
                throw new ArgumentException("There is no elements in the collection. At least one element must be.", nameof(elements));

            return new List<SqlCommandBuilderResult>
                   {
                       new SqlCommandBuilderResult
                       {
                           Command                = result,
                           Parameters             = allItemsParameters,
                           IsThereReturningClause = isThereReturningClause
                       }
                   };
        }

        /// <summary>
        /// Generates update sql command for one item 
        /// </summary>
        /// <param name="entityProfile">elements type profile (contains mapping and other options)</param>
        /// <param name="item">an instance that has to be updated to the database</param>
        /// <param name="externalBuilder">Builder to which the generated for an item command will be appended</param>
        /// <param name="elementIndex">As this method is called for each item, this value will be added to the sql parameter name</param>
        /// <returns> Returns named tuple with generated command and list of db parameters. </returns>
        public (string Command, ICollection<NpgsqlParameter> Parameters, bool hasReturningClause) GenerateForItem<TEntity>(
            EntityProfile entityProfile, TEntity item, StringBuilder externalBuilder, int elementIndex)
            where TEntity : class
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));
            if (entityProfile == null)
                throw new ArgumentNullException(nameof(entityProfile));

            var commandBuilder = externalBuilder ?? new StringBuilder(192);
            commandBuilder.Append($"update {entityProfile.TableName} set ");

            var whereClause          = " where ";
            var returningClause      = " returning ";
            var parameters           = new List<NpgsqlParameter>();
            var firstSetExpression   = true;
            var firstWhereExpression = true;
            var firstReturningColumn = true;

            foreach (var propInfo in entityProfile.Properties.Values) {
                var paramName = $"@param_{propInfo.DbColumnName}_{elementIndex}";
                try {
                    if (propInfo.IsPrivateKey) {
                        var whereDelimiter = firstWhereExpression ? "" : ",";
                        whereClause += $"{whereDelimiter}\"{propInfo.DbColumnName}\"={paramName}";
                        parameters.Add(new NpgsqlParameter(paramName, propInfo.DbColumnType)
                        {
                            Value      = propInfo.GetPropertyValue(item) ?? DBNull.Value,
                            IsNullable = propInfo.IsNullable
                        });
                        firstWhereExpression = false;
                    }
                    if (propInfo.IsUpdatedAfterUpdate) {
                        var returningDelimiter = firstReturningColumn ? "" : ", ";
                        returningClause      += $"{returningDelimiter}\"{propInfo.DbColumnName}\"";
                        firstReturningColumn =  false;
                    }
                    if (propInfo.IsAutoGenerated)
                        continue;
                }
                catch (Exception ex) {
                    var message = $"an error occurred while calculating {paramName}";
                    throw new SqlGenerationException(SqlOperation.Update, message, ex);
                }
                try {
                    var setExpressionDelimiter = firstSetExpression ? "" : ",";
                    commandBuilder.Append($"{setExpressionDelimiter}\"{propInfo.DbColumnName}\"={paramName}");
                    parameters.Add(new NpgsqlParameter(paramName, propInfo.DbColumnType)
                    {
                        Value      = propInfo.GetPropertyValue(item) ?? DBNull.Value,
                        IsNullable = propInfo.IsNullable
                    });
                    firstSetExpression = false;
                }
                catch (Exception ex) {
                    var message = $"an error occurred while calculating {paramName}";
                    throw new SqlGenerationException(SqlOperation.Update, message, ex);
                }
            }

            if (firstWhereExpression)
                throw new SqlGenerationException(SqlOperation.Update, $"There is no private key defined for the entity type: '{typeof(TEntity).FullName}'");

            commandBuilder.Append(whereClause);

            if (!firstReturningColumn)
                commandBuilder.Append(returningClause);
            commandBuilder.Append(";");

            var command = externalBuilder == null
                ? commandBuilder.ToString()
                : ""; // In order to allow append next elements externalBuilder must not be flashed to string.

            if (_logger.IsEnabled(LogLevel.Debug)) {
                _logger.LogDebug($"command: {command}");
                _logger.LogDebug($"returningClause: {returningClause}");
            }

            return (command, parameters, !firstReturningColumn);
        }
    }
}