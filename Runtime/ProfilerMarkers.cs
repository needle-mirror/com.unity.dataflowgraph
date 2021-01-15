using System;
using Unity.Profiling;

namespace Unity.DataFlowGraph
{
    partial class RenderGraph : IDisposable
    {
        internal static class Markers
        {
            // Control thread
            public static ProfilerMarker SyncPreviousRenderProfilerMarker = new ProfilerMarker("NodeSet.RenderGraph.SyncPreviousRender");
            public static ProfilerMarker AlignWorldProfilerMarker = new ProfilerMarker("NodeSet.RenderGraph.AlignWorld");
            public static ProfilerMarker PrepareGraphProfilerMarker = new ProfilerMarker("NodeSet.RenderGraph.PrepareGraph");
            public static ProfilerMarker RenderWorldProfilerMarker = new ProfilerMarker("NodeSet.RenderGraph.RenderWorld");
            public static ProfilerMarker PostScheduleTasksProfilerMarker = new ProfilerMarker("NodeSet.RenderGraph.PostScheduleTasks");
            public static ProfilerMarker FinalizeParallelTasksProfilerMarker = new ProfilerMarker("NodeSet.RenderGraph.FinalizeParallelTasks");
            public static ProfilerMarker WaitForSchedulingDependenciesProfilerMarker = new ProfilerMarker("NodeSet.RenderGraph.WaitForSchedulingDependencies");

            // Jobs
            public static ProfilerMarker ComputeCullingCandidates = new ProfilerMarker("NodeSet.RenderGraph.ComputeCullingCandidates");
            public static ProfilerMarker SweepActiveState = new ProfilerMarker("NodeSet.RenderGraph.SweepActiveCullingNodeState");
            public static ProfilerMarker IterativeObservabilityPass = new ProfilerMarker("NodeSet.RenderGraph.IterativeObservabilityPass");

            public static ProfilerMarker AnalyseLiveNodes = new ProfilerMarker("NodeSet.RenderGraph.AnalyseLiveNodes");
            public static ProfilerMarker UpdateInputDataPorts = new ProfilerMarker("NodeSet.RenderGraph.UpdateInputDataPorts");
            public static ProfilerMarker ResizeOutputDataPortArrays = new ProfilerMarker("NodeSet.RenderGraph.ResizeOutputDataPortArrays");
            public static ProfilerMarker ResizeOutputDataBuffers = new ProfilerMarker("NodeSet.RenderGraph.ResizeOutputDataBuffers");
            public static ProfilerMarker ComputeValueChunkAndResizeBuffers = new ProfilerMarker("NodeSet.RenderGraph.ComputeValueChunkAndResizeBuffers");
            public static ProfilerMarker CopyValueDependencies = new ProfilerMarker("NodeSet.RenderGraph.CopyGraphValueDependencies");
        }

    }

    static partial class TopologyAPI<TVertex, TInputPort, TOutputPort>
        where TVertex : unmanaged, IEquatable<TVertex>
        where TInputPort : unmanaged, IEquatable<TInputPort>
        where TOutputPort : unmanaged, IEquatable<TOutputPort>
    {
        public struct ProfilerMarkers
        {
            public static ProfilerMarkers Markers = new ProfilerMarkers
            {
                ReallocateContext = new ProfilerMarker("TopologyAPI.ReallocateContext"),
                ComputeLayout = new ProfilerMarker("TopologyAPI.ComputeLayout"),
                BuildConnectionCache = new ProfilerMarker("TopologyAPI.BuildConnectionCache")
            };

            public ProfilerMarker ReallocateContext;
            public ProfilerMarker ComputeLayout;
            public ProfilerMarker BuildConnectionCache;
        }
    }
}
