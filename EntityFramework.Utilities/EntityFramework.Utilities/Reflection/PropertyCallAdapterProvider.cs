using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace EntityFramework.Utilities.Reflection
{
    /// <summary>
    /// A provider class to the PropertyCallAdapter interface.
    /// This class stores the methodInfo for the getters and setters that we grab from reflection.
    /// This cache greatly speeds up the process.
    /// </summary>
    /// <typeparam name="TThis"></typeparam>
    public class PropertyCallAdapterProvider<TThis>
    {
        private static readonly Dictionary<string, IPropertyCallAdapter<TThis>> Instances = new Dictionary<string, IPropertyCallAdapter<TThis>>();

        /// <summary>
        /// Check if we have the method info for the property in cache, else generate it and store it in cache.
        /// </summary>
        /// <param name="forPropertyName"></param>
        /// <returns></returns>
        public static IPropertyCallAdapter<TThis> GetInstance(string forPropertyName)
        {
            IPropertyCallAdapter<TThis> instance;
            if (Instances.TryGetValue(forPropertyName, out instance)) return instance;

            // We actually do not need the getter property
            // But storing it here for completeness

            var parts = forPropertyName.Split('.');

            var property = typeof(TThis).GetProperty(parts[0], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
            MethodInfo getMethod;
            Delegate getterInvocation;
            if (property != null && (getMethod = property.GetGetMethod(true)) != null)
            {
                var openGetterType = typeof(Func<,>);
                var concreteGetterType = openGetterType.MakeGenericType(typeof(TThis), property.PropertyType);

                getterInvocation = Delegate.CreateDelegate(concreteGetterType, null, getMethod);

                //if its a complex property
                var tempProperty = property;
                foreach (var source in parts.Skip(1))
                {
                    if (tempProperty == null) continue;
                    var prop = tempProperty.PropertyType.GetProperty(source);

                    if (prop != null)
                    {
                        getMethod = prop.GetGetMethod();
                        concreteGetterType = openGetterType.MakeGenericType(tempProperty.PropertyType, prop.PropertyType);
                        getterInvocation = Delegate.CreateDelegate(concreteGetterType, null, getMethod); 
                    }
                    tempProperty = prop;
                }
            }
            else
            {
                throw new NullReferenceException($"Property is null: {typeof(TThis)}->{forPropertyName}.");
            }

            MethodInfo setMethod;
            Action<TThis, object> setterInvocation;

            // The setter cannot be invoked from a null target
            // So we store the dynamicInvoke into cache
            if ((setMethod = property.GetSetMethod(true)) != null)
            {
                var openSetterType = typeof(Action<,>);
                var concreteSetterType = openSetterType.MakeGenericType(typeof(TThis), property.PropertyType);

                var setter = Delegate.CreateDelegate(concreteSetterType, null, setMethod);

                setterInvocation = (tThis, setValue) =>
                {
                    if (property.PropertyType == typeof(string))
                    {
                        setValue = setValue?.ToString().Replace(",", " ").Replace("\r\n", " ");
                    }

                    var type = setValue?.GetType();
                    if (type == typeof(long) && (property.PropertyType == typeof(int) || property.PropertyType == typeof(int?)))
                    {
                        setter.DynamicInvoke(tThis, Convert.ToInt32(setValue));
                    }
                    else
                    {
                        setter.DynamicInvoke(tThis, setValue);
                    }
                };
            }
            else
            {
                throw new NullReferenceException($"Property is null: {typeof(TThis)}->{forPropertyName}.");
            }

            var openAdapterType = typeof(PropertyCallAdapter<,>);

            var concreteAdapterType = openAdapterType.MakeGenericType(typeof(TThis), property.PropertyType);

            // create a new instance of PropertyCallAdapter and add it to cache.
            instance = Activator.CreateInstance(concreteAdapterType, getterInvocation, setterInvocation) as IPropertyCallAdapter<TThis>;

            Instances.Add(forPropertyName, instance);

            return instance;
        }
    }
}