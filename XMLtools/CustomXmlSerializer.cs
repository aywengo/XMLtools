using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml;
using System.Reflection;
using System.Xml.Serialization;
using System.Collections;
using System.Threading;
using System.Text.RegularExpressions;
using XMLtools.Attributes;

namespace XMLTools
{
    public class CustomXmlSerializer : CustomXmlSerializerBase
    {
        Dictionary<Type, IDictionary<ObjKeyForCache, ObjInfo>> objCache = new Dictionary<Type, IDictionary<ObjKeyForCache, ObjInfo>>();
        int objCacheNextId = 0;
        SerializationOptions options;

        protected CustomXmlSerializer(SerializationOptions opt)
        {
            options = opt;
        }

        void SetTypeInfo(Type objType, XmlElement element)
        {
            element.SetAttribute("type", objType.FullName);

        }
        
        public static XmlDocument Serialize(object obj, string rootName)
        {
            // determine serialization options
            var serOptions = new SerializationOptions();
            if (obj != null)
            {
                Type objType = obj.GetType();
                object[] attribs = objType.GetCustomAttributes(typeof(CustomXmlSerializationOptionsAttribute), false);
                if (attribs.Length > 0)
                {
                    serOptions = ((CustomXmlSerializationOptionsAttribute)attribs[0]).SerializationOptions;
                }
            }
            // create serializer
            var serializer = new CustomXmlSerializer(serOptions);
            XmlElement element = serializer.SerializeCore(rootName, obj);
            element.SetAttribute("culture", Thread.CurrentThread.CurrentCulture.ToString());

            // add serialized data
            serializer.doc.AppendChild(element);
            return serializer.doc;
        }

        bool AddObjToCache(Type objType, object obj, XmlElement element)
        {
            var kfc = new ObjKeyForCache(obj);
            IDictionary<ObjKeyForCache, ObjInfo> entry;
            if (objCache.TryGetValue(objType, out entry))
            {
                // look for this particular object                
                ObjInfo objInfoFound;
                if (entry.TryGetValue(kfc, out objInfoFound))
                {
                    // the object has already been added
                    if (objInfoFound.OnlyElement != null)
                    {
                        objInfoFound.WriteObjId(objInfoFound.OnlyElement);
                        objInfoFound.OnlyElement = null;
                    }
                    // write id to element
                    objInfoFound.WriteObjId(element);
                    return false;
                }
            }
            else
            {
                // brand new type in the cache
                entry = new Dictionary<ObjKeyForCache, ObjInfo>(1);
                objCache.Add(objType, entry);
            }
            // object not found, add it
            var objInfo = new ObjInfo();
            objInfo.Id = objCacheNextId;
            objInfo.OnlyElement = element;
            entry.Add(kfc, objInfo);
            objCacheNextId++;
            return true;
        }

        static bool CheckForcedSerialization(Type objType)
        {
            object[] attribs = objType.GetCustomAttributes(typeof(XmlSerializeAsCustomTypeAttribute), false);
            return attribs.Length > 0;
        }

        XmlElement SerializeCore(string name, object obj)
        {
            XmlElement element = doc.CreateElement(name);
            if (obj == null)
            {
                element.SetAttribute("value", "null");
                return element;
            }

            Type objType = obj.GetType();

            if (objType.IsClass && objType != typeof(string))
            {
                if (objType == typeof(IPEndPoint))
                {
                    element.SetAttribute("value", ((IPEndPoint) obj).Address + ":" + (obj as IPEndPoint).Port);
                    return element;
                }

                // check if we have already serialized this object
                if (options.UseGraphSerialization
                    && !AddObjToCache(objType, obj, element)
                    )
                {
                    return element;
                }

                if (CheckForcedSerialization(objType))
                {
                    // serialize as complex type
                    SerializeComplexType(obj, element);
                    return element;
                }

                var xmlSer = obj as IXmlSerializable;
                if (xmlSer == null)
                {
                    // does not know about automatic serialization
                    var arr = obj as IEnumerable;
                    if (arr == null)
                    {
                      //  SetTypeInfo(objType, element);

                        SerializeComplexType(obj, element);
                    }
                    else
                    {
                        foreach (XmlElement e in from object arrObj in arr select SerializeCore(name, arrObj))
                        {
                            element.AppendChild(e);
                        }
                    }
                }
                else
                {
                    // can perform the serialization itself
                    var sb = new StringBuilder();
                    var settings = new XmlWriterSettings
                                                     {
                                                         ConformanceLevel = ConformanceLevel.Fragment,
                                                         Encoding = Encoding.UTF8,
                                                         OmitXmlDeclaration = true
                                                     };
                    XmlWriter wr = XmlWriter.Create(sb, settings);
                    wr.WriteStartElement("value");
                    xmlSer.WriteXml(wr);
                    wr.WriteEndElement();
                    wr.Close();

                    element.InnerXml = sb.ToString();
                }
            }
            else
            {
                if (CheckForcedSerialization(objType))
                {
                    // serialize as complex type
                    SerializeComplexType(obj, element);
                    return element;
                }

                if (objType.IsEnum)
                {
                    object val = Enum.Format(objType, obj, "d");
                    element.SetAttribute("value", val.ToString());
                }
                else
                {
                    if (objType.IsPrimitive || objType == typeof(string) ||
                        objType == typeof(DateTime) || objType == typeof(decimal))
                    {
                        element.SetAttribute("value", obj.ToString());
                    }
                    else
                    {
                        // this is most probably a struct
                        SerializeComplexType(obj, element);
                    }
                }
            }

            return element;
        }

        void SerializeComplexType(object obj, XmlElement element)
        {
            Type objType = obj.GetType();
            // get all instance fields
            const string regExpr = @"<(.*?)>k__BackingField";
            IDictionary<string, FieldInfo> fields = GetTypeFieldInfo(objType);
            foreach (KeyValuePair<string, FieldInfo> kv in fields)
            {
                // serialize field
                var key = kv.Key;
                Match match = Regex.Match(kv.Key, regExpr);
                if (match.Success)
                {
                    key =
                        match.Groups[1].Value;
                }

                XmlElement e = SerializeCore(key, kv.Value.GetValue(obj));
                element.AppendChild(e);
            }
        }

        class ObjInfo
        {
            internal int Id;
            internal XmlElement OnlyElement;

            internal void WriteObjId(XmlElement element)
            {
                element.SetAttribute("id", Id.ToString(CultureInfo.InvariantCulture));
            }
        }

        struct ObjKeyForCache : IEquatable<ObjKeyForCache>
        {
            object m_obj;

            public ObjKeyForCache(object obj)
            {
                m_obj = obj;
            }

            public bool Equals(ObjKeyForCache other)
            {
                return object.ReferenceEquals(m_obj, other.m_obj);
            }
        }

        public class SerializationOptions
        {
            public bool UseGraphSerialization = true;
        }
    }
}
