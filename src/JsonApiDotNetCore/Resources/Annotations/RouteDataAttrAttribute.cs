namespace JsonApiDotNetCore.Resources.Annotations
{
    /// <summary>
    /// Attr value comes from aspnet RouteData rather than from the HTTP JSON body
    /// </summary>
    public class RouteDataAttrAttribute : AttrAttribute
    {
        private string _routeDataKey;

        /// <summary>
        ///     When deserialising data into this attribute look for route data with this key name. If empty uses the PublicName.
        /// </summary>
        public string RouteDataKey
        {
            get => _routeDataKey ?? PublicName;
            set => _routeDataKey = value;
        }
    }
}
