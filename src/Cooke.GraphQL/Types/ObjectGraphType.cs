using System;
using Cooke.GraphQL.IntrospectionSchema;
using GraphQLParser.AST;

namespace Cooke.GraphQL.Types
{
    public sealed class ObjectGraphType : ComplexGraphType
    {
        public ObjectGraphType(Type clrType) : base(clrType)
        {
        }

        public override __TypeKind Kind => __TypeKind.Object;

        public override object CoerceInputValue(GraphQLValue value)
        {
            throw new TypeCoercionException("Cannot coerce an input value to an object type", value.Location);
        }
    }
}