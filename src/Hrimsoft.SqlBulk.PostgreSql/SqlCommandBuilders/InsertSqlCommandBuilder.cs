using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

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
        public IList<SqlCommandBuilderResult> Generate<TEntity>(ICollection<TEntity> elements, EntityProfile entityProfile, CancellationToken cancellationToken)
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
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug($"{nameof(TEntity)}: {typeof(TEntity).FullName}");
                _logger.LogDebug($"{nameof(elements)}.Count: {elements.Count}");
            }

            var (columns, returningClause) = this.GenerateColumnsAndReturningClauses(entityProfile.Properties.Values);
            var hasReturningClause = !string.IsNullOrWhiteSpace(returningClause);
            cancellationToken.ThrowIfCancellationRequested();

            const int MAX_PARAMS_PER_CMD = 65_535;

            var commandHeader           = $"insert into {entityProfile.TableName} ({columns}) values ";
            var approximateValuesLength = columns.Length * elements.Count;
            var paramsCount             = elements.Count * entityProfile.Properties.Count;
            var commandParameters       = new List<NpgsqlParameter>(Math.Min(paramsCount, MAX_PARAMS_PER_CMD));
            var resultBuilder = new StringBuilder(commandHeader.Length
                                                + approximateValuesLength
                                                + (returningClause?.Length ?? 0));
            resultBuilder.Append(commandHeader);

            var paramsPerElement = 0;
            var elementIndex     = -1;
            using (var elementsEnumerator = elements.GetEnumerator())
            {
                while (elementsEnumerator.MoveNext())
                {
                    var item = elementsEnumerator.Current;
                    if (item == null)
                        continue;
                    elementIndex++;
                    paramsPerElement = 0;
                    cancellationToken.ThrowIfCancellationRequested();
                    resultBuilder.Append('(');
                    var firstPropertyValue = true;
                    foreach (var propInfo in entityProfile.Properties.Values)
                    {
                        if (propInfo.IsAutoGenerated)
                            continue;
                        var delimiter = firstPropertyValue ? "" : ", ";
                        resultBuilder.Append(delimiter);
                        var propValue = propInfo.GetPropertyValueAsString(item);
                        if (propValue == null)
                        {
                            var paramName = $"@param_{propInfo.DbColumnName}_{elementIndex}";
                            try
                            {
                                // It was impossible to calculate property value without compile and dynamic invoke property expression
                                // so let's compile it)
                                commandParameters.Add(new NpgsqlParameter(paramName, propInfo.DbColumnType)
                                                      {
                                                          Value      = propInfo.GetPropertyValue(item) ?? DBNull.Value,
                                                          IsNullable = propInfo.IsNullable
                                                      });
                                resultBuilder.Append(paramName);
                                paramsPerElement++;
                            }
                            catch (Exception ex)
                            {
                                var message = $"an error occurred while calculating {paramName}";
                                throw new SqlGenerationException(SqlOperation.Insert, message, ex);
                            }
                        }
                        else
                        {
                            resultBuilder.Append(propValue);
                        }
                        firstPropertyValue = false;
                    }
                    resultBuilder.Append(')');
                    if (commandParameters.Count + paramsPerElement > MAX_PARAMS_PER_CMD)
                    {
                        if (hasReturningClause)
                        {
                            resultBuilder.Append(" returning ");
                            resultBuilder.Append(returningClause);
                        }
                        resultBuilder.Append(";");
                        var fullCommand = new SqlCommandBuilderResult
                                          {
                                              Command                = resultBuilder.ToString(),
                                              Parameters             = commandParameters,
                                              IsThereReturningClause = hasReturningClause
                                          };
                        if (_logger.IsEnabled(LogLevel.Debug))
                            _logger.LogDebug($"result command: {fullCommand}");
                        result.Add(fullCommand);

                        var remainingParams = (elements.Count - elementIndex - 1) * paramsPerElement;
                        commandParameters = new List<NpgsqlParameter>(Math.Min(remainingParams, MAX_PARAMS_PER_CMD));
                        resultBuilder.Clear();
                        resultBuilder.Append(commandHeader);
                    }
                    else
                    {
                        //  Finished with properties 
                        if (elements.Count > 1 && elementIndex < elements.Count - 1)
                            resultBuilder.Append(", ");   
                    }
                }
            }
            if (elementIndex == -1)
                throw new ArgumentException("There is no elements in the collection. At least one element must be.",
                                            nameof(elements));
            if (hasReturningClause)
            {
                resultBuilder.Append(" returning ");
                resultBuilder.Append(returningClause);
            }
            resultBuilder.Append(";");
            var lastFullCommand = new SqlCommandBuilderResult
                                  {
                                      Command                = resultBuilder.ToString(),
                                      Parameters             = commandParameters,
                                      IsThereReturningClause = hasReturningClause
                                  };
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug($"result command: {lastFullCommand}");
            result.Add(lastFullCommand);
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
            foreach (var propInfo in properties)
            {
                if (propInfo.IsUpdatedAfterInsert)
                {
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
                columns += $"{delimiter}\"{propInfo.DbColumnName}\"";

                firstColumn = false;
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug($"columns: {columns}");
                _logger.LogDebug($"returningClause: {returningClause}");
            }

            return (columns, returningClause);
        }
    }
}