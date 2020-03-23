using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

namespace Unity.DataFlowGraph
{
    /// <summary>
    /// Interface tag to be implemented on a struct, that will contain instance data on your node that is read/write
    /// accessible on the simulation-side (see <see cref="NodeDefinition{TNodeData, TSimulationportDefinition, TKernelData, TKernelPortDefinition, TKernel}.GetKernelData"/>)
    /// and made available read-only in the node's kernel on the rendering-side (see <see cref="IGraphKernel{TKernelData,TKernelPortDefinition}.Execute"/>).
    /// </summary>
    public interface IKernelData { }

    /// <summary>
    /// Interface tag to be implemented on a struct, that will contain the 
    /// the node definition's kernel port declarations.
    /// <seealso cref="DataInput{TDefinition,TType}"/>
    /// <seealso cref="DataOutput{TDefinition,TType}"/>
    /// </summary>
    public interface IKernelPortDefinition { }

    public interface IGraphKernel { };

    /// <summary>
    /// Interface to be implemented on a struct which represents the functor responsible for processing a node's
    /// <see cref="DataInput{TDefinition,TType}"/>s and filling out its <see cref="DataOutput{TDefinition,TType}"/>s
    /// each time <see cref="NodeSet.Update"/> is invoked. The functor is tied to a given node type by specifying it
    /// in the <see cref="NodeDefinition{TNodeData, TSimulationportDefinition, TKernelData, TKernelPortDefinition, TKernel}"/>.
    ///
    /// Any fields which exist in the struct are preserved between invocations of <see cref="IGraphKernel{TKernelData,TKernelPortDefinition}.Execute"/>
    /// and represent the ongoing state of the node's kernel.
    ///
    /// Implementing structs may be tagged with the <see cref="BurstCompileAttribute"/> to take advantage of improved performance.
    /// </summary>
    [JobProducerType(typeof(GraphKernel<,,>))]
    public interface IGraphKernel<TKernelData, TKernelPortDefinition> : IGraphKernel
       where TKernelPortDefinition : IKernelPortDefinition
       where TKernelData : IKernelData
    {
        void Execute(RenderContext ctx, TKernelData data, ref TKernelPortDefinition ports);
    }

    unsafe struct RenderKernelFunction : IVirtualFunctionDeclaration
    {
        internal delegate void InvokeDelegate(ref RenderContext ctx, in KernelLayout.Pointers instance);
        public IntPtr ReflectionData => JobData;
        internal readonly IntPtr JobData;
        internal readonly FunctionPointer<InvokeDelegate> FunctionPointer;

        internal struct BaseData { }
        internal struct BaseKernel { }
        internal struct BasePort { }
        
        internal static RenderKernelFunction GetBurstedFunction<TKernelData, TKernelPortDefinition, TUserKernel>()
           where TKernelData : struct, IKernelData
           where TKernelPortDefinition : struct, IKernelPortDefinition
           where TUserKernel : struct, IGraphKernel<TKernelData, TKernelPortDefinition>
        {
            FunctionPointer<InvokeDelegate> functionPointer;
            try
            {
                functionPointer = BurstCompiler.CompileFunctionPointer<InvokeDelegate>(GraphKernel<TUserKernel, TKernelData, TKernelPortDefinition>.Execute);
            }
            catch (InvalidOperationException exception)
            {
                Debug.LogError(
                    $"Could not Burst compile {typeof(TUserKernel)}, falling back to non-Bursted compilation\n{exception}");
                return GetManagedFunction<TKernelData, TKernelPortDefinition, TUserKernel>();
            }
            // TODO: uncomment when issue #229 is fixed
            // var reflectionData = IGraphNodeExecutorExtensions.JobStruct<GraphKernel<TUserKernel, TKernelData, TKernelPortDefinition>.Bursted>.JobReflectionData;
            var reflectionData = IGraphNodeExecutorExtensions.JobStruct<BurstedKernelWrapper>.JobReflectionData;
            return new RenderKernelFunction(reflectionData, functionPointer);
        }

        internal static RenderKernelFunction GetManagedFunction<TKernelData, TKernelPortDefinition, TUserKernel>()
            where TKernelData : struct, IKernelData
            where TKernelPortDefinition : struct, IKernelPortDefinition
            where TUserKernel : struct, IGraphKernel<TKernelData, TKernelPortDefinition>
        {
            // TODO: uncomment when issue #229 is fixed
            // var reflectionData = IGraphNodeExecutorExtensions.JobStruct<GraphKernel<TUserKernel, TKernelData, TKernelPortDefinition>.Managed>.JobReflectionData;
            var reflectionData = IGraphNodeExecutorExtensions.JobStruct<ManagedKernelWrapper>.JobReflectionData;
            var functionPointer = Marshal.GetFunctionPointerForDelegate(GraphKernel<TUserKernel, TKernelData, TKernelPortDefinition>.RetainedInvocation);
            return new RenderKernelFunction(reflectionData, new FunctionPointer<InvokeDelegate>(functionPointer));
        }

