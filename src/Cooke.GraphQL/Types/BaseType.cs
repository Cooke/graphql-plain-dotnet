using Cooke.GraphQL.Introspection;
using GraphQLParser.AST;
using Newtonsoft.Json.Linq;

namespace Cooke.GraphQL.Types
{
    public abstract class BaseType
    {
        public abstract object CoerceInputLiteralValue(GraphQLValue value);

        public abstract object CoerceInputVariableValue(JToken value);

        public abstract string Name { get; }

        public abstract __TypeKind Kind { get; }
    }
}