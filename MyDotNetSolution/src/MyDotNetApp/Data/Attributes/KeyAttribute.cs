using System;

namespace MyDotNetApp.Data.Attributes
{
    /// <summary>
    /// Marks a property as a primary key (supports single or composite keys)
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class KeyAttribute : Attribute
    {
    }
}
