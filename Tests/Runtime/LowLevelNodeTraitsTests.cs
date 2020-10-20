using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Burst;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.TestTools;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Unity.DataFlowGraph.Tests
{
    class LowLevelNodeTraitsTests
    {
        public enum NodeType
        {
            NonKernel,
            Kernel
        }

        // TODO: Port offsets

        const int k_NodePadding = 17;
        const int k_DataPadding = 35;
        const int k_KernelPadding = 57;

        class KernelNodeWithIO : KernelNodeDefinition<KernelNodeWithIO.KernelDefs>
        {
#pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
            public struct NestedAggregate
            {
                public Aggregate SubAggr;
            }

            public struct Aggregate
            {
                public Buffer<int> SubBuffer1;
                public Buffer<int> SubBuffer2;
            }

            public struct KernelDefs : IKernelPortDefinition
            {
                public DataInput<KernelNodeWithIO, int> Input1, Input2, Input3;
                public DataOutput<KernelNodeWithIO, int> Output1, Output2;
                public DataOutput<KernelNodeWithIO, Buffer<int>> Output3;
                public DataOutput<KernelNodeWithIO, Aggregate> Output4;
                public DataOutput<KernelNodeWithIO, NestedAggregate> Output5;
            }
#pragma warning restore 649

            internal unsafe struct Node : INodeData
            {
                fixed byte m_Pad[k_NodePadding];
            }

            internal unsafe struct Data : IKernelData
            {
                fixed byte m_Pad[k_DataPadding];
            }

            [BurstCompile(CompileSynchronously = true)]
            internal unsafe struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                fixed byte m_Pad[k_KernelPadding];

                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports) { }
            }
        }

        public enum CompilationFlags
        {
            Bursted,
            Managed
        }

        class EmptyKernelNode : KernelNodeDefinition<EmptyKernelNode.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition { }

            unsafe struct Node : INodeData
            {
                fixed byte m_Pad[k_NodePadding];
            }

            internal unsafe struct Data : IKernelData
            {
                fixed byte m_Pad[k_DataPadding];
            }

            internal struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports) { }
            }
        }

        class BurstedEmptyKernelNode : KernelNodeDefinition<BurstedEmptyKernelNode.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition { }

            unsafe struct Node : INodeData
            {
                fixed byte m_Pad[k_NodePadding];
            }

            internal unsafe struct Data : IKernelData
            {
                fixed byte m_Pad[k_DataPadding];
            }

            [BurstCompile(CompileSynchronously = true)]
            internal struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports) { }
            }
        }

        class SimpleNode : SimulationNodeDefinition<SimpleNode.EmptyPorts>
        {
            public struct EmptyPorts : ISimulationPortDefinition { }

            public unsafe struct Node : INodeData
            {
                fixed byte m_Pad[k_NodePadding];
            }
        }

        static SimpleType CompareSimpleType<T>(SimpleType computed)
            where T : struct
        {
            SimpleType actual = SimpleType.Create<T>();

            Assert.AreEqual(actual.Size, computed.Size);
            // analysed may be overaligned
            Assert.GreaterOrEqual(computed.Align, actual.Align);
            Assert.Zero(computed.Align % actual.Align, "Computed alignment is not a multiple of actual alignment");
            Assert.AreEqual(1, math.countbits(computed.Align), "Computed alignment is not a power-of-two");
            Assert.Zero(actual.Size % computed.Align, "Size is not a multiple of computed alignment");

            return actual;
        }

