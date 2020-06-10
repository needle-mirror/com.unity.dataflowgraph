using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Unity.DataFlowGraph
{
    static class ReflectionTools
    {
        public static IEnumerable<FieldInfo> WalkTypeInstanceFields(Type topLevel, BindingFlags flags, Func<Type, bool> filter)
        {
            foreach(var field in topLevel.GetFields(flags | BindingFlags.Instance))
            {
                var subType = field.FieldType;

                if(!filter(subType))
                {
                    if (!subType.IsPrimitive && !subType.IsPointer)
                    {
                        foreach (var subField in WalkTypeInstanceFields(subType, flags | BindingFlags.Instance, filter))
                            yield return subField;
                    }
                }
                else
                {
                    yield return field;
                }
            }
        }

        public static bool IsBufferDefinition(FieldInfo f)
            => IsBufferDefinition(f.FieldType);

        public static bool IsBufferDefinition(Type t)
            => t.IsConstructedGenericType && t.GetGenericTypeDefinition() == typeof(Buffer<>);

    }

 
}
