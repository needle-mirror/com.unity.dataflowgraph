using System;
using System.Linq;
using NUnit.Framework;

namespace Unity.DataFlowGraph.Tests
{
    class TraitsTests
    {
        struct NodeData : INodeData { }

        class TestNode : NodeDefinition<EmptyPorts>
        {

        }

        [Test]
        public void SetInjection_IsPerformedCorrectly_InTraits()
        {
            using (var set = new NodeSet())
            {
                var definition = set.GetDefinition<TestNode>();
                Assert.AreEqual(set, definition.BaseTraits.Set);
            }
        }
    }

}
