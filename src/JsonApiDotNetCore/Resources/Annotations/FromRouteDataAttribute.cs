using System;

namespace JsonApiDotNetCore.Resources.Annotations
{
    /// <summary>
    /// Attr value comes from aspnet RouteData rather than from the HTTP JSON body
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class FromRouteDataAttribute : Attribute
    {
        /// <summary>
        ///     When deserialising data into this attribute look for route data with this key name. If empty uses the PublicName.
        /// </summary>
        public string RouteDataKey
        {
            get; set;
        }
    }
}
