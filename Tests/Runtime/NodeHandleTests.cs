using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;

namespace Unity.DataFlowGraph.Tests
{
    public class NodeHandleTests
    {
        [Test]
        public void NodeHandles_Record_NodeSetID_WhenCreated()
        {
            using (var set = new NodeSet())
            {
                NodeHandle node = set.Create<EmptyNode>();
                Assert.AreEqual(set.NodeSetID, node.NodeSetID);
                set.Destroy(node);
            }
        }

        [Test]
        public void NodeHandles_HaveExpected_SizeAndAlignment()
        {
            Assert.AreEqual(8, UnsafeUtility.SizeOf<VersionedHandle>(), "SizeOf(VersionedHandle)");
            Assert.AreEqual(4, UnsafeUtility.AlignOf<VersionedHandle>(), "AlignOf(VersionedHandle)");
            Assert.AreEqual(8, UnsafeUtility.SizeOf<NodeHandle>(), "SizeOf(NodeHandle)");
            Assert.AreEqual(4, UnsafeUtility.AlignOf<NodeHandle>(), "AlignOf(NodeHandle)");
            Assert.AreEqual(8, UnsafeUtility.SizeOf<NodeHandle<EmptyNode>>(), "SizeOf(NodeHandle<T>)");
            Assert.AreEqual(4, UnsafeUtility.AlignOf<NodeHandle<EmptyNode>>(), "AlignOf(NodeHandle<T>)");
            Assert.AreEqual(8, UnsafeUtility.SizeOf<ValidatedHandle>(), "SizeOf(ValidatedHandle)");
            Assert.AreEqual(4, UnsafeUtility.AlignOf<ValidatedHandle>(), "AlignOf(ValidatedHandle)");
        }

        [Test]
        public void Typed_EqualAndDifferentNodes_CompareInExpectedOrder()
        {
            using (var set = new NodeSet())
            {
                var a = set.Create<EmptyNode>();
                var b = set.Create<EmptyNode>();
                var c = set.Create<EmptyNode2>();

                // Cannot compact following statements into reused functions,
                // because they are subtly different and generic rules impact
                // function overloads and availability

                // Compare equal type and instance
                Assert.AreEqual(a, a);
#pragma warning disable 1718  // comparison to same variable
                Assert.IsTrue(a == a);
#pragma warning restore 1718
                Assert.IsTrue(a.Equals(a));
                Assert.IsTrue(((object)a).Equals(a));
                Assert.IsTrue(a.Equals((object)a));

                // Compare LR equal type and non-equal instance
                Assert.AreNotEqual(a, b);
                Assert.IsFalse(a == b);
                Assert.IsFalse(a.Equals(b));
                Assert.IsFalse(((object)a).Equals(b));
                Assert.IsFalse(a.Equals((object)b));

                // Compare RL equal type and non-equal instance
                Assert.AreNotEqual(b, a);
                Assert.IsFalse(b == a);
                Assert.IsFalse(b.Equals(a));
                Assert.IsFalse(((object)b).Equals(a));
                Assert.IsFalse(b.Equals((object)a));

                // Compare LR unequal type and non-equal instance
                Assert.AreNotEqual(b, c);
                Assert.IsFalse(b.Equals(c));
                Assert.IsFalse(((object)b).Equals(c));
                Assert.IsFalse(b.Equals((object)c));

                // Compare RL unequal type and non-equal instance
                Assert.AreNotEqual(c, b);
                Assert.IsFalse(c.Equals(b));
                Assert.IsFalse(((object)c).Equals(b));
                Assert.IsFalse(c.Equals((object)b));

                var comparer = EqualityComparer<NodeHandle<EmptyNode>>.Default;

                Assert.IsTrue(comparer.Equals(a, a));

                Assert.IsFalse(comparer.Equals(b, a));
                Assert.IsFalse(comparer.Equals(a, b));

                set.Destroy(a, b, c);
            }
        }