        internal static RenderKernelFunction Pure<TPureKernel>(IntPtr pureJobReflectionData)
            where TPureKernel : struct, IGraphNodeExecutor
        {
            var functionPointer = Marshal.GetFunctionPointerForDelegate(PureCallRetainer<TPureKernel>.RetainedInvocation);

            return new RenderKernelFunction(
                pureJobReflectionData,
                new FunctionPointer<InvokeDelegate>(functionPointer)
            );
        }

        RenderKernelFunction(IntPtr jobReflectionData, FunctionPointer<InvokeDelegate> functionPointer)
        {
            JobData = jobReflectionData;
            FunctionPointer = functionPointer;
        }

        internal struct AliasedJobDefinition
        {
            public KernelLayout.Pointers Instance;
            public RenderContext RenderContext;
            public FunctionPointer<InvokeDelegate> Function;
        }

        internal JobHandle Schedule(JobHandle inputDependencies, in RenderContext ctx, in KernelLayout.Pointers instance)
        {
            var job = new AliasedJobDefinition { Instance = instance, RenderContext = ctx, Function = FunctionPointer };
            var scheduleParams = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref job), JobData, inputDependencies, ScheduleMode.Batched);
            return JobsUtility.Schedule(ref scheduleParams);
        }

        internal void Invoke(RenderContext ctx, in KernelLayout.Pointers instance)
        {
            FunctionPointer.Invoke(ref ctx, instance);
        }

        struct PureCallRetainer<TPure>
            where TPure : struct, IGraphNodeExecutor
        {
            public static InvokeDelegate RetainedInvocation = Pure<TPure>;
        }

        [AOT.MonoPInvokeCallback (typeof(InvokeDelegate))]
        static void Pure<TPure>(ref RenderContext ctx, in KernelLayout.Pointers instance)
            where TPure : struct, IGraphNodeExecutor
        {
            new TPure().Execute();
        }
    }

    [BurstCompile]
    unsafe struct GraphKernel<TUserKernel, TKernelData, TKernelPortDefinition>
       where TKernelData : struct, IKernelData
       where TKernelPortDefinition : struct, IKernelPortDefinition
       where TUserKernel : struct, IGraphKernel<TKernelData, TKernelPortDefinition>
    {
        internal static readonly RenderKernelFunction.InvokeDelegate RetainedInvocation = Execute;

        [BurstCompile]
        [AOT.MonoPInvokeCallback (typeof(RenderKernelFunction.InvokeDelegate))]
        public static void Execute(ref RenderContext ctx, in KernelLayout.Pointers instance)
        {
            Unsafe.AsRef<TUserKernel>(instance.Kernel).Execute(ctx, Unsafe.AsRef<TKernelData>(instance.Data), ref Unsafe.AsRef<TKernelPortDefinition>(instance.Ports));
        }

        /* TODO: Uncomment once issue #229 is fixed
        [BurstCompile]
        internal struct Bursted : IGraphNodeExecutor
        {
            [NativeDisableUnsafePtrRestriction]
            internal RenderKernelFunction.BaseKernel* m_Kernel;
            [NativeDisableUnsafePtrRestriction]
            internal RenderKernelFunction.BaseData* m_Data;
            [NativeDisableUnsafePtrRestriction]
            internal RenderKernelFunction.BasePort* m_Ports;

            internal RenderContext m_RenderContext;

            public void Execute() => GraphKernel<TUserKernel, TKernelData, TKernelPortDefinition>.Execute(ref m_RenderContext, m_Kernel, m_Data, m_Ports);
        }

        internal struct Managed : IGraphNodeExecutor
        {
            [NativeDisableUnsafePtrRestriction]
            internal RenderKernelFunction.BaseKernel* m_Kernel;
            [NativeDisableUnsafePtrRestriction]
            internal RenderKernelFunction.BaseData* m_Data;
            [NativeDisableUnsafePtrRestriction]
            internal RenderKernelFunction.BasePort* m_Ports;

            internal RenderContext m_RenderContext;

            public void Execute() => GraphKernel<TUserKernel, TKernelData, TKernelPortDefinition>.Execute(ref m_RenderContext, m_Kernel, m_Data, m_Ports);
        }
        */
    }

    unsafe struct ManagedKernelWrapper : IGraphNodeExecutor
    {
        public KernelLayout.Pointers Instance;
        public RenderContext m_RenderContext;

        FunctionPointer<RenderKernelFunction.InvokeDelegate> Function;

        public void Execute() => Function.Invoke(ref m_RenderContext, Instance);
    }

    [BurstCompile]
    unsafe struct BurstedKernelWrapper : IGraphNodeExecutor
    {
        public KernelLayout.Pointers Instance;
        public RenderContext m_RenderContext;

        FunctionPointer<RenderKernelFunction.InvokeDelegate> Function;

        public void Execute() => Function.Invoke(ref m_RenderContext, Instance);
    }
}
