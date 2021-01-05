using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using Humanizer;
using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Errors;
using JsonApiDotNetCore.Middleware;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;
using JsonApiDotNetCore.Serialization.Objects;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;

namespace JsonApiDotNetCore.Serialization
{
    /// <summary>
    /// Server deserializer implementation of the <see cref="BaseDeserializer"/>.
    /// </summary>
    public class RequestDeserializer : BaseDeserializer, IJsonApiDeserializer
    {
        private readonly ITargetedFields  _targetedFields;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IJsonApiRequest _request;

        protected override bool AllowLocalIds => _request.Kind == EndpointKind.AtomicOperations;

        public RequestDeserializer(
            IResourceContextProvider resourceContextProvider,
            IResourceFactory resourceFactory,
            ITargetedFields targetedFields,
            IHttpContextAccessor httpContextAccessor,
            IJsonApiRequest request) 
            : base(resourceContextProvider, resourceFactory)
        {
            _targetedFields = targetedFields ?? throw new ArgumentNullException(nameof(targetedFields));
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
            _request = request ?? throw new ArgumentNullException(nameof(request));
        }

        /// <inheritdoc />
        public object DeserializeDocument(string body)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));

            if (_request.Kind == EndpointKind.Relationship)
            {
                _targetedFields.Relationships.Add(_request.Relationship);
            }

            if (_request.Kind == EndpointKind.AtomicOperations)
            {
                return DeserializeOperationsDocument(body);
            }

            var instance = DeserializeBody(body);

            AssertResourceIdIsNotTargeted();
            
            return instance;
        }

        private object DeserializeOperationsDocument(string body)
        {
            JToken bodyToken = LoadJToken(body);
            var document = bodyToken.ToObject<AtomicOperationsDocument>();

            var operations = new List<OperationContainer>();

            if (document?.Operations == null || !document.Operations.Any())
            {
                throw new JsonApiException(new Error(HttpStatusCode.BadRequest)
                {
                    Title = "Failed to deserialize operations request."
                });
            }

            int index = 0;
            foreach (var operation in document.Operations)
            {
                var container = DeserializeOperation(operation, index);
                operations.Add(container);

                index++;
            }

            return operations;
        }

        // TODO: Cleanup code.

        private OperationContainer DeserializeOperation(AtomicOperationObject operation, int index)
        {
            if (operation.Href != null)
            {
                throw new JsonApiSerializationException("Usage of the 'href' element is not supported.", null,
                    atomicOperationIndex: index);
            }

            if (operation.Code == AtomicOperationCode.Remove)
            {
                if (operation.Ref == null)
                {
                    throw new JsonApiSerializationException("The 'ref' element is required.", null,
                        atomicOperationIndex: index);
                }
            }

            if (operation.Ref != null)
            {
                if (operation.Code == AtomicOperationCode.Add && operation.Ref.Relationship == null)
                {
                    throw new JsonApiSerializationException("The 'ref.relationship' element is required.", null,
                        atomicOperationIndex: index);
                }

                if (operation.Ref.Type == null)
                {
                    throw new JsonApiSerializationException("The 'ref.type' element is required.", null,
                        atomicOperationIndex: index);
                }

                var resourceContext = GetExistingResourceContext(operation.Ref.Type, index);
                
                if ((operation.Ref.Id == null && operation.Ref.Lid == null) || (operation.Ref.Id != null && operation.Ref.Lid != null))
                {
                    throw new JsonApiSerializationException("The 'ref.id' or 'ref.lid' element is required.", null,
                        atomicOperationIndex: index);
                }

                if (operation.Ref.Id != null)
                {
                    try
                    {
                        TypeHelper.ConvertType(operation.Ref.Id, resourceContext.IdentityType);
                    }
                    catch (FormatException exception)
                    {
                        throw new JsonApiSerializationException(null, exception.Message, null, index);
                    }
                }

                if (operation.Ref.Relationship != null)
                {
                    var relationship = resourceContext.Relationships.FirstOrDefault(r => r.PublicName == operation.Ref.Relationship);
                    if (relationship == null)
                    {
                        throw new JsonApiSerializationException(
                            "The referenced relationship does not exist.",
                            $"Resource of type '{operation.Ref.Type}' does not contain a relationship named '{operation.Ref.Relationship}'.",
                            atomicOperationIndex: index);
                    }

                    if (operation.Code != AtomicOperationCode.Update && relationship is HasOneAttribute)
                    {
                        throw new JsonApiSerializationException(
                            $"Only to-many relationships can be targeted in '{operation.Code.ToString().Camelize()}' operations.",
                            $"Relationship '{operation.Ref.Relationship}' must be a to-many relationship.",
                            atomicOperationIndex: index);
                    }

                    if (relationship is HasOneAttribute && operation.ManyData != null)
                    {
                        throw new JsonApiSerializationException(
                            "Expected single data element for to-one relationship.",
                            $"Expected single data element for '{relationship.PublicName}' relationship.",
                            atomicOperationIndex: index);
                    }

                    if (relationship is HasManyAttribute && operation.ManyData == null)
                    {
                        throw new JsonApiSerializationException(
                            "Expected data[] element for to-many relationship.",
                            $"Expected data[] element for '{relationship.PublicName}' relationship.",
                            atomicOperationIndex: index);
                    }

                    if (operation.ManyData != null)
                    {
                        foreach (var resourceObject in operation.ManyData)
                        {
                            if (resourceObject.Type == null)
                            {
                                throw new JsonApiSerializationException("The 'data[].type' element is required.", null,
                                    atomicOperationIndex: index);
                            }

                            if ((resourceObject.Id == null && resourceObject.Lid == null) || (resourceObject.Id != null && resourceObject.Lid != null))
                            {
                                throw new JsonApiSerializationException("The 'data[].id' or 'data[].lid' element is required.", null,
                                    atomicOperationIndex: index);
                            }

                            var rightResourceContext = GetExistingResourceContext(resourceObject.Type, index);
                            if (!rightResourceContext.ResourceType.IsAssignableFrom(relationship.RightType))
                            {
                                var relationshipRightTypeName = ResourceContextProvider.GetResourceContext(relationship.RightType);
                            
                                throw new JsonApiSerializationException("Resource type mismatch between 'ref.relationship' and 'data[].type' element.", 
                                    $@"Expected resource of type '{relationshipRightTypeName}' in 'data[].type', instead of '{rightResourceContext.PublicName}'.",
                                    atomicOperationIndex: index);
                            }
                        }
                    }

                    if (operation.SingleData != null)
                    {
                        var resourceObject = operation.SingleData;

                        if (resourceObject.Type == null)
                        {
                            throw new JsonApiSerializationException("The 'data.type' element is required.", null,
                                atomicOperationIndex: index);
                        }

                        if ((resourceObject.Id == null && resourceObject.Lid == null) || (resourceObject.Id != null && resourceObject.Lid != null))
                        {
                            throw new JsonApiSerializationException("The 'data.id' or 'data.lid' element is required.", null,
                                atomicOperationIndex: index);
                        }

                        var rightResourceContext = GetExistingResourceContext(resourceObject.Type, index);
                        if (!rightResourceContext.ResourceType.IsAssignableFrom(relationship.RightType))
                        {
                            var relationshipRightTypeName = ResourceContextProvider.GetResourceContext(relationship.RightType);
                            
                            throw new JsonApiSerializationException("Resource type mismatch between 'ref.relationship' and 'data.type' element.", 
                                $@"Expected resource of type '{relationshipRightTypeName}' in 'data.type', instead of '{rightResourceContext.PublicName}'.",
                                atomicOperationIndex: index);
                        }
                    }
                }
            }

            return ToOperationContainer(operation);
        }

        private OperationContainer ToOperationContainer(AtomicOperationObject operation)
        {
            var operationKind = GetOperationKind(operation);

            var resourceName = operation.GetResourceTypeName();
            var primaryResourceContext = ResourceContextProvider.GetResourceContext(resourceName);

            _targetedFields.Attributes.Clear();
            _targetedFields.Relationships.Clear();

            IIdentifiable resource;

            switch (operationKind)
            {
                case OperationKind.CreateResource:
                case OperationKind.UpdateResource:
                {
                    resource = ParseResourceObject(operation.SingleData);
                    break;
                }
                case OperationKind.DeleteResource:
                case OperationKind.SetRelationship:
                case OperationKind.AddToRelationship:
                case OperationKind.RemoveFromRelationship:
                {
                    resource = ResourceFactory.CreateInstance(primaryResourceContext.ResourceType);
                    resource.StringId = operation.Ref.Id;
                    resource.LocalId = operation.Ref.Lid;
                    break;
                }
                default:
                {
                    throw new NotSupportedException($"Unknown operation kind '{operationKind}'.");
                }
            }

            var request = new JsonApiRequest
            {
                Kind = EndpointKind.AtomicOperations,
                OperationKind = operationKind,
                BasePath = "TODO: Set this...",
                PrimaryResource = primaryResourceContext
            };

            if (operation.Ref != null)
            {
                request.PrimaryId = operation.Ref.Id;

                if (operation.Ref.Relationship != null)
                {
                    var relationship = primaryResourceContext.Relationships.SingleOrDefault(r => r.PublicName == operation.Ref.Relationship);
                    if (relationship == null)
                    {
                        throw new InvalidOperationException("TODO: @OPS: Relationship does not exist.");
                    }

                    var secondaryResourceContext = ResourceContextProvider.GetResourceContext(relationship.RightType);
                    if (secondaryResourceContext == null)
                    {
                        throw new InvalidOperationException("TODO: @OPS: Secondary resource type does not exist.");
                    }

                    request.SecondaryResource = secondaryResourceContext;
                    request.Relationship = relationship;
                    request.IsCollection = relationship is HasManyAttribute;

                    _targetedFields.Relationships.Add(relationship);

                    if (operation.SingleData != null)
                    {
                        var rightResource = ParseResourceObject(operation.SingleData);
                        relationship.SetValue(resource, rightResource);
                    }
                    else if (operation.ManyData != null)
                    {
                        var secondaryResources = operation.ManyData.Select(ParseResourceObject).ToArray();
                        var rightResources = TypeHelper.CopyToTypedCollection(secondaryResources, relationship.Property.PropertyType);
                        relationship.SetValue(resource, rightResources);
                    }
                }
            }

            var targetedFields = new TargetedFields
            {
                Attributes = _targetedFields.Attributes.ToHashSet(),
                Relationships = _targetedFields.Relationships.ToHashSet()
            };

            return new OperationContainer(operationKind, resource, targetedFields, request);
        }

        private static OperationKind GetOperationKind(AtomicOperationObject operation)
        {
            switch (operation.Code)
            {
                case AtomicOperationCode.Add:
                {
                    return operation.Ref != null ? OperationKind.AddToRelationship : OperationKind.CreateResource;
                }
                case AtomicOperationCode.Update:
                {
                    return operation.Ref?.Relationship != null ? OperationKind.SetRelationship : OperationKind.UpdateResource;
                }
                case AtomicOperationCode.Remove:
                {
                    return operation.Ref.Relationship != null ? OperationKind.RemoveFromRelationship : OperationKind.DeleteResource;
                }
                default:
                {
                    throw new NotSupportedException($"Unknown operation code '{operation.Code}'.");
                }
            }
        }

        private void AssertResourceIdIsNotTargeted()
        {
            if (!_request.IsReadOnly && _targetedFields.Attributes.Any(attribute => attribute.Property.Name == nameof(Identifiable.Id)))
            {
                throw new JsonApiSerializationException("Resource ID is read-only.", null);
            }
        }

        /// <summary>
        /// Additional processing required for server deserialization. Flags a
        /// processed attribute or relationship as updated using <see cref="ITargetedFields"/>.
        /// </summary>
        /// <param name="resource">The resource that was constructed from the document's body.</param>
        /// <param name="field">The metadata for the exposed field.</param>
        /// <param name="data">Relationship data for <paramref name="resource"/>. Is null when <paramref name="field"/> is not a <see cref="RelationshipAttribute"/>.</param>
        protected override void AfterProcessField(IIdentifiable resource, ResourceFieldAttribute field, RelationshipEntry data = null)
        {
            if (field is AttrAttribute attr)
            {
                if (_httpContextAccessor.HttpContext.Request.Method == HttpMethod.Post.Method &&
                    !attr.Capabilities.HasFlag(AttrCapabilities.AllowCreate))
                {
                    throw new JsonApiSerializationException(
                        "Setting the initial value of the requested attribute is not allowed.",
                        $"Setting the initial value of '{attr.PublicName}' is not allowed.");
                }

                if (_httpContextAccessor.HttpContext.Request.Method == HttpMethod.Patch.Method &&
                    !attr.Capabilities.HasFlag(AttrCapabilities.AllowChange))
                {
                    throw new JsonApiSerializationException(
                        "Changing the value of the requested attribute is not allowed.",
                        $"Changing the value of '{attr.PublicName}' is not allowed.");
                }

                _targetedFields.Attributes.Add(attr);
            }
            else if (field is RelationshipAttribute relationship)
                _targetedFields.Relationships.Add(relationship);
        }
    }
}
