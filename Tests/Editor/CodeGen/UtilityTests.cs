using NUnit.Framework;

namespace Unity.DataFlowGraph.CodeGen.Tests
{

    public class UtilityTests
    {
        [Test]
        public void Diagnostic_WithErrorsOrExceptions_ConfirmsHasErrors()
        {
            var diag = new Diag();
            Assert.False(diag.HasErrors());

            diag.Error("");
            Assert.True(diag.HasErrors());

            diag = new Diag();

            diag.Exception("");
            Assert.True(diag.HasErrors());
        }

        [Test]
        public void Diagnostic_WithWarning_DoesNotHaveErrors()
        {
            var diag = new Diag();
            diag.Warning("");
            Assert.False(diag.HasErrors());
        }
    }
}
