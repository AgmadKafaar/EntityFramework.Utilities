using System;

namespace EntityFramework.Utilities.Reflection
{
    /// <summary>
    /// Implementation to store setters and getters of properties for class type
    /// </summary>
    /// <typeparam name="TThis"></typeparam>
    /// <typeparam name="TResult"></typeparam>
    public class PropertyCallAdapter<TThis, TResult> : IPropertyCallAdapter<TThis>
    {
        private readonly Func<TThis, TResult> _getterInvocation;
        private readonly Action<TThis, object> _setterInvocation;

        public PropertyCallAdapter(Func<TThis, TResult> getterInvocation, Action<TThis, object> setterInvocation)
        {
            _getterInvocation = getterInvocation;
            _setterInvocation = setterInvocation;
        }

        /// <summary>
        /// Calls the getter function of a property
        /// </summary>
        /// <param name="this"></param>
        /// <returns></returns>
        public object InvokeGet(TThis @this) => _getterInvocation.Invoke(@this);

        /// <summary>
        /// Calls the setter function of property
        /// </summary>
        /// <param name="this"></param>
        /// <param name="value"></param>
        public void InvokeSet(TThis @this, object value)
        {
            _setterInvocation.Invoke(@this, value);
        }
    }
}