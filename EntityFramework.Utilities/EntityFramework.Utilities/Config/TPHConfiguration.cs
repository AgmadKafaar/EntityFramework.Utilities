using System;
using System.Collections.Generic;

namespace EntityFramework.Utilities.Config
{
    public class TphConfiguration
    {
        public Dictionary<Type, string> Mappings { get; set; }
        public string ColumnName { get; set; }
    }
}