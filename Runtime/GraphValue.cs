using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;

namespace Unity.DataFlowGraph
{
    /// <summary>
    /// A simple handle structure identifying a tap point in the graph.
    /// <seealso cref="NodeSetAPI.CreateGraphValue{T, TDefinition}(NodeHandle{TDefinition}, DataOutput{TDefinition, T})"/>
    /// </summary>
    /// <remarks>
    /// Note that a graph value never represents a copy of the value, it is simply a reference. Using
    /// <see cref="GraphValueResolver"/> you can directly read memory from inside the rendering without
    /// any copies.
    /// </remarks>
    [DebuggerDisplay("{Handle, nq}")]
    public readonly struct GraphValue<T>
    {
        internal readonly VersionedHandle Handle;

        internal GraphValue(VersionedHandle handle)
        {
            Handle = handle;
        }
    }

    public readonly struct GraphValueArray<T>
    {
        internal readonly VersionedHandle Handle;

        internal GraphValueArray(VersionedHandle handle)
        {
            Handle = handle;
        }
    }

    unsafe struct DataOutputValue : IVersionedItem
    {
        public bool Valid => Source != default;

        public ValidatedHandle Handle { get; set; }

        public ValidatedHandle Source;
        public DataPortDeclarations.OutputDeclaration* OutputDeclaration;

        public void Emplace<T>(in OutputPair source, in DataPortDeclarations.OutputDeclaration* outputDeclaration)
            where T : struct
        {
            Source = source.Handle;
            OutputDeclaration = outputDeclaration;
        }

        public void Dispose() => this = default;

        public ref readonly T UnsafeRead<T>(in RenderGraph.KernelNode kernelNode)
            where T : struct
        {
#if DFG_ASSERTIONS
            if (OutputDeclaration->IsArray)
                throw new AssertionException("Unexpected PortArray encountered while resolving a non PortArray GraphValue");
#endif
            var kernelPorts = kernelNode.Instance.Ports;
            return ref UnsafeUtility.AsRef<T>(OutputDeclaration->Resolve(kernelPorts, 0));
        }
    }

    /// <summary>
    /// A graph value resolver can resolve the state of an output port pointed to by a <see cref="GraphValue{T}"/>.
    /// It can be burst compiled, used concurrently on a job or on the main thread, so long as the dependencies are
    /// resolved.
    ///
    /// API on this object is a subset of what is available on <see cref="RenderContext"/>
    /// </summary>
    /// <see cref="NodeSetAPI.GetGraphValueResolver(out JobHandle)"/>
    public struct GraphValueResolver
    {
        [NativeDisableUnsafePtrRestriction]
        // This manager needs to come from the render graph, to ensure the stuff it resolves decays properly
        // when it's no longer needed (and also produce the nice error messages)
        unsafe internal AtomicSafetyManager* Manager;

        [ReadOnly]
        // This being a native list ensures user cannot schedule their graph value jobs incorrectly against each other
        internal NativeList<DataOutputValue> Values;

        [ReadOnly]
        internal AtomicSafetyManager.BufferProtectionScope ReadBuffersScope;

        // TODO: This list is only used for secondary version invalidation,
        // but could in fact make most entries in DataOutputValue redundant
        [ReadOnly]
        internal BlitList<RenderGraph.KernelNode> KernelNodes;

        /// <summary>
        /// Returns the contents of the output port the graph value was originally specified to point to.
        /// <seealso cref="NodeSetAPI.CreateGraphValue{T, TDefinition}(NodeHandle{TDefinition}, DataOutput{TDefinition, T})"/>
        /// <seealso cref="RenderContext"/>
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown if either the graph value or the pointed-to node is no longer valid.</exception>
        public T Resolve<T>(GraphValue<T> handle)
            where T : struct
        {
            // TODO: Change return value to ref readonly when Burst supports it
            ReadBuffersScope.CheckReadAccess();

            // Primary invalidation check
            if (handle.Handle.Index >= Values.Length || handle.Handle.Version != Values[handle.Handle.Index].Handle.Versioned.Version)
            {
                throw new ObjectDisposedException("GraphValue is either disposed, invalid or hasn't yet roundtripped through the render graph");
            }

            var value = Values[handle.Handle.Index];

            // Secondary invalidation check
            if (!RenderGraph.StillExists(ref KernelNodes, value.Source))
            {
                throw new ObjectDisposedException("GraphValue's target node handle is disposed or invalid");
            }

            return value.UnsafeRead<T>(KernelNodes[value.Source.Versioned.Index]);
        }

