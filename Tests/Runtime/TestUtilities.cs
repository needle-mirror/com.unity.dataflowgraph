using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Unity.DataFlowGraph.Tests
{
    public sealed class InvalidTestNodeDefinitionAttribute : Attribute {}

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

                if (def.IsAssignableFrom(type) && !type.IsAbstract)
                {
                    yield return type;
                }
            }
        }

        public static IEnumerable<Type> FindValidTestNodes()
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
                    !type.GetCustomAttributes(true).Any(a => a is InvalidTestNodeDefinitionAttribute) &&
                    !type.IsGenericType &&
                    type != typeof(NodeWithAllTypesOfPorts))
                {
                    yield return type;
                }
            }
        }

        static NodeHandle CreateNodeFromTypeShim<TNodeDefinition>(NodeSet set)
            where TNodeDefinition : NodeDefinition, new()
        {
            return set.Create<TNodeDefinition>();
        }

        static public NodeHandle CreateNodeFromType(this NodeSet set, Type nodeType)
        {
            var method = typeof(TestUtilities).GetMethod(nameof(CreateNodeFromTypeShim), BindingFlags.Static | BindingFlags.NonPublic);
            var fn = method.MakeGenericMethod(nodeType);
            return (NodeHandle)fn.Invoke(null, new [] { set });
        }

        static PortDescription GetStaticPortDescriptionFromTypeShim<TNodeDefinition>(NodeSet set)
            where TNodeDefinition : NodeDefinition, new()
        {
            return set.GetDefinition<TNodeDefinition>().GetStaticPortDescription();
        }

        static public PortDescription GetStaticPortDescriptionFromType(this NodeSet set, Type nodeType)
        {
            var method = typeof(TestUtilities).GetMethod(nameof(GetStaticPortDescriptionFromTypeShim), BindingFlags.Static | BindingFlags.NonPublic);
            var fn = method.MakeGenericMethod(nodeType);
            return (PortDescription)fn.Invoke(null, new [] { set });
        }

        [Test]
        public static void ExpectedNumberOfTestNodes_AreReported()
        {
            Assert.Greater(FindValidTestNodes().Count(), 100);
        }

        [Test]
        public static void ExpectedNumberOfDFGExportedNodes_AreReported()
        {
            Assert.AreEqual(1, FindDFGExportedNodes().Count());
        }
    }
}
