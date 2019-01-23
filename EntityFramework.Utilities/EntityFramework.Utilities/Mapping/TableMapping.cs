using System.Collections.Generic;
using EntityFramework.Utilities.Config;

namespace EntityFramework.Utilities.Mapping
{
    /// <summary>
    /// Represents the mapping of an entity to a table in the database
    /// </summary>
    public class TableMapping
    {
        /// <summary>
        /// The name of the table the entity is mapped to
        /// </summary>
        public string TableName { get; set; }

        /// <summary>
        /// The schema of the table the entity is mapped to
        /// </summary>
        public string Schema { get; set; }

        /// <summary>
        /// Details of the property-to-column mapping
        /// </summary>
        public List<PropertyMapping> PropertyMappings { get; set; }

        /// <summary>
        /// Null if not TPH
        /// </summary>
        public TphConfiguration TphConfiguration { get; set; }
    }
}