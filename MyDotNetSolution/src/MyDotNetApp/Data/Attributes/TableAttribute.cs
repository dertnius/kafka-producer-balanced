using System;

namespace MyDotNetApp.Data.Attributes
{
    /// <summary>
    /// Specifies the database table name for an entity
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class TableAttribute : Attribute
    {
        public string Name { get; }

        public TableAttribute(string name)
        {
            Name = name;
        }
    }
}