#if !ENABLE_IL2CPP // This reflection is problematic for IL2CPP
        static SimpleType CompareSimpleType(Type type, SimpleType computed)
        {
            var compareSimpleTypeGenericFunction = typeof(LowLevelNodeTraitsTests).GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Single(m => m.Name == nameof(CompareSimpleType) && m.GetGenericArguments().Length==1);
            var compareSimpleType = compareSimpleTypeGenericFunction.MakeGenericMethod(type);
            return (SimpleType)compareSimpleType.Invoke(null, new object[]{ computed });
        }

        struct EmptyStruct {}
        unsafe struct PointerStruct {void* p;}
        struct ShortAndByteStruct {short s; byte b;}
        struct ThreeByteStruct {byte b1, b2; byte b3;}
        unsafe struct TwoPointersStruct {void* p1, p2;}
        unsafe struct ThreePointersStruct {void* p1, p2, p3;}
        unsafe struct ThreePointersAndAByteStruct {void* p1, p2, p3; byte b;}
        unsafe struct ThreePointersAndAShortStruct {void* p1, p2, p3; short b;}
        unsafe struct BigStructWithPointers {void* p1, p2, p3, p4; int i1, i2, i3; short s1, s2, s3; byte b;}

        [TestCase(typeof(bool), 1, 1, 1),
         TestCase(typeof(byte), 1, 1, 1),
         TestCase(typeof(short), 2, 2, 2),
         TestCase(typeof(int), 4, 4, 4),
         TestCase(typeof(float), 4, 4, 4),
         TestCase(typeof(double), 8, 8, 8),
         TestCase(typeof(long), 8, 8, 8),
         TestCase(typeof(ThreeByteStruct), 3, 1, 1),
         TestCase(typeof(ShortAndByteStruct), 4, 2, 4),
#if UNITY_64 || UNITY_EDITOR_64
         TestCase(typeof(PointerStruct), 8, 8, 8),
         TestCase(typeof(TwoPointersStruct), 16, 8, 16),
         TestCase(typeof(ThreePointersStruct), 24, 8, 8),
         TestCase(typeof(ThreePointersAndAByteStruct), 32, 8, SimpleType.MaxAlignment),
         TestCase(typeof(ThreePointersAndAShortStruct), 32, 8, SimpleType.MaxAlignment),
         TestCase(typeof(BigStructWithPointers), 56, 8, 8),
#else
         TestCase(typeof(PointerStruct), 4, 4, 4),
         TestCase(typeof(TwoPointersStruct), 8, 4, 8),
         TestCase(typeof(ThreePointersStruct), 12, 4, 4),
         TestCase(typeof(ThreePointersAndAByteStruct), 16, 8, SimpleType.MaxAlignment),
         TestCase(typeof(ThreePointersAndAShortStruct), 16, 8, SimpleType.MaxAlignment),
         TestCase(typeof(BigStructWithPointers), 12 + 16 + 4 + 4, 8, SimpleType.MaxAlignment),
#endif
         TestCase(typeof(EmptyStruct), 1, 1, 1)]
        public void SimpleType_CorrectlyComputes_AlignmentAndSize_FromSystemType(Type type, int expectedSize, int expectedAlignment, int expectedComputedAlignment)
        {
            var alignOfGenericMethod = typeof(UnsafeUtility).GetMethod(nameof(UnsafeUtility.AlignOf), BindingFlags.Static | BindingFlags.Public);
            var alignOfMethod = alignOfGenericMethod.MakeGenericMethod(type);
            var actualAlign = (int) alignOfMethod.Invoke(null, new object[0]);

            Assert.AreEqual(actualAlign, expectedAlignment);

            var sizeOfGenericMethod = typeof(UnsafeUtility).GetMethods().Single(m => m.Name == nameof(UnsafeUtility.SizeOf) && m.GetGenericArguments().Length==1);
            var sizeOfMethod = sizeOfGenericMethod.MakeGenericMethod(type);
            var actualSize = (int) sizeOfMethod.Invoke(null, new object[0]);

            Assert.AreEqual(actualSize, expectedSize);

            SimpleType actual = CompareSimpleType(type, new SimpleType(expectedSize, expectedAlignment));
            Assert.AreEqual(actual.Align, expectedAlignment);
            Assert.AreEqual(actual.Size, expectedSize);

            var computed = new SimpleType(type);
            CompareSimpleType(type, computed);

            Assert.Zero(expectedComputedAlignment % expectedAlignment, "Expected computed alignment is not a multiple of expected alignment");
            Assert.AreEqual(expectedComputedAlignment, computed.Align);
        }
