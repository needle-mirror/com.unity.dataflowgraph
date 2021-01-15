using System;
using System.ComponentModel;
using Unity.Collections;

namespace Unity.DataFlowGraph
{
    namespace Detail
    {
        /// <summary>
        /// A common base interface shared by all simulation callback contexts which allows them to share a common API
        /// through the use of extension methods. User code should use <see cref="CommonContext"/> for local implementation
        /// of helper methods rather than being written against this interface.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public interface IContext<TSelf>
        {
            /// <summary>
            /// A handle to the node being operated on.
            /// </summary>
            NodeHandle Handle { get; }

            /// <summary>
            /// The <see cref="NodeSetAPI"/> associated with this context.
            /// </summary>
            NodeSetAPI Set { get; }
        }
    }

    /// <summary>
    /// The common portion of the API which appears on all contexts.
    /// Instances of <see cref="InitContext"/>, <see cref="MessageContext"/>, and <see cref="UpdateContext"/> can all be
    /// implicitly cast to this common context.
    /// </summary>
    public readonly struct CommonContext : Detail.IContext<CommonContext>
    {
        readonly ValidatedHandle m_Handle;

        /// <summary>
        /// A handle to the node being operated on.
        /// </summary>
        public NodeHandle Handle => m_Handle.ToPublicHandle();

        /// <summary>
        /// The <see cref="NodeSetAPI"/> associated with this context.
        /// </summary>
        public NodeSetAPI Set { get; }

        internal CommonContext(NodeSetAPI set, in ValidatedHandle handle)
        {
            Set = set;
            m_Handle = handle;
        }
    }

    public static partial class CommonContextAPI
    {
        /// <summary>
        /// Emit a message from yourself on a port. Everything connected to it will receive your message.
        /// </summary>
        public static void EmitMessage<TContext, T, TNodeDefinition>(ref this TContext self, MessageOutput<TNodeDefinition, T> port, in T msg)
            where TContext : struct, Detail.IContext<TContext>
            where TNodeDefinition : NodeDefinition
                => self.Set.EmitMessage(self.InternalHandle(), new OutputPortArrayID(port.Port), msg);

        /// <summary>
        /// Emit a message from yourself on a port array. Everything connected to it will receive your message.
        /// </summary>
        /// <exception cref="IndexOutOfRangeException">Thrown if the index is out of range with respect to the port array.</exception>
        public static void EmitMessage<TContext, T, TNodeDefinition>(ref this TContext self, PortArray<MessageOutput<TNodeDefinition, T>> port, int arrayIndex, in T msg)
            where TContext : struct, Detail.IContext<TContext>
            where TNodeDefinition : NodeDefinition
                => self.Set.EmitMessage(self.InternalHandle(), new OutputPortArrayID(port.GetPortID(), arrayIndex), msg);

        /// <summary>
        /// Updates the contents of <see cref="Buffer{T}"/>s appearing in this node's <see cref="IGraphKernel{TKernelData,TKernelPortDefinition}"/>.
        /// Pass an instance of the node's <see cref="IGraphKernel{TKernelData,TKernelPortDefinition}"/> as the <paramref name="requestedContents"/>
        /// parameter with <see cref="Buffer{T}"/> instances within it having been set using <see cref="UploadRequest"/>, or
        /// <see cref="Buffer{T}.SizeRequest(int)"/>.
        /// Any <see cref="Buffer{T}"/> instances within the given struct that have default values will be unaffected by the call.
        /// </summary>
        public static void UpdateKernelBuffers<TContext, TGraphKernel>(ref this TContext self, in TGraphKernel requestedSize)
            where TContext : struct, Detail.IContext<TContext>
            where TGraphKernel : struct, IGraphKernel
                => self.Set.UpdateKernelBuffers(self.InternalHandle(), requestedSize);

        /// <summary>
        /// The return value should be used together with <see cref="UpdateKernelBuffers{TContext,TGraphKernel}"/> to change the contents
        /// of a kernel buffer living on a <see cref="IGraphKernel{TKernelData, TKernelPortDefinition}"/>.
        /// </summary>
        /// <remarks>
        /// This will resize the affected buffer to the same size as <paramref name="inputMemory"/>.
        /// Failing to include the return value in a call to <see cref="UpdateKernelBuffers{TContext,TGraphKernel"/> is an error and will result in a memory leak.
        /// </remarks>
        public static Buffer<T> UploadRequest<TContext, T>(ref this TContext self, NativeArray<T> inputMemory, BufferUploadMethod method = BufferUploadMethod.Copy)
            where TContext : struct, Detail.IContext<TContext>
            where T : struct
                => self.Set.UploadRequest(self.InternalHandle(), inputMemory, method);

        /// <summary>
        /// Updates the associated <typeparamref name="TKernelData"/> asynchronously,
        /// to be available in a <see cref="IGraphKernel"/> in the next render.
        /// </summary>
        public static void UpdateKernelData<TContext, TKernelData>(ref this TContext self, in TKernelData data)
            where TContext : struct, Detail.IContext<TContext>
            where TKernelData : struct, IKernelData
                => self.Set.UpdateKernelData(self.InternalHandle(), data);

        /// <summary>
        /// Registers the current node for regular updates every time <see cref="NodeSet.Update()"/> is called.
        /// This only takes effect after the next <see cref="NodeSet.Update()"/>.
        /// <seealso cref="IUpdate.Update(in UpdateContext)"/>
        /// <seealso cref="RemoveFromUpdate{TContext}"/>
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
        public static void RegisterForUpdate<TContext>(ref this TContext self)
            where TContext : struct, Detail.IContext<TContext>
                => self.Set.RegisterForUpdate(self.InternalHandle());

        /// <summary>
        /// Deregisters the current node from updating every time <see cref="NodeSet.Update()"/> is called.
        /// This only takes effect after the next <see cref="NodeSet.Update()"/>.
        /// <seealso cref="RegisterForUpdate{TContext}"/>
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the current node is not registered for updating.
        /// </exception>
        public static void RemoveFromUpdate<TContext>(ref this TContext self)
            where TContext : struct, Detail.IContext<TContext>
                => self.Set.RemoveFromUpdate(self.InternalHandle());

        /// <summary>
        /// Contexts are always initialized by ValidatedHandle, it is thus safe to bypass the actual validation step when
        /// presenting their contained NodeHandle as a ValidatedHandle.
        /// </summary>
        internal static ValidatedHandle InternalHandle<TContext>(ref this TContext self)
            where TContext : struct, Detail.IContext<TContext>
                => ValidatedHandle.Create_FromPrevalidatedContextHandle(self.Handle.VHandle);
    }
}
