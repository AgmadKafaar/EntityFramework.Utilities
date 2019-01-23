namespace EntityFramework.Utilities.Reflection
{
    /// <summary>
    /// Generic interface for method info adapters
    /// </summary>
    /// <typeparam name="TThis">Class type</typeparam>
    public interface IPropertyCallAdapter<in TThis>
    {
        /// <summary>
        /// Method info for getter
        /// </summary>
        /// <param name="this"></param>
        /// <returns></returns>
        object InvokeGet(TThis @this);

        /// <summary>
        /// Method info for setter
        /// </summary>
        /// <param name="this"></param>
        /// <param name="value"></param>
        void InvokeSet(TThis @this, object value);
    }
}
