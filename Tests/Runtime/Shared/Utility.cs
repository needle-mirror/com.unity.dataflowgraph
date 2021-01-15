using System;
using NUnit.Framework;

namespace Unity.DataFlowGraph.Tests
{
    public enum APIType
    {
        StronglyTyped,
        WeaklyTyped
    }

    public class UtilityAssert
    {
        public static void ThrowsEither<E1, E2>(Action expression)
            where E1 : Exception
            where E2 : Exception
        {
            bool bCaught = false;
            try
            {
                expression();
            }
            catch(E1)
            {
                bCaught = true;
            }
            catch(E2)
            {
                bCaught = true;
            }

            if (!bCaught)
                Assert.Fail($"Neither {typeof(E1)} nor {typeof(E2)} was thrown");
        }
    }
}
