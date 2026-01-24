using System.Collections.Generic;
using System.Linq;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace MyDotNetApp.Tests
{
    /// <summary>
    /// Orders tests so that ones marked with LongRunningFact run last. Keeps others alphabetical for determinism.
    /// </summary>
    public class LongLastOrderer : ITestCaseOrderer
    {
        public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases) where TTestCase : ITestCase
        {
            var grouped = testCases
                .Select(tc => new
                {
                    TestCase = tc,
                    IsLong = tc.TestMethod.Method.ToRuntimeMethod()?
                        .GetCustomAttributes(typeof(LongRunningFactAttribute), true)
                        .Any() == true
                })
                .OrderBy(x => x.IsLong) // false first, true last
                .ThenBy(x => x.TestCase.TestMethod.Method.Name);

            foreach (var item in grouped)
            {
                yield return item.TestCase;
            }
        }
    }
}
