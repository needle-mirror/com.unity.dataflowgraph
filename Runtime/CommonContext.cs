using System;
using Unity.Collections;

namespace Unity.DataFlowGraph
{
    /// <summary>
    /// The common portion of the API which appears on all contexts.
    /// Instances of <see cref="InitContext"/>, <see cref="MessageContext"/>, and <see cref="UpdateContext"/> can all be
    /// implicitly cast to this common context.
    /// </summary>
    public readonly partial struct CommonContext
    {
        /// <summary>
        /// A handle uniquely identifying the current node.
        /// </summary>
        public NodeHandle Handle => InternalHandle.ToPublicHandle();

        /// <summary>
        /// The <see cref="NodeSetAPI"/> associated with this context.
        /// </summary>
        public readonly NodeSetAPI Set;

        internal readonly ValidatedHandle InternalHandle;

        /// <summary>
        /// Emit a message from yourself on a port. Everything connected to it will receive your message.
        /// </summary>
        public void EmitMessage<T, TNodeDefinition>(MessageOutput<TNodeDefinition, T> port, in T msg)
            where TNodeDefinition : NodeDefinition
        {
            Set.EmitMessage(InternalHandle, new OutputPortArrayID(port.Port), msg);
        }

        /// <summary>
        /// Emit a message from yourself on a port array. Everything connected to it will receive your message.
        /// </summary>
        /// <exception cref="IndexOutOfRangeException">Thrown if the index is out of range with respect to the port array.</exception>
        public void EmitMessage<T, TNodeDefinition>(PortArray<MessageOutput<TNodeDefinition, T>> port, int arrayIndex, in T msg)
            where TNodeDefinition : NodeDefinition
        {
            Set.EmitMessage(InternalHandle, new OutputPortArrayID(port.GetPortID(), arrayIndex), msg);
        }

        /// <summary>
        /// Set the size of a <see cref="Buffer{T}"/> appearing in this node's <see cref="IGraphKernel{TKernelData,TKernelPortDefinition}"/>.
        /// Pass an instance of the node's <see cref="IGraphKernel{TKernelData,TKernelPortDefinition}"/> as the <paramref name="requestedSize"/>
        /// parameter with <see cref="Buffer{T}"/> instances within it having been set using <see cref="Buffer{T}.SizeRequest(int)"/>.
        /// Any <see cref="Buffer{T}"/> instances within the given struct that have not been set using
        /// <see cref="Buffer{T}.SizeRequest(int)"/> will be unaffected by the call.
        /// </summary>
        public void UpdateKernelBuffers<TGraphKernel>(in TGraphKernel requestedSize)
            where TGraphKernel : struct, IGraphKernel
        {
            Set.UpdateKernelBuffers(InternalHandle, requestedSize);
        }

        /// <summary>
        /// The return value should be used together with <see cref="UpdateKernelBuffers"/> to change the contents
        /// of a kernel buffer living on a <see cref="IGraphKernel{TKernelData, TKernelPortDefinition}"/>.
        /// </summary>
        /// <remarks>
        /// This will resize the affected buffer to the same size as <paramref name="inputMemory"/>.
        /// Failing to include the return value in a call to <see cref="UpdateKernelBuffers"/> is an error and will result in a memory leak.
        /// </remarks>
        public Buffer<T> UploadRequest<T>(NativeArray<T> inputMemory, BufferUploadMethod method = BufferUploadMethod.Copy)
            where T : struct
                => Set.UploadRequest(InternalHandle, inputMemory, method);

        /// <summary>
        /// Updates the associated <typeparamref name="TKernelData"/> asynchronously,
        /// to be available in a <see cref="IGraphKernel"/> in the next render.
        /// </summary>
        public void UpdateKernelData<TKernelData>(in TKernelData data)
            where TKernelData : struct, IKernelData
        {
            Set.UpdateKernelData(InternalHandle, data);
        }

        /// <summary>
        /// Registers the current node for regular updates every time <see cref="NodeSet.Update"/> is called.
        /// This only takes effect after the next <see cref="NodeSet.Update"/>.
        /// <seealso cref="IUpdate.Update(in UpdateContext)"/>
        /// <seealso cref="RemoveFromUpdate()"/>
        /// </summary>
        /// <remarks>
        /// A node will automatically be removed from the update list when it is destroyed.
        /// </remarks>
        /// <exception cref="InvalidNodeDefinitionException">
        /// Will be thrown if the current node does not support updating.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the current node is already registered for updating.
        /// </exception>
        public void RegisterForUpdate() => Set.RegisterForUpdate(InternalHandle);

        /// <summary>
        /// Deregisters the current node from updating every time <see cref="NodeSet.Update"/> is called.
        /// This only takes effect after the next <see cref="NodeSet.Update"/>.
        /// <seealso cref="RegisterForUpdate()"/>
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown if <see cref="Handle"/> is not registered for updating.
        /// </exception>
        public void RemoveFromUpdate() => Set.RemoveFromUpdate(InternalHandle);

        internal CommonContext(NodeSetAPI set, in ValidatedHandle handle)
        {
            Set = set;
            InternalHandle = handle;
        }
    }
}