        /// <summary>
        /// Returns the contents of the output port array the graph value was originally specified to point to.
        /// <seealso cref="NodeSet.CreateGraphValueArray{T,TDefinition}"/>
        /// <seealso cref="RenderContext"/>
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown if either the graph value or the pointed-to node is no longer valid.</exception>
        public NativeSlice<T> Resolve<T>(GraphValueArray<T> handle)
            where T : struct
        {
            ReadBuffersScope.CheckReadAccess();

            // Primary invalidation check
            if (handle.Handle.Index >= Values.Length || handle.Handle.Version != Values[handle.Handle.Index].Handle.Versioned.Version)
            {
                throw new ObjectDisposedException("GraphValue is disposed or invalid");
            }

            var value = Values[handle.Handle.Index];

            // Secondary invalidation check
            if (!RenderGraph.StillExists(ref KernelNodes, value.Source))
            {
                throw new ObjectDisposedException("GraphValue's target node handle is disposed or invalid");
            }

            ref var knode = ref KernelNodes[value.Source.Versioned.Index];

            unsafe
            {
                ref readonly var array = ref value.OutputDeclaration->AsPortArray(knode.Instance.Ports);
                var size = OutputPortArrayExt.PortArrayDetails<T>.ElementType;
                var ptr = array.Get(size, 0);
                var ret = NativeSliceUnsafeUtility.ConvertExistingDataToNativeSlice<T>(ptr, size.Size, array.Size);
                Manager->MarkNativeSliceAsReadOnly(ref ret);
                return ret;
            }
        }

        /// <summary>
        /// Returns the NativeArray representation of the buffer output port the graph value was originally specified to point to.
        /// </summary>
        /// <remarks>
        /// This is a convenience method for <see cref="Resolve{T}(GraphValue{T})"/> for directly accessing
        /// the NativeArray representation of a <see cref="DataOutput{TDefinition, T}"/> of type <see cref="Buffer{T}"/>
        /// </remarks>
        public NativeArray<T> Resolve<T>(GraphValue<Buffer<T>> handle)
            where T : struct
        {
            return Resolve<Buffer<T>>(handle).ToNative(this);
        }

        internal NativeArray<T> Resolve<T>(Buffer<T> buffer)
            where T : struct
        {
            // TODO: A malicious user could store stale buffers around (ie. previously partly resolved from another resolver),
            // and successfully resolve them here.
            // To solve this we need to version buffers as well, but it is an edge case.
            if (!RenderGraph.StillExists(ref KernelNodes, buffer.OwnerNode))
                throw new ObjectDisposedException("GraphValue's target node handle is disposed or invalid");

            ReadBuffersScope.CheckReadAccess();

            unsafe
            {
                var ret = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(buffer.Ptr, buffer.Size, Allocator.Invalid);
                Manager->MarkNativeArrayAsReadOnly(ref ret);
                return ret;
            }
        }

        internal unsafe bool IsValid => Manager != null;
    }

    public partial class NodeSetAPI
    {
        VersionedList<DataOutputValue> m_GraphValues;

        /// <summary>
        /// Graph values can exist in two states:
        /// 1) just created
        /// 2) post one render, after which they get initialized with job fences and memory references.
        ///
        /// To safely read data back from them, GraphValueResolver will only accept graph values
        /// that was created before the last current render.
        /// </summary>
        NativeList<DataOutputValue> m_PostRenderValues = new NativeList<DataOutputValue>(Allocator.Persistent);
        (GraphValueResolver Resolver, JobHandle Dependency) m_CurrentGraphValueResolver;
        BlitList<JobHandle> m_ReaderFences = new BlitList<JobHandle>(0, Allocator.Persistent);

        /// <summary>
        /// Creates a tap point at a specific output port location in the graph. Using graph values you can read back state and
        /// results from graph kernels, either from the main thread using <see cref="GetValueBlocking{T}(GraphValue{T})"/>
        /// or asynchronously using <see cref="GraphValueResolver"/>.
        /// <seealso cref="GetGraphValueResolver(out JobHandle)"/>
        /// <seealso cref="IKernelPortDefinition"/>
        /// <seealso cref="IGraphKernel{TKernelData, TKernelPortDefinition}"/>
        /// </summary>
        /// <remarks>
        /// You will not be able to read contents of the output until after the next issue of <see cref="Update()"/>.
        /// </remarks>
        /// <exception cref="ArgumentException">Thrown if the target node is invalid or disposed</exception>
        /// <exception cref="IndexOutOfRangeException">Thrown if the output port is invalid</exception>
        public GraphValue<T> CreateGraphValue<T, TDefinition>(NodeHandle<TDefinition> node, DataOutput<TDefinition, T> output)
            where TDefinition : NodeDefinition
            where T : struct
        {
            return new GraphValue<T>(CreateGraphValueStrong<T>(new OutputPair(this, node, new OutputPortArrayID(output.Port))));
        }

