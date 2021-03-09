using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Hrimsoft.SqlBulk.PostgreSql
{
    /// <summary>
    /// Sql bulk inset command generator 
    /// </summary>
    public class InsertSqlCommandBuilder : IInsertSqlCommandBuilder
    {
        private readonly ILogger<InsertSqlCommandBuilder> _logger;

        /// <summary> </summary>
        public InsertSqlCommandBuilder(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<InsertSqlCommandBuilder>();
        }

        /// <summary>
        /// Generates sql inset command for bunch of elements
        /// </summary>
        /// <param name="elements">elements that have to be inserted into the table</param>
        /// <param name="entityProfile">elements type profile (contains mapping and other options)</param>
        /// <param name="cancellationToken"></param>
        /// <returns>Returns a text of an sql inset command and collection of database parameters</returns>
        public IList<SqlCommandBuilderResult> Generate<TEntity>(
            ICollection<TEntity> elements,
            EntityProfile        entityProfile,
            CancellationToken    cancellationToken)
            where TEntity : class
        {
            if (elements == null)
                throw new ArgumentNullException(nameof(elements));
            if (entityProfile == null)
                throw new ArgumentNullException(nameof(entityProfile));

            if (elements.Count == 0)
                throw new ArgumentException("There is no elements in the collection. At least one element must be.", nameof(elements));

            _logger.LogTrace($"Generating insert sql for {elements.Count} elements.");

            var result = new List<SqlCommandBuilderResult>();
            if (_logger.IsEnabled(LogLevel.Debug)) {
                _logger.LogDebug($"{nameof(TEntity)}: {typeof(TEntity).FullName}");
                _logger.LogDebug($"{nameof(elements)}.Count: {elements.Count}");
            }

            var (columns, returningClause) = this.GenerateColumnsAndReturningClauses(entityProfile.Properties.Values);
            var hasReturningClause = !string.IsNullOrWhiteSpace(returningClause);
            cancellationToken.ThrowIfCancellationRequested();

            const int MAX_PARAMS_PER_CMD = 65_535;

            var commandHeader     = $"insert into {entityProfile.TableName} ({columns}) values ";
            var valueClauseLength = columns.Length * elements.Count;
            var paramsCount       = elements.Count * entityProfile.MaxPossibleSqlParameters;
            var sqlParameters     = new List<NpgsqlParameter>(Math.Min(paramsCount, MAX_PARAMS_PER_CMD));
            var commandBuilder = new StringBuilder(commandHeader.Length
                                                 + valueClauseLength
                                                 + (returningClause?.Length ?? 0));
            commandBuilder.Append(commandHeader);
            var elementIndex    = -1;
            var elementAbsIndex = -1;
            using (var elementsEnumerator = elements.GetEnumerator()) {
                while (elementsEnumerator.MoveNext()) {
                    elementAbsIndex++;
                    var item = elementsEnumerator.Current;
                    if (item == null)
                        continue;
                    elementIndex++;
                    cancellationToken.ThrowIfCancellationRequested();
                    commandBuilder.Append('(');
                    var firstPropertyValue = true;
                    foreach (var pair in entityProfile.Properties) {
                        try {
                            var propInfo = pair.Value;
                            if (propInfo.IsAutoGenerated)
                                continue;
                            var delimiter = firstPropertyValue ? "" : ", ";
                            commandBuilder.Append(delimiter);
                            if (propInfo.IsDynamicallyInvoked()) {
                                var paramName = $"@param_{propInfo.DbColumnName}_{elementIndex}";

                                var value = propInfo.GetPropertyValue(item);
                                if (value == null) {
                                    // as count of parameters are limited, it's better to save params for non null values
                                    commandBuilder.Append("null");
                                }
                                else {
                                    sqlParameters.Add(new NpgsqlParameter(paramName, propInfo.DbColumnType)
                                                      {
                                                          Value = value
                                                      });
                                    commandBuilder.Append(paramName);
                                }
                            }
                            else {
                                var value = propInfo.GetPropertyValueAsString(item);
                                commandBuilder.Append(value);
                            }
                            firstPropertyValue = false;
                        }
                        catch (Exception ex) {
                            var message = $"an error occurred while processing a property {pair.Key} of entity {entityProfile.EntityType.Namespace} entity, item idx: {elementAbsIndex}";
                            throw new SqlGenerationException(SqlOperation.Insert, message, ex);
                        }
                    }
                    commandBuilder.Append(')');
                    if (sqlParameters.Count + entityProfile.MaxPossibleSqlParameters > MAX_PARAMS_PER_CMD) {
                        if (hasReturningClause) {
                            commandBuilder.Append(" returning ");
                            commandBuilder.Append(returningClause);
                        }
                        commandBuilder.Append(";");
                        if (_logger.IsEnabled(LogLevel.Information)) {
                            var (cmdSize, suffix) = ((long) commandBuilder.Length * 2).PrettifySize();
                            _logger.LogInformation($"Generated sql insert command for {elementIndex + 1} {entityProfile.EntityType.Name} elements, command size {cmdSize:F2} {suffix}");
                        }
                        result.Add(new SqlCommandBuilderResult
                                   (
                                       commandBuilder.ToString(),
                                       sqlParameters,
                                       isThereReturningClause: hasReturningClause,
                                       elementsCount: elementIndex
                                   ));
                        sqlParameters = new List<NpgsqlParameter>(sqlParameters.Count);
                        commandBuilder.Clear();
                        commandBuilder.Append(commandHeader);
                        elementIndex = -1;
                    }
                    else {
                        //  Finished with properties 
                        if (elements.Count > 1 && elementAbsIndex < elements.Count - 1)
                            commandBuilder.Append(", ");
                    }
                }
            }
            if (elementIndex == -1 && result.Count == 0)
                throw new ArgumentException("There is no elements in the collection. At least one element must be.",
                                            nameof(elements));
            if (hasReturningClause) {
                commandBuilder.Append(" returning ");
                commandBuilder.Append(returningClause);
            }
            commandBuilder.Append(";");
            result.Add(new SqlCommandBuilderResult
                       (
                           commandBuilder.ToString(),
                           sqlParameters,
                           isThereReturningClause: hasReturningClause,
                           elementsCount: elementIndex + 1
                       ));
            return result;
        }

        /// <summary>
        /// In one pass generates both columns and returning clauses 
        /// </summary>
        /// <param name="properties">Information about entity properties</param>
        /// <returns>
        /// Returns named tuple with generated columns and returning clauses.
        /// If there is no properties that has to be included into returning clause then ReturningClause item in the result tuple will be an empty string.
        /// </returns>
        // ReSharper disable once MemberCanBePrivate.Global  Needed to be public for unit testing purpose
        public (string Columns, string ReturningClause) GenerateColumnsAndReturningClauses(ICollection<PropertyProfile> properties)
        {
            if (properties == null)
                throw new ArgumentNullException(nameof(properties));

            var returningClause      = "";
            var firstReturningColumn = true;

            var columns     = "";
            var firstColumn = true;
            foreach (var propInfo in properties) {
                if (propInfo.IsUpdatedAfterInsert) {
                    var returningDelimiter = firstReturningColumn
                        ? ""
                        : ", ";
                    returningClause      += $"{returningDelimiter}\"{propInfo.DbColumnName}\"";
                    firstReturningColumn =  false;
                }

                if (propInfo.IsAutoGenerated)
                    continue;

                var delimiter = firstColumn
                    ? ""
                    : ", ";
                columns     += $"{delimiter}\"{propInfo.DbColumnName}\"";
                firstColumn =  false;
            }

            if (_logger.IsEnabled(LogLevel.Debug)) {
                _logger.LogDebug($"columns: {columns}");
                _logger.LogDebug($"returningClause: {returningClause}");
            }

            return (columns, returningClause);
        }
    }
}