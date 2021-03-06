using System.Collections.Generic;
using Cooke.GraphQL.Types;

namespace Cooke.GraphQL.Introspection
{
    public class __TypeProvider
    {
        private readonly Dictionary<BaseType, __Type> _types = new Dictionary<BaseType, __Type>();

        public __Type GetOrCreateType(BaseType type)
        {
            __Type returnType;
            if (_types.TryGetValue(type, out returnType))
            {
                return returnType;
            }

            returnType = new __Type(type, this);
            return returnType;
        }

        public void RegisterType(BaseType type, __Type introType)
        {
            _types[type] = introType;
        }
    }
}