#endif // ENABLE_IL2CPP

        [TestCase(NodeType.Kernel), TestCase(NodeType.NonKernel)]
        public void LowLevelNodeTraits_IsCreated(NodeType type)
        {
            using (var set = new NodeSet())
            {
                var node = type == NodeType.Kernel ? (NodeHandle)set.Create<KernelNodeWithIO>() : set.Create<SimpleNode>();

                ref readonly var llTraits = ref set.GetNodeTraits(node);

                Assert.IsTrue(llTraits.IsCreated);
                set.Destroy(node);
            }
        }

        [TestCase(NodeType.Kernel), TestCase(NodeType.NonKernel)]
        public void HasKernelData_IsOnlySet_ForNodesThatDeclareIt(NodeType type)
        {
            bool shouldHaveKernelData = type == NodeType.Kernel;

            using (var set = new NodeSet())
            {
                var node = shouldHaveKernelData ? (NodeHandle)set.Create<KernelNodeWithIO>() : set.Create<SimpleNode>();

                ref readonly var llTraits = ref set.GetNodeTraits(node);

                Assert.AreEqual(shouldHaveKernelData, llTraits.HasKernelData);

                set.Destroy(node);
            }
        }

        [Test]
        public void StorageRequirementAnalysis_IsEquivalentToLanguageIntrinsics()
        {
            using (var set = new NodeSet())
            {
                NodeHandle node = set.Create<KernelNodeWithIO>();

                ref readonly var llTraits = ref set.GetNodeTraits(node);

                CompareSimpleType<KernelNodeWithIO.Kernel>(llTraits.KernelStorage.Kernel);
                CompareSimpleType<KernelNodeWithIO.KernelDefs>(llTraits.KernelStorage.KernelPorts);
                CompareSimpleType<KernelNodeWithIO.Data>(llTraits.KernelStorage.KernelData);
                CompareSimpleType<KernelNodeWithIO.Node>(llTraits.SimulationStorage.NodeData);

                set.Destroy(node);
            }
        }

        [TestCase(NodeType.Kernel), TestCase(NodeType.NonKernel)]
        public void AllNodes_AlwaysHaveNonDefault_SimulationPortStorage(NodeType type)
        {
            using (var set = new NodeSet())
            {
                NodeHandle node = type == NodeType.Kernel ? (NodeHandle)set.Create<KernelNodeWithIO>() : set.Create<SimpleNode>();

                Assert.AreNotEqual(new SimpleType(), set.GetNodeTraits(node).SimulationStorage.SimPorts);

                set.Destroy(node);
            }
        }

        [TestCase(NodeType.Kernel), TestCase(NodeType.NonKernel)]
        public void AllNodes_AlwaysHaveNonDefault_VTable(NodeType type)
        {
            using (var set = new NodeSet())
            {
                NodeHandle node = type == NodeType.Kernel ? (NodeHandle)set.Create<KernelNodeWithIO>() : set.Create<SimpleNode>();

                ref readonly var llTraits = ref set.GetNodeTraits(node);

                Assert.AreNotEqual(new LowLevelNodeTraits.VirtualTable(), llTraits.VTable.KernelFunction.JobData);
                Assert.AreNotEqual(IntPtr.Zero, llTraits.VTable.KernelFunction.JobData);

                set.Destroy(node);
            }
        }

        [TestCase(NodeType.Kernel), TestCase(NodeType.NonKernel)]
        public void KernelFunctionIsOverriden_OnlyOnKernelNodes(NodeType type)
        {
            bool isKernelType = type == NodeType.Kernel;

            using (var set = new NodeSet())
            {
                NodeHandle node = isKernelType ? (NodeHandle)set.Create<KernelNodeWithIO>() : set.Create<SimpleNode>();

                Assert.AreEqual(isKernelType, LowLevelNodeTraits.VirtualTable.IsMethodImplemented(set.GetNodeTraits(node).VTable.KernelFunction));

                set.Destroy(node);
            }
        }

        [Test]
        public void CreatedVirtualTable_ContainsOnlyPureVirtualFunctions()
        {
            var function = LowLevelNodeTraits.VirtualTable.Create();
            Assert.IsFalse(LowLevelNodeTraits.VirtualTable.IsMethodImplemented(function.KernelFunction));
        }

        [Test]
        public unsafe void PureVirtualFunctions_CanBeScheduledAndInvoked_ButWillPrintError()
        {
            var function = LowLevelNodeTraits.VirtualTable.Create();

            LogAssert.Expect(LogType.Exception, new Regex("Pure virtual function"));

            try
            {
                function.KernelFunction.Invoke(new RenderContext(), new KernelLayout.Pointers());
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            LogAssert.Expect(LogType.Exception, new Regex("Pure virtual function"));
            function.KernelFunction.Schedule(new JobHandle(), new RenderContext(), new KernelLayout.Pointers()).Complete();
        }

        [TestCase(CompilationFlags.Managed), TestCase(CompilationFlags.Bursted)]
        public unsafe void OverridenVirtualFunctions_CanBeScheduledAndInvoked(CompilationFlags flags)
        {
            using (var set = new NodeSet())
            {
                NodeHandle node = flags == CompilationFlags.Bursted ? (NodeHandle)set.Create<BurstedEmptyKernelNode>() : set.Create<EmptyKernelNode>();

                ref readonly var llTraits = ref set.GetNodeTraits(node);

                BurstedEmptyKernelNode.Data bdata;
                EmptyKernelNode.Data data;
                BurstedEmptyKernelNode.Kernel bkernel;
                EmptyKernelNode.Kernel kernel;
                BurstedEmptyKernelNode.KernelDefs bdefs;
                EmptyKernelNode.KernelDefs defs;

                var instance = new KernelLayout.Pointers
                (
                    kernel: flags == CompilationFlags.Bursted ? (RenderKernelFunction.BaseKernel*)&bkernel : (RenderKernelFunction.BaseKernel*)&kernel,
                    data: flags == CompilationFlags.Bursted ? (RenderKernelFunction.BaseData*)&bdata : (RenderKernelFunction.BaseData*)&data,
                    ports: flags == CompilationFlags.Bursted ? (RenderKernelFunction.BasePort*)&bdefs : (RenderKernelFunction.BasePort*)&defs
                );

                llTraits.VTable.KernelFunction.Invoke(new RenderContext(), instance);
                llTraits.VTable.KernelFunction.Schedule(new JobHandle(), new RenderContext(), instance).Complete();

                set.Destroy(node);
            }
        }

        [IsNotInstantiable]
        class InvalidForBurstKernelNodeWithIO : KernelNodeDefinition<InvalidForBurstKernelNodeWithIO.KernelDefs>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataOutput<InvalidForBurstKernelNodeWithIO, int> Output1, Output2;
            }

            unsafe struct Node : INodeData
            {
                fixed byte m_Pad[k_NodePadding];
            }

            internal unsafe struct Data : IKernelData
            {
                fixed byte m_Pad[k_DataPadding];
            }

