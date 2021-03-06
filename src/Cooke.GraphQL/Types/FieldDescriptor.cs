using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Cooke.GraphQL.Types
{
    public class FieldDescriptor
    {
        private readonly Dictionary<object, object> _metadata;

        public FieldDescriptor(string name, BaseType type, FieldResolver resolver, FieldArgumentDescriptor[] arguments, Dictionary<object, object> metadata = null)
        {
            Name = name;
            Type = type;
            Resolver = resolver;
            Arguments = arguments;
            _metadata = metadata ?? new Dictionary<object, object>();
        }

        public string Name { get; }

        public BaseType Type { get; }

        public FieldResolver Resolver { get; }

        public FieldArgumentDescriptor[] Arguments { get; }

        public T GetMetadata<T>()
        {
            var key = typeof(T);
            return (T) (_metadata.ContainsKey(key) ? _metadata[key] : null);
        }

        public FieldDescriptor WithMetadataField<T>(T value)
        {
            var newDictionary = _metadata.ToDictionary(x => x.Key, x => x.Value);
            newDictionary[typeof(T)] = value;
            return new FieldDescriptor(Name, Type, Resolver, Arguments, newDictionary);
        }
    }

    public delegate Task<object> FieldResolver(FieldResolveContext context);
}