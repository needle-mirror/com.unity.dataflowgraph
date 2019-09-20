using System;
using System.Linq;
using NUnit.Framework;

namespace Unity.DataFlowGraph.Tests
{
    class TraitsTests
    {
        struct NodeData : INodeData { }

        class TestNode : NodeDefinition<NodeData>
        {

        }

        [Test]
        public void SetInjection_IsPerformedCorrectly_InTraits()
        {
            using (var set = new NodeSet())
            {
                var functionality = set.GetFunctionality<TestNode>();
                Assert.AreEqual(set, functionality.BaseTraits.Set);
            }
        }
    }

}