#if UNITY_EDITOR
            [BurstCompile(CompileSynchronously = true)]
#endif
            internal struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports)
                {
                    var burstCantDoTypeOf = typeof(int);
                    ctx.Resolve(ref ports.Output1) = 11;
                    ctx.Resolve(ref ports.Output2) = 12;
                }
            }
        }

        struct ForceBurstSyncCompilation : IDisposable
        {
            public ForceBurstSyncCompilation(bool enable)
            {
                #if UNITY_EDITOR
                m_WasSyncCompile = Menu.GetChecked("Jobs/Burst/Synchronous Compilation");
                Menu.SetChecked("Jobs/Burst/Synchronous Compilation", enable);
                #endif
            }

            public void Dispose()
            {
                #if UNITY_EDITOR
                Menu.SetChecked("Jobs/Burst/Synchronous Compilation", m_WasSyncCompile);
                #endif
                s_FirstDomainCompilation = false;
            }

            public bool IsFirstDomainCompilation => s_FirstDomainCompilation;

            static bool s_FirstDomainCompilation = true;

            #if UNITY_EDITOR
            bool m_WasSyncCompile;
            #endif
        }

        [Test, Explicit]
        public unsafe void FailedBurstCompilationOfKernel_FallsBackToManagedKernel([Values] NodeSet.RenderExecutionModel meansOfComputation)
        {
            Assume.That(JobsUtility.JobCompilerEnabled, Is.True);

            using (var set = new NodeSet())
            using (var forceBurstSyncCompile = new ForceBurstSyncCompilation(true))
            {
                set.RendererModel = meansOfComputation;

#if UNITY_EDITOR
                // Failure is only logged as an Error when run in Editor. Burst FunctionPointers do not work in non-Editor at all
                // so those failures are currently logged as a Warning.

                // Only the first time a job is compiled in the current domain will an error be produced asynchronously
                // through Burst. This means the error will be printed twice (once by us, once from Burst from somewhere).
                // Only happens on scheduling jobs, not compiling function pointers.
                if (forceBurstSyncCompile.IsFirstDomainCompilation)
                    LogAssert.Expect(LogType.Error, new Regex("Accessing the type"));

                LogAssert.Expect(LogType.Error, new Regex("Accessing the type"));
                LogAssert.Expect(LogType.Error, new Regex("Could not Burst compile"));
#endif

                var node = set.Create<InvalidForBurstKernelNodeWithIO>();

                set.Update();
                set.DataGraph.SyncAnyRendering();

                // Check the reflection data on the KernelFunction to identify if this is indeed the Managed version of
                // the kernel's compilation as opposed to the Bursted one.
                ref readonly var llTraits = ref set.GetNodeTraits(node);

                Assert.AreEqual(
                    llTraits.VTable.KernelFunction.ReflectionData,
                    RenderKernelFunction.GetManagedFunction<InvalidForBurstKernelNodeWithIO.Data, InvalidForBurstKernelNodeWithIO.KernelDefs, InvalidForBurstKernelNodeWithIO.Kernel>().ReflectionData
                );

                var knodes = set.DataGraph.GetInternalData();
                ref var inode = ref knodes[node.VHandle.Index];
                var ports = UnsafeUtility.AsRef<InvalidForBurstKernelNodeWithIO.KernelDefs>(inode.Instance.Ports);
                Assert.AreEqual(ports.Output1.m_Value, 11);
                Assert.AreEqual(ports.Output2.m_Value, 12);

                set.Destroy(node);
            }
        }

        [Test]
        public void NonKernelNodes_HaveDefaultInitialised_KernelStorageRequirements()
        {
            using (var set = new NodeSet())
            {
                NodeHandle node = set.Create<SimpleNode>();

                ref readonly var llTraits = ref set.GetNodeTraits(node);

                Assert.AreEqual(new SimpleType(), llTraits.KernelStorage.Kernel);
                Assert.AreEqual(new SimpleType(), llTraits.KernelStorage.KernelPorts);
                Assert.AreEqual(new SimpleType(), llTraits.KernelStorage.KernelData);
                CompareSimpleType<SimpleNode.Node>(llTraits.SimulationStorage.NodeData);

                set.Destroy(node);
            }
        }

        [Test]
        public void LLDataPortCount_IsEquivalentToPortDeclaration()
        {
            using (var set = new NodeSet())
            {
                NodeHandle node = set.Create<KernelNodeWithIO>();
                var portDeclaration = set.GetDefinition(node).GetPortDescription(node);
                ref readonly var llTraits = ref set.GetNodeTraits(node);

                Assert.AreEqual(portDeclaration.Inputs.Count(p => p.Category == PortDescription.Category.Data), llTraits.DataPorts.Inputs.Count);
                Assert.AreEqual(portDeclaration.Outputs.Count(p => p.Category == PortDescription.Category.Data), llTraits.DataPorts.Outputs.Count);

                set.Destroy(node);
            }
        }

        [Test]
        public void LLDataPortBufferOffsets_AreEquivalentToPortDeclaration()
        {
            using (var set = new NodeSet())
            {
                NodeHandle node = set.Create<KernelNodeWithIO>();
                var portDeclaration = set.GetDefinition(node).GetPortDescription(node);
                ref readonly var llTraits = ref set.GetNodeTraits(node);

                int numDataBuffers = 0;
                foreach (var port in portDeclaration.Outputs)
                    numDataBuffers += port.BufferInfos.Count;

                var bufferOffsets = llTraits.DataPorts.OutputBufferOffsets;
                Assert.AreEqual(5, numDataBuffers);
                Assert.AreEqual(5, bufferOffsets.Count);

                foreach (var port in portDeclaration.Outputs)
                {
                    var portOffset =
                        UnsafeUtility.GetFieldOffset(typeof(KernelNodeWithIO.KernelDefs).GetField(port.Name));
                    foreach (var buf in port.BufferInfos)
                    {
                        for (int i = 0; i < bufferOffsets.Count; ++i)
                        {
                            if (buf.Offset + portOffset == bufferOffsets[i].Offset)
                            {
                                bufferOffsets.Remove(i, 1);
                                break;
                            }
                        }
                    }
                }
                Assert.Zero(bufferOffsets.Count);

                set.Destroy(node);
            }
        }

        [Test]
        public void PortAssignments_AreExtractedCorrectly()
        {
            using (var set = new NodeSet())
            {
                NodeHandle node = set.Create<KernelNodeWithIO>();
                ref readonly var llTraits = ref set.GetNodeTraits(node);

                Assert.AreEqual(KernelNodeWithIO.KernelPorts.Input1.Port, llTraits.DataPorts.Inputs[0].PortNumber);
                Assert.AreEqual(KernelNodeWithIO.KernelPorts.Input2.Port, llTraits.DataPorts.Inputs[1].PortNumber);
                Assert.AreEqual(KernelNodeWithIO.KernelPorts.Input3.Port, llTraits.DataPorts.Inputs[2].PortNumber);

                Assert.AreEqual(KernelNodeWithIO.KernelPorts.Output1.Port, llTraits.DataPorts.Outputs[0].PortNumber);
                Assert.AreEqual(KernelNodeWithIO.KernelPorts.Output2.Port, llTraits.DataPorts.Outputs[1].PortNumber);

                set.Destroy(node);
            }
        }

        [Test]
        public void InputPort_MemoryPatchOffsets_ForPaddedNode_AreNotZeroAndIncreasesInOffset()
        {
            using (var set = new NodeSet())
            {
                NodeHandle node = set.Create<KernelNodeWithIO>();
                ref readonly var llTraits = ref set.GetNodeTraits(node);

                var runningOffset = -1;

                for (int i = 0; i < llTraits.DataPorts.Inputs.Count; ++i)
                {
                    Assert.Greater(llTraits.DataPorts.Inputs[i].PatchOffset, runningOffset);
                    runningOffset = llTraits.DataPorts.Inputs[i].PatchOffset;
                }

                for (int i = 0; i < llTraits.DataPorts.Outputs.Count; ++i)
                {
                    Assert.Greater(llTraits.DataPorts.Outputs[i].PatchOffset, runningOffset);
                    runningOffset = llTraits.DataPorts.Outputs[i].PatchOffset;
                }

                set.Destroy(node);
            }
        }

        [Test]
        public void LLTraits_ThrowObjectDisposedException_OnNotCreated_AndDoubleDispose()
        {
            LowLevelNodeTraits traits = new LowLevelNodeTraits();

            Assert.Throws<ObjectDisposedException>(() => traits.Dispose());

            KernelNodeWithIO rawNodeDefinition = default;

            try
            {
                rawNodeDefinition = new KernelNodeWithIO();

                LLTraitsHandle handle = new LLTraitsHandle();
                Assert.DoesNotThrow(() => handle = rawNodeDefinition.BaseTraits.CreateNodeTraits(rawNodeDefinition.GetType(), rawNodeDefinition.SimulationStorageTraits, rawNodeDefinition.KernelStorageTraits));

                Assert.IsTrue(handle.IsCreated);

                handle.Dispose();

                Assert.Throws<ObjectDisposedException>(() => handle.Dispose());
            }
            finally
            {
                rawNodeDefinition?.Dispose();
            }
        }

        unsafe struct PrettyUniqueStructSize
        {
            public const int kPadSize = 0x237;
            fixed byte Pad[kPadSize];
        }

        [Test]
        public void OutputDeclaration_ElementOrType_ReflectsSizeCorrectly_ForScalars()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<NodeWithParametricPortType<PrettyUniqueStructSize>>();
                var decl = set
                    .GetNodeTraits(node)
                    .DataPorts
                    .FindOutputDataPort(NodeWithParametricPortType<PrettyUniqueStructSize>.KernelPorts.Output.Port);

                Assert.AreEqual(SimpleType.Create<PrettyUniqueStructSize>().Size, decl.ElementOrType.Size);

                set.Destroy(node);
            }
        }

        unsafe struct DifferentButUniqueStructSize
        {
            public const int kPadSize = 0x733;
            fixed byte Pad[kPadSize];
        }

        [Test]
        public void OutputDeclaration_ElementOrType_ReflectsNestedBuffer_ElementSize()
        {
            using (var set = new NodeSet())
            {
                var node = set.Create<NodeWithParametricPortType<Buffer<DifferentButUniqueStructSize>>>();
                var decl = set
                    .GetNodeTraits(node)
                    .DataPorts
                    .FindOutputDataPort(NodeWithParametricPortType<Buffer<DifferentButUniqueStructSize>>.KernelPorts.Output.Port);

                Assert.AreEqual(SimpleType.Create<DifferentButUniqueStructSize>().Size, decl.ElementOrType.Size);

                set.Destroy(node);
            }
        }

        [Test]
        public unsafe void DataStructures_HaveExpectedAlignment_AndSize()
        {
            int pointerAlignAndSize = sizeof(void*);

            // * 2 due to 3 extra ownership bits that cannot be packed in the pointer on XBox and PS4
            int expectedDataInputSizeAndAlign = pointerAlignAndSize * 2;

            Assert.AreEqual(expectedDataInputSizeAndAlign, UnsafeUtility.SizeOf<DataInputStorage>(), "SizeOf(DataInputStorage)");
            Assert.AreEqual(pointerAlignAndSize, UnsafeUtility.AlignOf<DataInputStorage>(), "AlignOf(DataInputStorage)");

            Assert.AreEqual(expectedDataInputSizeAndAlign, UnsafeUtility.SizeOf<DataInput<InvalidDefinitionSlot, Matrix4x4>>(), "SizeOf(DataInput<>)");
            Assert.AreEqual(pointerAlignAndSize, UnsafeUtility.AlignOf<DataInput<InvalidDefinitionSlot, Matrix4x4>>(), "AlignOf(DataInput<>)");
        }
    }

}
