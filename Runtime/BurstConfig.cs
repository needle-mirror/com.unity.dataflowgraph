using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Unity.DataFlowGraph
{
    static class BurstConfig
    {
        static bool s_IsBurstEnabledInitilized;
        static bool s_IsBurstEnabled;

        public enum ExecutionResult
        {
            Undefined,
            InsideBurst,
            InsideMono
        }

        public static ExecutionResult DetectExecutionEngine()
        {
            ExecutionResult exec = ExecutionResult.InsideBurst;
            OverrideToMono(ref exec);
            return exec;
        }

        public static unsafe bool IsBurstEnabled
        {
            get
            {
                if (s_IsBurstEnabledInitilized)
                    return s_IsBurstEnabled;

                bool result;
                DetectBurstJob job;
                job.Result = &result;
                job.Schedule().Complete();
                s_IsBurstEnabled = result;
                s_IsBurstEnabledInitilized = true;
                return s_IsBurstEnabled;
            }
        }

        [BurstDiscard]
        static void OverrideToMono(ref ExecutionResult answer) => answer = ExecutionResult.InsideMono;

        [BurstCompile(CompileSynchronously = true)]
        unsafe struct DetectBurstJob : IJob
        {
            [NativeDisableUnsafePtrRestriction]
            public bool* Result;
            public void Execute() => *Result = DetectExecutionEngine() == ExecutionResult.InsideBurst;

        }
    }
}