        [Test]
        public void Untyped_EqualAndDifferentNodes_CompareInExpectedOrder()
        {
            using (var set = new NodeSet())
            {
                NodeHandle
                    a = set.Create<EmptyNode>(),
                    b = set.Create<EmptyNode>(),
                    c = set.Create<EmptyNode2>();

                // Cannot compact following statements into reused functions,
                // because they are subtly different and generic rules impact
                // function overloads and availability

                // Compare equal type and instance
                Assert.AreEqual(a, a);
#pragma warning disable 1718  // comparison to same variable
                Assert.IsTrue(a == a);
#pragma warning restore 1718
                Assert.IsTrue(a.Equals(a));
                Assert.IsTrue(((object)a).Equals(a));
                Assert.IsTrue(a.Equals((object)a));

                // Compare LR equal type and non-equal instance
                Assert.AreNotEqual(a, b);
                Assert.IsFalse(a == b);
                Assert.IsFalse(a.Equals(b));
                Assert.IsFalse(((object)a).Equals(b));
                Assert.IsFalse(a.Equals((object)b));

                // Compare RL equal type and non-equal instance
                Assert.AreNotEqual(b, a);
                Assert.IsFalse(b == a);
                Assert.IsFalse(b.Equals(a));
                Assert.IsFalse(((object)b).Equals(a));
                Assert.IsFalse(b.Equals((object)a));

                // Compare LR unequal type and non-equal instance
                Assert.AreNotEqual(b, c);
                Assert.IsFalse(b == c);
                Assert.IsFalse(b.Equals(c));
                Assert.IsFalse(((object)b).Equals(c));
                Assert.IsFalse(b.Equals((object)c));

                // Compare RL unequal type and non-equal instance
                Assert.AreNotEqual(c, b);
                Assert.IsFalse(c == b);
                Assert.IsFalse(c.Equals(b));
                Assert.IsFalse(((object)c).Equals(b));
                Assert.IsFalse(c.Equals((object)b));

                var comparer = EqualityComparer<NodeHandle>.Default;

                Assert.IsTrue(comparer.Equals(a, a));

                Assert.IsFalse(comparer.Equals(b, a));
                Assert.IsFalse(comparer.Equals(a, b));

                Assert.IsFalse(comparer.Equals(a, c));
                Assert.IsFalse(comparer.Equals(c, a));

                Assert.IsFalse(comparer.Equals(c, b));
                Assert.IsFalse(comparer.Equals(b, c));

                set.Destroy(a, b, c);
            }
        }

        [Test]
        public void HashCodeIsStable_AndEqual_ForTypedBoxedAndUntyped()
        {
            const int iterations = 50;

            var codes = new Dictionary<int, int>();
            var gc = new List<NodeHandle<EmptyNode>>();

            using (var set = new NodeSet())
            {

                for (int i = 0; i < iterations; ++i)
                {
                    var handle = set.Create<EmptyNode>();

                    gc.Add(handle);
                    codes[i] = handle.GetHashCode();

                    for (int z = 0; z < i; ++z)
                    {
                        var current = gc[z];

                        var hashCode = codes[z];

                        Assert.AreEqual(hashCode, current.GetHashCode());
                        Assert.AreEqual(hashCode, ((NodeHandle)current).GetHashCode());
                        Assert.AreEqual(hashCode, ((object)current).GetHashCode());
                    }
                }

                foreach (var node in gc)
                    set.Destroy(node);
            }
        }

        [Test]
        public void TypedUntypedBoxesHandles_WorksInHashMaps()
        {
            const int iterations = 50;

            var typedHandles = new Dictionary<int, NodeHandle<EmptyNode>>();
            var untypedHandles = new Dictionary<int, NodeHandle>();
            var boxedHandles = new Dictionary<int, object>();

            var gc = new List<NodeHandle<EmptyNode>>();

            using (var set = new NodeSet())
            {

                for (int i = 0; i < iterations; ++i)
                {
                    var handle = set.Create<EmptyNode>();
                    gc.Add(handle);

                    typedHandles[i] = handle;
                    untypedHandles[i] = handle;
                    boxedHandles[i] = handle;

                    Assert.AreEqual(handle.GetHashCode(), typedHandles[i].GetHashCode());
                    Assert.AreEqual(handle.GetHashCode(), untypedHandles[i].GetHashCode());
                    Assert.AreEqual(handle.GetHashCode(), boxedHandles[i].GetHashCode());
                }

                foreach (var node in gc)
                    set.Destroy(node);
            }
        }

    }
}
