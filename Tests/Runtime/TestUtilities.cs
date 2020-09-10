using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Unity.DataFlowGraph.Tests
{
    /// <summary>
    /// Used to tag a node as not instantiable automatically for generic tests.
    /// Could be because it itself is an invalid node definition, tests corner cases
    /// with exceptions or requires more contexts than a direct instantiation gives.
    /// </summary>
    public sealed class IsNotInstantiableAttribute : Attribute {}

#pragma warning disable 618
    // Silence warnings about old NodeDefinitions in Tests
    public abstract class NodeDefinition<TSimulationPortDefinition>
        : Unity.DataFlowGraph.NodeDefinition<TSimulationPortDefinition>
            where TSimulationPortDefinition : struct, ISimulationPortDefinition
    {}
    public abstract class NodeDefinition<TNodeData, TSimulationPortDefinition>
        : Unity.DataFlowGraph.NodeDefinition<TNodeData, TSimulationPortDefinition>
            where TNodeData : struct, INodeData
            where TSimulationPortDefinition : struct, ISimulationPortDefinition
    {}
    public abstract class NodeDefinition<TKernelData, TKernelPortDefinition, TKernel>
        : Unity.DataFlowGraph.NodeDefinition<TKernelData, TKernelPortDefinition, TKernel>
            where TKernelData : struct, IKernelData
            where TKernelPortDefinition : struct, IKernelPortDefinition
            where TKernel : struct, IGraphKernel<TKernelData, TKernelPortDefinition>
    {}
    public abstract class NodeDefinition<TNodeData, TKernelData, TKernelPortDefinition, TKernel>
        : Unity.DataFlowGraph.NodeDefinition<TNodeData, TKernelData, TKernelPortDefinition, TKernel>
            where TNodeData : struct, INodeData
            where TKernelData : struct, IKernelData
            where TKernelPortDefinition : struct, IKernelPortDefinition
            where TKernel : struct, IGraphKernel<TKernelData, TKernelPortDefinition>
    {}
    public abstract class NodeDefinition<TNodeData, TSimulationPortDefinition, TKernelData, TKernelPortDefinition, TKernel>
        : Unity.DataFlowGraph.NodeDefinition<TNodeData, TSimulationPortDefinition, TKernelData, TKernelPortDefinition, TKernel>
            where TNodeData : struct, INodeData
            where TSimulationPortDefinition : struct, ISimulationPortDefinition
            where TKernelData : struct, IKernelData
            where TKernelPortDefinition : struct, IKernelPortDefinition
            where TKernel : struct, IGraphKernel<TKernelData, TKernelPortDefinition>
    {}
#pragma warning restore 618

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
