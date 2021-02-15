using System.Collections.Generic;
using System.Net.Http;
using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Middleware;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;
using Microsoft.AspNetCore.Http;

namespace JsonApiDotNetCore.Serialization
{
    public class RouteDataAssigningRequestDeserialiser : RequestDeserializer
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ITargetedFields _targetedFields;

        public RouteDataAssigningRequestDeserialiser(IResourceContextProvider resourceContextProvider,
            IResourceFactory resourceFactory, ITargetedFields targetedFields, IHttpContextAccessor httpContextAccessor,
            IJsonApiRequest request) : base(resourceContextProvider, resourceFactory, targetedFields,
            httpContextAccessor, request)
        {
            _targetedFields = targetedFields;
            _httpContextAccessor = httpContextAccessor;
        }

        protected override IIdentifiable SetAttributes(IIdentifiable resource,
            IDictionary<string, object> attributeValues, IReadOnlyCollection<AttrAttribute> attributes)
        {
            var result = base.SetAttributes(resource, attributeValues, attributes);
            if (_httpContextAccessor.HttpContext.Request.Method == HttpMethod.Post.Method)
            {
                foreach (var attr in attributes)
                {
                    if (attr is RouteDataAttrAttribute routeAttr &&
                        _httpContextAccessor.HttpContext.Request.RouteValues.TryGetValue(routeAttr.RouteDataKey,
                            out var routeValue))
                    {
                        attr.SetValue(resource, routeValue);
                        _targetedFields.Attributes.Add(attr);
                    }
                }
            }

            return result;
        }
    }
}