        /// <summary>
        /// Creates a tap point at a specific output port array location in the graph. Using graph values you can read back state and
        /// results from graph kernels asynchronously using <see cref="GraphValueResolver"/>.
        /// <seealso cref="GetGraphValueResolver(out JobHandle)"/>
        /// <seealso cref="IKernelPortDefinition"/>
        /// <seealso cref="IGraphKernel{TKernelData, TKernelPortDefinition}"/>
        /// </summary>
        /// <remarks>
        /// You will not be able to read contents of the output until after the next issue of <see cref="Update()"/>.
        /// </remarks>
        /// <exception cref="ArgumentException">Thrown if the target node is invalid or disposed</exception>
        /// <exception cref="IndexOutOfRangeException">Thrown if the output port is invalid</exception>
        public GraphValueArray<T> CreateGraphValueArray<T, TDefinition>(NodeHandle<TDefinition> node, PortArray<DataOutput<TDefinition, T>> output)
            where TDefinition : NodeDefinition
            where T : struct
        {
            return new GraphValueArray<T>(
                CreateGraphValueStrong<T>(new OutputPair(this, node, new OutputPortArrayID(output.GetPortID()))));
        }

        /// <summary>
        /// Creates a graph value from an untyped node and source port.
        /// See documentation for <see cref="CreateGraphValue{T, TDefinition}(NodeHandle{TDefinition}, DataOutput{TDefinition, T})"/>.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown if the target node is invalid or disposed</exception>
        /// <exception cref="InvalidOperationException">Thrown if the output port is not of a data type</exception>
        /// <exception cref="IndexOutOfRangeException">Thrown if the output port is out of bounds</exception>
        public GraphValue<T> CreateGraphValue<T>(NodeHandle handle, OutputPortID output)
            where T : struct
        {
            return new GraphValue<T>(CreateGraphValueWeak<T>(new OutputPair(this, handle, new OutputPortArrayID(output)), false));
        }

        /// <summary>
        /// Creates a graph value from an untyped node and source port array.
        /// See documentation for <see cref="CreateGraphValueArray{T,TDefinition}"/>.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown if the target node is invalid or disposed</exception>
        /// <exception cref="InvalidOperationException">Thrown if the output port is not of a data type</exception>
        /// <exception cref="IndexOutOfRangeException">Thrown if the output port is out of bounds</exception>
        public GraphValueArray<T> CreateGraphValueArray<T>(NodeHandle handle, OutputPortID output)
            where T : struct
        {
            return new GraphValueArray<T>(CreateGraphValueWeak<T>(new OutputPair(this, handle, new OutputPortArrayID(output)), true));
        }

        /// <summary>
        /// Releases a graph value previously created with <see cref="CreateGraphValue{T,TDefinition}"/>.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown if the graph value is invalid or disposed</exception>
        public void ReleaseGraphValue<T>(GraphValue<T> graphValue)
        {
            ref readonly var gv = ref m_GraphValues[graphValue.Handle];
            m_Diff.GraphValueDestroyed(gv);
            m_GraphValues.Release(gv);
        }

        /// <summary>
        /// Releases a graph value previously created with <see cref="CreateGraphValueArray{T,TDefinition}"/>.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown if the graph value is invalid or disposed</exception>
        public void ReleaseGraphValueArray<T>(GraphValueArray<T> graphValue)
            => m_GraphValues.Release(graphValue.Handle);

        /// <summary>
        /// Fetches the last value from a node's <see cref="DataOutput{TDefinition,TType}"/> via a previously created graph
        /// value (see <see cref="CreateGraphValue{T,TDefinition}"/>).
        /// </summary>
        /// <exception cref="ArgumentException">Thrown if the graph value is invalid or disposed</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the referenced node has been destroyed</exception>
        /// <remarks>
        /// This blocks execution in the calling thread until the last rendering of the given node has finished.
        /// If non-blocking behavior is desired, consider using a job in combination with a
        /// <see cref="GraphValueResolver"/>.
        /// Default value of <typeparamref name="T"/> is returned if an <see cref="Update"/> has not been issued since the
        /// the <paramref name="graphValue"/> was created.
        /// </remarks>
        public T GetValueBlocking<T>(GraphValue<T> graphValue)
            where T : struct
        {
            ref readonly var value = ref m_GraphValues[graphValue.Handle];

            if (!Nodes.StillExists(value.Source))
                throw new ObjectDisposedException("The node that the graph value refers to is destroyed");

            // Check whether the graph value did roundtrip - if it didn't, return a default value.
            // Note that in the similar situation, the GraphValueResolver will throw an exception (it can't disambiguate).
            if (value.Handle.Versioned.Index >= m_PostRenderValues.Length || value.Handle.Versioned != m_PostRenderValues[value.Handle.Versioned.Index].Handle.Versioned)
                return default;

            DataGraph.ComputeDependency(value).Complete();

            return value.UnsafeRead<T>(DataGraph.GetInternalData()[value.Source.Versioned.Index]);
        }

