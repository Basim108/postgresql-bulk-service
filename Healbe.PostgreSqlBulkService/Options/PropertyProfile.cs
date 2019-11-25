using System;
using System.Linq.Expressions;
using JetBrains.Annotations;

namespace Hrimsoft.PostgresSqlBulkService
{
    /// <summary>
    /// A configuration of an entity's property mapping
    /// </summary>
    public class PropertyProfile
    {
        /// <inheritdoc />
        public PropertyProfile(string column, [NotNull] MemberExpression memberExpression)
        {
            if (string.IsNullOrWhiteSpace(column))
                throw new ArgumentNullException(nameof(column));
            
            this.DbColumnName = column;
            this.PropertyExpresion = memberExpression;
        }
        
        /// <summary>
        /// If true then the value of the column that represents this property will be included after insert
        /// </summary>
        public bool IncludeInReturning { get; private set; }

        /// <summary>
        /// If true this property is a private key 
        /// </summary>
        public bool IsAutoGeneratedKey { get; private set; }
        
        /// <summary>
        /// Expression to access a property 
        /// </summary>
        public MemberExpression PropertyExpresion { get; }

        /// <summary>
        /// Column name in database table 
        /// </summary>
        public string DbColumnName { get; }
        
        /// <summary>
        /// When applied, the value of the column that represents this property will be included after insert
        /// </summary>
        public void ThatIsPartOfKey()
        {
            this.IncludeInReturning = true;
        }
        
        /// <summary>
        /// When applied, the value of a new generated private key  that represents this property will be updated after insert
        /// </summary>
        public void ThatIsAutoGeneratedPrivateKey()
        {
            this.IsAutoGeneratedKey = true;
            this.IncludeInReturning = true;
        }
    }
}