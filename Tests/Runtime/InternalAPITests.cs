using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using System.Linq;
using Unity.Collections.LowLevel.Unsafe;
using System.Runtime.CompilerServices;

namespace Unity.DataFlowGraph.Tests
{
    public class InternalAPITests
    {
        // TODO: Assert NodeHandle<T> is perfectly convertible to NodeHandle, and Exists() api returns consistently for both
#if !ENABLE_IL2CPP
        static IEnumerable<Type> FindExportedNodes()
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

        public NodeHandle Create<TNodeDefinition>(NodeSet set)
            where TNodeDefinition : NodeDefinition, new()
        {
            return set.Create<TNodeDefinition>();
        }

        [Test]
        public void GetInputOutputDescription_IsConsistentWithPortDescription([ValueSource(nameof(FindExportedNodes))] Type nodeType)
        {
            var type = GetType();
            var method = type.GetMethod(nameof(Create));
            var fn = method.MakeGenericMethod(nodeType);

            using (var set = new NodeSet())
            {
                var handle = (NodeHandle)fn.Invoke(this, new [] { set });

                var def = set.GetDefinition(handle);
                var ports = set.GetDefinition(handle).GetPortDescription(handle);

                foreach(var input in ports.Inputs)
                {
                    var desc = def.GetFormalInput(set.Validate(handle), new InputPortArrayID(input));

                    Assert.AreEqual(desc, input);
                }

                foreach (var output in ports.Outputs)
                {
                    var desc = def.GetFormalOutput(set.Validate(handle), output);

                    Assert.AreEqual(desc, output);
                }

                set.Destroy(handle);
            }
        }

        [Test]
        public void ExpectedNumberOfExportedNodes_AreTested()
        {
            Assert.AreEqual(1, FindExportedNodes().Count());
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

            ref var buffer = ref Unsafe.AsRef<Buffer<byte>>(&d);

            Assert.True(d.Ptr == buffer.Ptr);
            Assert.AreEqual(d.Size, buffer.Size);
            Assert.AreEqual(d.OwnerNode, buffer.OwnerNode);
        }
    }
}
