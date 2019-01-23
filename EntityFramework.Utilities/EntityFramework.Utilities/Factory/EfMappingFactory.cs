using System;
using System.Collections.Generic;
using System.Data.Entity;
using EntityFramework.Utilities.Mapping;

namespace EntityFramework.Utilities.Factory
{
    public static class EfMappingFactory
    {
        private static readonly Dictionary<Type, EfMapping> Cache = new Dictionary<Type, EfMapping>();

        public static EfMapping GetMappingsForContext(DbContext context)
        {
            var type = context.GetType();
            EfMapping mapping;
            if (Cache.TryGetValue(type, out mapping)) return mapping;
            mapping = new EfMapping(context);
            Cache.Add(type, mapping);
            return mapping;
        }
    }
}