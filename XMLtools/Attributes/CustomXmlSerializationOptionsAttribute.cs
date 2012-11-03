using System;
using XMLTools;

namespace XMLtools.Attributes
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class CustomXmlSerializationOptionsAttribute : Attribute
    {
        public CustomXmlSerializer.SerializationOptions SerializationOptions = new CustomXmlSerializer.SerializationOptions();

        public CustomXmlSerializationOptionsAttribute(bool useGraphSerialization)
        {
            SerializationOptions.UseGraphSerialization = useGraphSerialization;
        }
    }
}