        /// <summary>
        /// Tests whether <paramref name="graphValue"/> exists.
        /// </summary>
        public bool ValueExists<T>(GraphValue<T> graphValue)
        {
            return m_GraphValues.Exists(graphValue.Handle);
        }

        /// <summary>
        /// Tests whether <paramref name="graphValue"/>'s target still exists.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// If the <paramref name="graphValue"/> is invalid, or comes from another <see cref="NodeSet"/>
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// If the <paramref name="graphValue"/> no longer exists.
        /// <see cref="ValueExists{T}(GraphValue{T})"/>
        /// </exception>
        public bool ValueTargetExists<T>(GraphValue<T> graphValue)
        {
            return Nodes.StillExists(m_GraphValues[graphValue.Handle].Source);
        }

        protected void InjectDependencyFromConsumerInternal(JobHandle handle)
        {
            // TODO: Could immediately try to schedule a dependent job here (MarkBuffersAsUsed)
            // to make deferred errors immediate here in case of bad job handles.
            m_ReaderFences.Add(handle);
        }

        protected GraphValueResolver GetGraphValueResolverInternal(out JobHandle resultDependency)
        {
            // TODO: Here we take dependency on every graph value in the graph.
            // We could make a "filter" or similar.
            if (!m_CurrentGraphValueResolver.Resolver.IsValid)
                m_CurrentGraphValueResolver = DataGraph.CombineAndProtectDependencies(m_PostRenderValues);
            resultDependency = m_CurrentGraphValueResolver.Dependency;
            return m_CurrentGraphValueResolver.Resolver;
        }

        VersionedHandle CreateGraphValueStrong<T>(in OutputPair source)
            where T : struct
        {
            // To ensure the port actually exists.
            GetFormalPort(source);

            return CreateGraphValue<T>(source);
        }

        VersionedHandle CreateGraphValueWeak<T>(in OutputPair source, bool forPortArray)
            where T : struct
        {
            var portDef = GetFormalPort(source);

            if (portDef.IsPortArray != forPortArray)
                throw new InvalidOperationException(portDef.IsPortArray
                    ? "Cannot create a GraphValueArray for a port which is not an array (use a GraphValue instead)"
                    : "Cannot create a GraphValue for a port array (use a GraphValueArray instead)");

            if (portDef.Category != PortDescription.Category.Data)
                throw new InvalidOperationException($"Graph values can only point to data outputs");

            if (portDef.Type != typeof(T))
                throw new InvalidOperationException($"Cannot create a graph value of type {typeof(T)} pointing to a data output of type {portDef.Type}");

            return CreateGraphValue<T>(source);
        }

        unsafe VersionedHandle CreateGraphValue<T>(OutputPair source)
            where T : struct
        {
            ref readonly var traits = ref m_Traits[Nodes[source.Handle].TraitsIndex].Resolve();
            ref readonly var outputDeclaration = ref traits.DataPorts.FindOutputDataPort(source.Port.PortID);

            ref var value = ref m_GraphValues.Allocate();
            value.Emplace<T>(source, (DataPortDeclarations.OutputDeclaration*)UnsafeUtilityExtensions.AddressOf(outputDeclaration));

            m_Diff.GraphValueCreated(value);
            return value.Handle.Versioned;
        }

        unsafe void FenceOutputConsumers()
        {
            var readerJobs = JobHandleUnsafeUtility.CombineDependencies(m_ReaderFences.Pointer, m_ReaderFences.Count);
            readerJobs.Complete();
            m_ReaderFences.Clear();
            // TODO: Maybe have an early check for writability on m_PostRenderValues here.
        }

        unsafe void SwapGraphValues()
        {
            // If this throws, then any input job handles were bad.
            m_GraphValues.CopyUnvalidatedTo(m_PostRenderValues);
            m_CurrentGraphValueResolver = default;
        }

        internal VersionedList<DataOutputValue> GetOutputValues()
        {
            return m_GraphValues;
        }
    }
}
