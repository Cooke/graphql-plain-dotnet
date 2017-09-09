using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Cooke.GraphQL.Annotations;
using Cooke.GraphQL.Types;
using Tests;

namespace Cooke.GraphQL
{
    public delegate GraphFieldInfo FieldEnhancer(GraphFieldInfo fieldInfo);

    public class SchemaBuilderOptions
    {
        public IFieldNamingStrategy FieldNamingStrategy { get; set; } = new CamelCaseFieldNamingStrategy();
    }

    public class SchemaBuilder
    {
        private readonly SchemaBuilderOptions _options;
        private readonly IList<FieldEnhancer> _fieldEnhancers = new List<FieldEnhancer>();
        private readonly Dictionary<TypeCacheKey, GraphType> _typeCache = new Dictionary<TypeCacheKey, GraphType>();
        private Type _queryType;
        private Type _mutationType;

        public SchemaBuilder(SchemaBuilderOptions options)
        {
            _options = options;

            // Default attrubte metadata enhancer
            this.UseAttributeMetadata();
        }

        public SchemaBuilder() : this(new SchemaBuilderOptions())
        {
        }

        public SchemaBuilder UseQuery<T>()
        {
            _queryType = typeof(T);
            return this;
        }

        public SchemaBuilder UseMutation<T>()
        {
            _mutationType = typeof(T);
            return this;
        }

        public SchemaBuilder UseFieldEnhancer(FieldEnhancer fieldEnhancer)
        {
            _fieldEnhancers.Add(fieldEnhancer);
            return this;
        }

        public Schema Build()
        {
            var graphQueryType = CreateType(_queryType);
            var graphMutationType = _mutationType != null ? CreateType(_mutationType) : null;
            return new Schema((ObjectGraphType) graphQueryType, (ObjectGraphType)graphMutationType, _typeCache.Values);
        }

        // TODO change to data driven type creation and add possibility to register custom type factories
        private GraphType CreateType(Type clrType, bool withinInputType = false)
        {
            clrType = TypeHelper.UnwrapTask(clrType);

            var typeCacheKey = new TypeCacheKey { ClrType = clrType, IsInput = withinInputType && !TypeHelper.IsList(clrType) && clrType.GetTypeInfo().IsClass };
            if (_typeCache.ContainsKey(typeCacheKey))
            {
                return _typeCache[typeCacheKey];
            }

            if (clrType == typeof(bool))
            {
                _typeCache[typeCacheKey] = BooleanGraphtype.Instance;
                return StringGraphType.Instance;
            }

            if (clrType == typeof(string))
            {
                _typeCache[typeCacheKey] = StringGraphType.Instance;
                return StringGraphType.Instance;
            }

            if (clrType == typeof(int))
            {
                _typeCache[typeCacheKey] = IntGraphType.Instance;
                return IntGraphType.Instance;
            }

            if (clrType.GetTypeInfo().IsEnum)
            {
                var enumType = new EnumGraphType(Enum.GetNames(clrType).Select(x => new EnumValue(x)), clrType);
                _typeCache[typeCacheKey] = enumType;
                return enumType;
            }

            if (TypeHelper.IsList(clrType))
            {
                var listEnumerableType = clrType.GetTypeInfo().ImplementedInterfaces.Concat(new[] { clrType })
                    .First(x => x.IsConstructedGenericType && x.GetGenericTypeDefinition() == typeof(IEnumerable<>));
                var listGraphType = new ListGraphType();
                _typeCache[typeCacheKey] = listGraphType;
                listGraphType.ItemType = CreateType(listEnumerableType.GenericTypeArguments.Single(), withinInputType);
                return listGraphType;
            }

            if (clrType.GetTypeInfo().IsClass)
            {
                var baseType = clrType.GetTypeInfo().BaseType;
                if (baseType != typeof(object))
                {
                    CreateType(baseType, withinInputType);
                }

                if (!withinInputType)
                {
                    GraphType resultGraphType;
                    if (clrType.GetTypeInfo().IsAbstract)
                    {
                        var interfaceGraphType = new InterfaceGraphType(clrType);
                        _typeCache[typeCacheKey] = interfaceGraphType;
                        interfaceGraphType.Fields = CreateFields(clrType);
                        resultGraphType = interfaceGraphType;
                    }
                    else
                    {
                        var objectGraphType = new ObjectGraphType(clrType);
                        _typeCache[typeCacheKey] = objectGraphType;
                        objectGraphType.Fields = CreateFields(clrType);
                        resultGraphType = objectGraphType;
                    }
                    

                    foreach (var subType in clrType.GetTypeInfo().Assembly.DefinedTypes.Where(x => x.BaseType == clrType))
                    {
                        CreateType(subType.AsType());
                    }

                    return resultGraphType;
                }
                else
                {
                    var inputObjectGraphType = new InputObjectGraphType(clrType);
                    _typeCache[typeCacheKey] = inputObjectGraphType;
                    inputObjectGraphType.Fields = CreateInputFields(clrType);
                    return inputObjectGraphType;
                }
            }

            throw new NotSupportedException($"The given CLR type '{clrType}' is currently not supported.");
        }

