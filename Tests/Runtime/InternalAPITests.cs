using System;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.DataFlowGraph.Tests
{
    public class InternalAPITests
    {
        // TODO: Assert NodeHandle<T> is perfectly convertible to NodeHandle, and Exists() api returns consistently for both
#if !ENABLE_IL2CPP
        [Test]
        public void GetInputOutputDescription_IsConsistentWithPortDescription([ValueSource(typeof(TestUtilities), nameof(TestUtilities.FindDFGExportedNodes))] Type nodeType)
        {
            using (var set = new NodeSet())
            {
                var handle = set.CreateNodeFromType(nodeType);

                var def = set.GetDefinition(handle);
                var ports = set.GetDefinition(handle).GetPortDescription(handle);

                foreach(var input in ports.Inputs)
                {
                    var desc = def.GetFormalInput(set.Validate(handle), new InputPortArrayID(input));

                    Assert.AreEqual(desc, input);
                }

                foreach (var output in ports.Outputs)
                {
                    var desc = def.GetFormalOutput(set.Validate(handle), new OutputPortArrayID(output));

                    Assert.AreEqual(desc, output);
                }

                set.Destroy(handle);
            }
        }
#endif

        [Test]
        public unsafe void Buffer_AndBufferDescription_HaveSameLayout()
        {
            var typed = typeof(Buffer<byte>);
            var untyped = typeof(BufferDescription);

            Assert.AreEqual(UnsafeUtility.SizeOf(typed), UnsafeUtility.SizeOf(untyped));

            var fields = typed.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

            Assert.AreEqual(1, fields.Length);
            Assert.AreEqual(fields[0].FieldType, untyped);
            Assert.Zero(UnsafeUtility.GetFieldOffset(fields[0]));
        }

        [Test]
        public unsafe void Buffer_CreatedFromDescription_MatchesDescription()
        {
            var d = new BufferDescription((void*)0x13, 12, default);
            var buffer = new Buffer<byte>(d);

            Assert.True(d.Equals(buffer));
            Assert.True(d.Equals(buffer.Description));
        }

        [Test]
        public unsafe void Buffer_CanAliasDescription()
        {
            var d = new BufferDescription((void*)0x13, 12, default);

            ref var buffer = ref UnsafeUtility.AsRef<Buffer<byte>>(&d);

            Assert.True(d.Ptr == buffer.Ptr);
            Assert.AreEqual(d.Size, buffer.Size);
            Assert.AreEqual(d.OwnerNode, buffer.OwnerNode);
        }
    }
}
