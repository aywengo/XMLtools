using System;

namespace XMLtools.Attributes
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class XmlSerializeAsCustomTypeAttribute : Attribute
    {
    }
}