        private Dictionary<string, GraphFieldInfo> CreateFields(Type clrType)
        {
            var propertyInfos = clrType.GetRuntimeProperties().Where(x => x.GetMethod.IsPublic && !x.GetMethod.IsStatic);
            var fields = propertyInfos.Select(x => _fieldEnhancers.Aggregate(CreateFieldInfo(x), (f, e) => e(f)));

            var methodInfos = clrType.GetTypeInfo().DeclaredMethods.Where(x => x.IsPublic && !x.IsSpecialName && !x.IsStatic);
            fields = fields.Concat(methodInfos.Select(x => _fieldEnhancers.Aggregate(CreateFieldInfo(x, null), (f, e) => e(f))));
            return fields.ToDictionary(x => x.Name);
        }

        private Dictionary<string, GraphInputFieldInfo> CreateInputFields(Type clrType)
        {
            var propertyInfos = clrType.GetTypeInfo().DeclaredProperties.Where(x => x.GetMethod.IsPublic && x.SetMethod.IsPublic && !x.GetMethod.IsStatic);
            // TODO replace reflection set property with expression
            var fields = propertyInfos.Select(x => new GraphInputFieldInfo(_options.FieldNamingStrategy.ResolveFieldName(x), CreateType(x.PropertyType, true), x.SetValue));
            return fields.ToDictionary(x => x.Name);
        }

        private GraphFieldInfo CreateFieldInfo(PropertyInfo propertyInfo)
        {
            var type = CreateType(propertyInfo.PropertyType);
            FieldResolver resolver = async context =>
            {
                // TODO replace with a compiled expression that gets the property value
                var result = propertyInfo.GetValue(context.Instance);
                return await UnwrapResult(result);
            };

            if (propertyInfo.GetCustomAttribute<NotNull>() != null)
            {
                type = new NotNullGraphType {ItemType = type};
                var localResolver = resolver;
                resolver = async context =>
                {
                    var resolvedValue = await localResolver(context);
                    if (resolvedValue == null)
                    {
                        // TODO better exception
                        throw new NullReferenceException();
                    }

                    return resolvedValue;
                };
            }

            var graphFieldInfo = new GraphFieldInfo(_options.FieldNamingStrategy.ResolveFieldName(propertyInfo), type, resolver, new GraphFieldArgumentInfo[0]);
            return graphFieldInfo
                .WithMetadataField(propertyInfo)
                .WithMetadataField((MemberInfo)propertyInfo);
        }

        private GraphFieldInfo CreateFieldInfo(MethodInfo methodInfo, FieldResolver next)
        {
            var type = CreateType(methodInfo.ReturnType);
            var skipParameterTypes = new[] { typeof(FieldResolveContext), typeof(FieldResolver) };
            var resolverParameters = methodInfo.GetParameters();
            var fieldParameters = resolverParameters.Where(x => !skipParameterTypes.Contains(x.ParameterType)).Select(CreateFieldArgument).ToArray();
            var fieldName = _options.FieldNamingStrategy.ResolveFieldName(methodInfo);

            async Task<object> Resolver(FieldResolveContext context)
            {
                // NOTE arguments have already been coerced outside of the resolve function
                var paramteters = resolverParameters.Select(arg => GetResolverParameterValue(context, next, arg)).ToArray();

                // TODO replace with a compiled expression that invokes the method
                var result = methodInfo.Invoke(context.Instance, paramteters);
                return await UnwrapResult(result);
            }

            return new GraphFieldInfo(fieldName, type, Resolver, fieldParameters)
                .WithMetadataField(methodInfo)
                .WithMetadataField((MemberInfo)methodInfo);
        }

        private static object GetResolverParameterValue(FieldResolveContext context, FieldResolver next, ParameterInfo arg)
        {
            if (arg.ParameterType == typeof(FieldResolveContext))
            {
                return context;
            }

            if (arg.ParameterType == typeof(FieldResolver))
            {
                return next;
            }

            return context.Arguments.ContainsKey(arg.Name) ? context.Arguments[arg.Name] : null;
        }

        private GraphFieldArgumentInfo CreateFieldArgument(ParameterInfo arg)
        {
            var argType = CreateType(arg.ParameterType, true);
            
            // TODO use a naming strategy 
            return new GraphFieldArgumentInfo(argType, arg.Name, arg.HasDefaultValue, arg.DefaultValue);
        }

        private static async Task<object> UnwrapResult(object result)
        {
            var task = result as Task;
            if (task != null)
            {
                await task;
                // TODO investigate if using expressions directly would be more performant
                dynamic dynamicTask = task;
                return dynamicTask.Result;
            }

            return result;
        }

        private struct TypeCacheKey
        {
            public Type ClrType { get; set; }

            public bool IsInput { get; set; }

            private bool Equals(TypeCacheKey other)
            {
                return ClrType == other.ClrType && IsInput == other.IsInput;
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                return obj is TypeCacheKey && Equals((TypeCacheKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (ClrType.GetHashCode() * 397) ^ IsInput.GetHashCode();
                }
            }
        }
    }
}