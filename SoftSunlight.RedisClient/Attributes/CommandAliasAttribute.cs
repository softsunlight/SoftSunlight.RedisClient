using System;
using System.Collections.Generic;
using System.Text;

namespace SoftSunlight.RedisClient.Attributes
{
    [System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    internal class CommandAliasAttribute : Attribute
    {
        public string Name { get; private set; }

        public CommandAliasAttribute(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentNullException("name is not null");
            }
            this.Name = name;
        }
    }
}
