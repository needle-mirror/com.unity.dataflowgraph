using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;

namespace Unity.DataFlowGraph.Tests
{
    /// <summary>
    /// Used to tag a node as not instantiable automatically for generic tests.
    /// Could be because it itself is an invalid node definition, tests corner cases
    /// with exceptions or requires more contexts than a direct instantiation gives.
    /// </summary>
    public sealed class IsNotInstantiableAttribute : Attribute {}

    static class TestUtilities
    {
        public static IEnumerable<Type> FindDFGExportedNodes()
        {
            // Always test at least one normal node (also NUnit barfs if there is none available)
            yield return typeof(NodeWithAllTypesOfPorts);

            var def = typeof(NodeDefinition);
            // Locate assembly containing our custom nodes.
            var asm = Assembly.GetAssembly(def);

            foreach (var type in asm.GetTypes())
            {
                // Skip invalid definition, as it is not disposable.
                if (type == typeof(InvalidDefinitionSlot))
                    continue;

                // Entity nodes are not default-constructible, and needs to live in a special set.
                if (type == typeof(InternalComponentNode))
                    continue;

                if (def.IsAssignableFrom(type) &&
                    !type.IsAbstract &&
                    !type.IsGenericType)
                {
                    yield return type;
                }
            }
        }

        public static IEnumerable<Type> FindInstantiableTestNodes()
        {
            foreach (var dfgType in FindDFGExportedNodes())
            {
                yield return dfgType;
            }

            // Locate assembly containing our test nodes.
            var asm = Assembly.GetAssembly(typeof(TestUtilities));

            foreach (var type in asm.GetTypes())
            {
                if (typeof(NodeDefinition).IsAssignableFrom(type) &&
                    !type.IsAbstract &&
                    !type.GetCustomAttributes(true).Any(a => a is IsNotInstantiableAttribute) &&
                    !type.IsGenericType &&
                    type != typeof(NodeWithAllTypesOfPorts))
                {
                    yield return type;
                }
            }
        }

        public static NativeSlice<T> GetTestingValueBlocking<T>(this NodeSet set, GraphValueArray<T> graphValue)
            where T : struct
        {
            var resolver = set.GetGraphValueResolver(out var job);
            job.Complete();
            return resolver.Resolve(graphValue);
        }

        static NodeHandle CreateNodeFromTypeShim<TNodeDefinition>(NodeSet set)
            where TNodeDefinition : NodeDefinition, new()
        {
            return set.Create<TNodeDefinition>();
        }

        public static NodeHandle CreateNodeFromType(this NodeSet set, Type nodeType)
        {
            var method = typeof(TestUtilities).GetMethod(nameof(CreateNodeFromTypeShim), BindingFlags.Static | BindingFlags.NonPublic);
            var fn = method.MakeGenericMethod(nodeType);
            return (NodeHandle)fn.Invoke(null, new [] { set });
        }

        static PortDescription GetStaticPortDescriptionFromTypeShim<TNodeDefinition>(NodeSet set)
            where TNodeDefinition : NodeDefinition, new()
        {
            return set.GetStaticPortDescription<TNodeDefinition>();
        }

        public static PortDescription GetStaticPortDescriptionFromType(this NodeSet set, Type nodeType)
        {
            var method = typeof(TestUtilities).GetMethod(nameof(GetStaticPortDescriptionFromTypeShim), BindingFlags.Static | BindingFlags.NonPublic);
            var fn = method.MakeGenericMethod(nodeType);
            return (PortDescription)fn.Invoke(null, new [] { set });
        }

        static NodeDefinition GetDefinitionFromTypeShim<TNodeDefinition>(NodeSet set)
            where TNodeDefinition : NodeDefinition, new()
        {
            return set.GetDefinition<TNodeDefinition>();
        }

        public static NodeDefinition GetDefinitionFromType(this NodeSet set, Type nodeType)
        {
            var method = typeof(TestUtilities).GetMethod(nameof(GetDefinitionFromTypeShim), BindingFlags.Static | BindingFlags.NonPublic);
            var fn = method.MakeGenericMethod(nodeType);
            return (NodeDefinition)fn.Invoke(null, new [] { set });
        }

        [Test]
        public static void ExpectedNumberOfTestNodes_AreReported()
        {
            Assert.Greater(FindInstantiableTestNodes().Count(), 100);
        }

        [Test]
        public static void ExpectedNumberOfDFGExportedNodes_AreReported()
        {
            Assert.AreEqual(1, FindDFGExportedNodes().Count());
        }
    }
}
