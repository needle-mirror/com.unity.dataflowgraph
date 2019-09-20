using System;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Burst;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.TestTools;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;

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

        public unsafe struct Node : INodeData
        {
            fixed byte m_Pad[k_NodePadding];
        }

        public unsafe struct Data : IKernelData
        {
            fixed byte m_Pad[k_DataPadding];
        }

        class KernelNodeWithIO : NodeDefinition<Node, Data, KernelNodeWithIO.KernelDefs, KernelNodeWithIO.Kernel>
        {
            public struct Aggregate
            {
                #pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public Buffer<int> SubBuffer1;
                public Buffer<int> SubBuffer2;
                #pragma warning restore 649
            }

            public struct KernelDefs : IKernelPortDefinition
            {
                #pragma warning disable 649  // Assigned through internal DataFlowGraph reflection
                public DataInput<KernelNodeWithIO, int> Input1, Input2, Input3;
                public DataOutput<KernelNodeWithIO, int> Output1, Output2;
                public DataOutput<KernelNodeWithIO, Buffer<int>> Output3;
                public DataOutput<KernelNodeWithIO, Aggregate> Output4;
                #pragma warning restore 649
            }

            [BurstCompile(CompileSynchronously = true)]
            public unsafe struct Kernel : IGraphKernel<Data, KernelDefs>
            {
                fixed byte m_Pad[k_KernelPadding];

                public void Execute(RenderContext ctx, Data data, ref KernelDefs ports)
                {
                }
            }
        }

        public enum CompilationFlags
        {
            Bursted,
            Managed
        }

        public struct EmptyKernelDefs : IKernelPortDefinition { }

        public struct EmptyKernel : IGraphKernel<Data, EmptyKernelDefs>
        {
            public void Execute(RenderContext ctx, Data data, ref EmptyKernelDefs ports) { }
        }

        [BurstCompile(CompileSynchronously = true)]
        public struct BurstedEmptyKernel : IGraphKernel<Data, EmptyKernelDefs>
        {
            public void Execute(RenderContext ctx, Data data, ref EmptyKernelDefs ports) { }
        }

        class EmptyKernelNode : NodeDefinition<Node, Data, EmptyKernelDefs, EmptyKernel>
        {

        }

        class BurstedEmptyKernelNode : NodeDefinition<Node, Data, EmptyKernelDefs, BurstedEmptyKernel>
        {
        }

        class SimpleNode : NodeDefinition<Node>
        {
        }

        void CompareSimpleTypes(SimpleType original, SimpleType analysed)
        {
            Assert.AreEqual(original.Size, analysed.Size);
            // analysed may be overaligned
            Assert.GreaterOrEqual(analysed.Align, original.Align);
        }

        [TestCase(NodeType.Kernel), TestCase(NodeType.NonKernel)]
        public void LowLevelNodeTraits_IsCreated(NodeType type)
        {
            using (var set = new NodeSet())
            {
                var node = type == NodeType.Kernel ? (NodeHandle)set.Create<KernelNodeWithIO>() : set.Create<SimpleNode>();

                var nodes = set.GetInternalData();
                ref var llTraits = ref set.GetLLTraits()[nodes[node.VHandle.Index].TraitsIndex].Resolve();

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

                var nodes = set.GetInternalData();
                ref var llTraits = ref set.GetLLTraits()[nodes[node.VHandle.Index].TraitsIndex].Resolve();

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
                var nodes = set.GetInternalData();
                ref var llTraits = ref set.GetLLTraits()[nodes[node.VHandle.Index].TraitsIndex].Resolve();

                CompareSimpleTypes(SimpleType.Create<KernelNodeWithIO.Kernel>(), llTraits.Storage.Kernel);
                CompareSimpleTypes(SimpleType.Create<KernelNodeWithIO.KernelDefs>(), llTraits.Storage.KernelPorts);
                CompareSimpleTypes(SimpleType.Create<Data>(), llTraits.Storage.KernelData);
                CompareSimpleTypes(SimpleType.Create<Node>(), llTraits.Storage.NodeData);

                set.Destroy(node);
            }
        }

        [TestCase(NodeType.Kernel), TestCase(NodeType.NonKernel)]
        public void AllNodes_AlwaysHaveNonDefault_SimulationPortStorage(NodeType type)
        {
            using (var set = new NodeSet())
            {
                NodeHandle node = type == NodeType.Kernel ? (NodeHandle)set.Create<KernelNodeWithIO>() : set.Create<SimpleNode>();

                var nodes = set.GetInternalData();
                ref var llTraits = ref set.GetLLTraits()[nodes[node.VHandle.Index].TraitsIndex].Resolve();

                Assert.AreNotEqual(new SimpleType(), llTraits.Storage.SimPorts);

                set.Destroy(node);
            }
        }

        [TestCase(NodeType.Kernel), TestCase(NodeType.NonKernel)]
        public void AllNodes_AlwaysHaveNonDefault_VTable(NodeType type)
        {
            using (var set = new NodeSet())
            {
                NodeHandle node = type == NodeType.Kernel ? (NodeHandle)set.Create<KernelNodeWithIO>() : set.Create<SimpleNode>();

                var nodes = set.GetInternalData();
                ref var llTraits = ref set.GetLLTraits()[nodes[node.VHandle.Index].TraitsIndex].Resolve();

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

                var nodes = set.GetInternalData();
                ref var llTraits = ref set.GetLLTraits()[nodes[node.VHandle.Index].TraitsIndex].Resolve();

                Assert.AreEqual(isKernelType, !llTraits.VTable.KernelFunction.IsPureVirtual);

                set.Destroy(node);
            }
        }

        [Test]
        public void CreatedVirtualTable_ContainsOnlyPureVirtualFunctions()
        {
            var function = LowLevelNodeTraits.VirtualTable.Create();
            Assert.IsTrue(function.KernelFunction.IsPureVirtual);
        }

        [Test]
        public unsafe void PureVirtualFunctions_CanBeScheduledAndInvoked_ButWillPrintError()
        {
            var function = LowLevelNodeTraits.VirtualTable.Create();

            LogAssert.Expect(LogType.Exception, new Regex("Pure virtual function"));

            try
            {
                function.KernelFunction.Invoke(new RenderContext(), null, null, null);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            LogAssert.Expect(LogType.Exception, new Regex("Pure virtual function"));
            function.KernelFunction.Schedule(new JobHandle(), new RenderContext(), null, null, null).Complete();
        }

        [TestCase(CompilationFlags.Managed), TestCase(CompilationFlags.Bursted)]
        public unsafe void OverridenVirtualFunctions_CanBeScheduledAndInvoked(CompilationFlags flags)
        {
#if ENABLE_IL2CPP
            if (flags == CompilationFlags.Managed)
                Assert.Ignore("Skipping test since IL2CPP is broken for non-bursted Kernels");
#endif

            using (var set = new NodeSet())
            {
                NodeHandle node = flags == CompilationFlags.Bursted ? (NodeHandle)set.Create<BurstedEmptyKernelNode>() : set.Create<EmptyKernelNode>();

                var nodes = set.GetInternalData();
                ref var llTraits = ref set.GetLLTraits()[nodes[node.VHandle.Index].TraitsIndex].Resolve();

                Data data;
                BurstedEmptyKernel bkernel;
                EmptyKernel kernel;
                EmptyKernelDefs defs;

                var kernelPointer = flags == CompilationFlags.Bursted ? (RenderKernelFunction.BaseKernel*)&bkernel : (RenderKernelFunction.BaseKernel*)&kernel;

                llTraits.VTable.KernelFunction.Invoke(new RenderContext(), kernelPointer, (RenderKernelFunction.BaseData*)&data, (RenderKernelFunction.BasePort*)&defs);
                llTraits.VTable.KernelFunction.Schedule(new JobHandle(), new RenderContext(), kernelPointer, (RenderKernelFunction.BaseData*)&data, (RenderKernelFunction.BasePort*)&defs).Complete();

                set.Destroy(node);
            }
        }

        class InvalidForBurstKernelNodeWithIO : NodeDefinition<Node, Data, InvalidForBurstKernelNodeWithIO.KernelDefs, InvalidForBurstKernelNodeWithIO.InvalidForBurstKernel>
        {
            public struct KernelDefs : IKernelPortDefinition
            {
                public DataOutput<InvalidForBurstKernelNodeWithIO, int> Output1, Output2;
            }

#if UNITY_EDITOR
            [BurstCompile(CompileSynchronously = true)]
#endif
            public struct InvalidForBurstKernel : IGraphKernel<Data, InvalidForBurstKernelNodeWithIO.KernelDefs>
            {
                public void Execute(RenderContext ctx, Data data, ref InvalidForBurstKernelNodeWithIO.KernelDefs ports)
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
                #if UNITY_EDITOR && UNITY_2019_3_OR_NEWER
                m_WasSyncCompile = Menu.GetChecked("Jobs/Burst/Synchronous Compilation");
                Menu.SetChecked("Jobs/Burst/Synchronous Compilation", enable);
                #endif
            }

            public void Dispose()
            {
                #if UNITY_EDITOR && UNITY_2019_3_OR_NEWER
                Menu.SetChecked("Jobs/Burst/Synchronous Compilation", m_WasSyncCompile);
                #endif
                s_FirstDomainCompilation = false;
            }

            public bool IsFirstDomainCompilation => s_FirstDomainCompilation;

            static bool s_FirstDomainCompilation = true;

            #if UNITY_EDITOR && UNITY_2019_3_OR_NEWER
            bool m_WasSyncCompile;
            #endif
        }

        [Test, Explicit]
        public unsafe void FailedBurstCompilationOfKernel_FallsBackToManagedKernel([Values] RenderExecutionModel meansOfComputation)
        {
            Assume.That(JobsUtility.JobCompilerEnabled, Is.True);

            using (var set = new NodeSet())
            using (var forceBurstSyncCompile = new ForceBurstSyncCompilation(true))
            {
                set.RendererModel = meansOfComputation;

#if UNITY_EDITOR
                // Failure is only logged as an Error when run in Editor. Burst FunctionPointers do not work in non-Editor at all 
                // so those failures are currently logged as a Warning.

#if UNITY_2019_3_OR_NEWER

                // Only the first time a job is compiled in the current domain will a error be produced asynchronously 
                // through Burst. This means the error will be printed twice (once by us, once from Burst from somewhere).
                // Only happens on scheduling jobs, not compiling function pointers.
                if (forceBurstSyncCompile.IsFirstDomainCompilation)
                    LogAssert.Expect(LogType.Error, new Regex("Accessing the type"));

                LogAssert.Expect(LogType.Error, new Regex("Accessing the type"));
#endif

                LogAssert.Expect(LogType.Error, new Regex("Could not Burst compile"));
#endif

                var node = set.Create<InvalidForBurstKernelNodeWithIO>();

                set.Update();
                set.DataGraph.SyncAnyRendering();

                // Check the reflection data on the KernelFunction to identify if this is indeed the Managed version of
                // the kernel's compilation as opposed to the Bursted one.
                var nodes = set.GetInternalData();
                ref var llTraits = ref set.GetLLTraits()[nodes[node.VHandle.Index].TraitsIndex].Resolve();
                Assert.AreEqual(
                    llTraits.VTable.KernelFunction.ReflectionData,
                    RenderKernelFunction.GetManagedFunction<Data, InvalidForBurstKernelNodeWithIO.KernelDefs, InvalidForBurstKernelNodeWithIO.InvalidForBurstKernel>().ReflectionData
                );

                var knodes = set.DataGraph.GetInternalData();
                ref var inode = ref knodes[node.VHandle.Index];
                var ports = Unsafe.AsRef<InvalidForBurstKernelNodeWithIO.KernelDefs>(inode.KernelPorts);
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

                var nodes = set.GetInternalData();
                ref var llTraits = ref set.GetLLTraits()[nodes[node.VHandle.Index].TraitsIndex].Resolve();

                CompareSimpleTypes(new SimpleType(), llTraits.Storage.Kernel);
                CompareSimpleTypes(new SimpleType(), llTraits.Storage.KernelPorts);
                CompareSimpleTypes(new SimpleType(), llTraits.Storage.KernelData);
                CompareSimpleTypes(SimpleType.Create<Node>(), llTraits.Storage.NodeData);

                set.Destroy(node);
            }
        }

        [Test]
        public void LLDataPortCount_IsEquivalentToPortDeclaration()
        {
            using (var set = new NodeSet())
            {
                NodeHandle node = set.Create<KernelNodeWithIO>();
                var portDeclaration = set.GetFunctionality(node).GetPortDescription(node);
                var nodes = set.GetInternalData();
                ref var llTraits = ref set.GetLLTraits()[nodes[node.VHandle.Index].TraitsIndex].Resolve();

                Assert.AreEqual(portDeclaration.Inputs.Count(p => p.PortUsage == Usage.Data), llTraits.DataPorts.Inputs.Count);
                Assert.AreEqual(portDeclaration.Outputs.Count(p => p.PortUsage == Usage.Data), llTraits.DataPorts.Outputs.Count);

                set.Destroy(node);
            }
        }

        [Test]
        public void LLDataPortBufferOffsets_AreEquivalentToPortDeclaration()
        {
            using (var set = new NodeSet())
            {
                NodeHandle node = set.Create<KernelNodeWithIO>();
                var portDeclaration = set.GetFunctionality(node).GetPortDescription(node);
                var nodes = set.GetInternalData();
                ref var llTraits = ref set.GetLLTraits()[nodes[node.VHandle.Index].TraitsIndex].Resolve();

                int numDataBuffers = 0;
                foreach (var port in portDeclaration.Outputs)
                    numDataBuffers += port.BufferInfos.Count;

                var bufferOffsets = llTraits.DataPorts.OutputBufferOffsets;
                Assert.AreEqual(numDataBuffers, bufferOffsets.Count);

                foreach (var port in portDeclaration.Outputs)
                {
                    var portOffset =
                        UnsafeUtility.GetFieldOffset(typeof(KernelNodeWithIO.KernelDefs).GetField(port.Name));
                    foreach (var buf in port.BufferInfos)
                    {
                        for (int i = 0; i < bufferOffsets.Count; ++i)
                        {
                            if (buf.Offset + portOffset == bufferOffsets[i])
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
                var nodes = set.GetInternalData();
                ref var llTraits = ref set.GetLLTraits()[nodes[node.VHandle.Index].TraitsIndex].Resolve();

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
                var nodes = set.GetInternalData();
                ref var llTraits = ref set.GetLLTraits()[nodes[node.VHandle.Index].TraitsIndex].Resolve();

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

            using (var rawNodeDefinition = new KernelNodeWithIO())
            {
                LLTraitsHandle handle = new LLTraitsHandle();
                Assert.DoesNotThrow(() => handle = rawNodeDefinition.BaseTraits.CreateNodeTraits(rawNodeDefinition.GetType()));

                Assert.IsTrue(handle.IsCreated);

                handle.Dispose();

                Assert.Throws<ObjectDisposedException>(() => handle.Dispose());
            }
        }
    }

}
