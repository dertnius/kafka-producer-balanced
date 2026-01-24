using System;
using Xunit;

namespace MyDotNetApp.Tests
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class LongRunningFactAttribute : FactAttribute
    {
        public LongRunningFactAttribute()
        {
            // Currently unused: all tests run; keep placeholder for future opt-in control
        }
    }
}
