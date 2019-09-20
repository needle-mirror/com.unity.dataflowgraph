using System.Runtime.CompilerServices;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Unity.DataFlowGraph.Tests
{
    public class ExternalDependencyTests
    {
        public unsafe struct Data : IKernelData
        {
            fixed byte m_Data[37];
        }

        public enum Compilation
        {
            Vanilla,
            Bursted
        }

        unsafe struct NonBurstJob : IJob
        {
            [NativeDisableUnsafePtrRestriction]
            public void* Int;

            public void Execute()
            {
                ref int i = ref Unsafe.AsRef<int>(Int);
                i *= 2;
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        unsafe struct BurstedJob : IJob
        {
            [NativeDisableUnsafePtrRestriction]
            public void* Int;

            public void Execute()
            {
                ref int i = ref Unsafe.AsRef<int>(Int);
                i *= 2;
            }
        }


        [TestCase(Compilation.Vanilla), TestCase(Compilation.Bursted)]
        public void CanUse_UnsafeAsRef_InsideJob(Compilation means)
        {
            unsafe
            {
                int value = 10;


                if (means == Compilation.Vanilla)
                {
                    NonBurstJob job;
                    job.Int = &value;
                    job.Schedule().Complete();
                }
                else
                {
                    BurstedJob job;
                    job.Int = &value;
                    job.Schedule().Complete();
                }

                Assert.AreEqual(20, value);
            }

        }

        [Test]
        public unsafe void BlittableType_MatchesLanguageIntrisics_ForSizeAlign()
        {
            var bt = SimpleType.Create<Data>();
            var dataSize = UnsafeUtility.SizeOf<Data>();
            var dataAlign = UnsafeUtility.AlignOf<Data>();

            Assert.AreEqual(sizeof(Data), bt.Size);
            Assert.AreEqual(sizeof(Data), dataSize);

            Assert.AreEqual(dataAlign, bt.Align);
        }
    }
}
