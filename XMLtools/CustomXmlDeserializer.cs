using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;

namespace XMLTools
{
    public class CustomXmlDeserializer : CustomXmlSerializerBase
    {
        CultureInfo m_cult;
        
        public static object Deserialize(string xml, Type type = null)
        {
            var deserializer = new CustomXmlDeserializer();
            deserializer.doc.LoadXml(xml);
            if (deserializer.doc.DocumentElement != null)
            {
                string culture = deserializer.doc.DocumentElement.GetAttribute("culture");
                deserializer.m_cult = new CultureInfo(culture);
            }
            return deserializer.DeserializeCore(deserializer.doc.DocumentElement, type);
        }

        void DeserializeComplexType(object obj, Type objType, XmlNode firstChild)
        {
            // complex type
            // get the class's fields                                
            IDictionary<string, FieldInfo> dictFields = GetTypeFieldInfo(objType);
            // set values for fields that are found
                        
            const string regExpr = @"<(.*?)>k__BackingField";
            bool isBackingField = dictFields.Any(a => Regex.IsMatch(a.Key, regExpr));
            
            for (XmlNode node = firstChild; node != null; node = node.NextSibling)
            {
                string fieldName = node.Name;

                if (isBackingField)
                    fieldName = "<"
                                + node.Name
                                + ">k__BackingField";

                FieldInfo field = null;
                if (dictFields.TryGetValue(fieldName, out field))
                {
                    // field is present, get value
                    object val = DeserializeCore((XmlElement)node, field.FieldType);
                    // set value in object
                    field.SetValue(obj, val);
                }
            }
        }

        object DeserializeCore(XmlElement element, Type type = null)
        {
            // check for null
            string value = element.GetAttribute("value");
            if (value == "null") return null;

            int subItems = element.ChildNodes.Count;
            XmlNode firstChild = element.FirstChild;

            // get type            
            Type objType = type ?? InferTypeFromElement(element);

            // process enum
            if (objType.IsEnum)
            {
                long val = Convert.ToInt64(value, m_cult);
                return Enum.ToObject(objType, val);
            }

            // process some simple types
            switch (Type.GetTypeCode(objType))
            {
                case TypeCode.Boolean: return Convert.ToBoolean(value, m_cult);
                case TypeCode.Byte: return Convert.ToByte(value, m_cult);
                case TypeCode.Char: return Convert.ToChar(value, m_cult);
                case TypeCode.DBNull: return DBNull.Value;
                case TypeCode.DateTime: return Convert.ToDateTime(value, m_cult);
                case TypeCode.Decimal: return Convert.ToDecimal(value, m_cult);
                case TypeCode.Double: return Convert.ToDouble(value, m_cult);
                case TypeCode.Int16: return Convert.ToInt16(value, m_cult);
                case TypeCode.Int32: return Convert.ToInt32(value, m_cult);
                case TypeCode.Int64: return Convert.ToInt64(value, m_cult);
                case TypeCode.SByte: return Convert.ToSByte(value, m_cult);
                case TypeCode.Single: return Convert.ToSingle(value, m_cult);
                case TypeCode.String: return value;
                case TypeCode.UInt16: return Convert.ToUInt16(value, m_cult);
                case TypeCode.UInt32: return Convert.ToUInt32(value, m_cult);
                case TypeCode.UInt64: return Convert.ToUInt64(value, m_cult);
            }

            // our value
            object obj = null;

            if (objType.IsArray)
            {
                Type elementType = objType.GetElementType();
                MethodInfo setMethod = objType.GetMethod("Set", new Type[] { typeof(int), elementType });

                ConstructorInfo constructor = objType.GetConstructor(new Type[] { typeof(int) });
                if (constructor != null) obj = constructor.Invoke(new object[] { subItems });

                int i = 0;
                foreach (object val in ValuesFromNode(firstChild, elementType))
                {
                    setMethod.Invoke(obj, new[] { i, val });
                    i++;
                }
                return obj;
            }

            //create instance IPEndPoint
            if (objType == typeof(System.Net.IPEndPoint))
            {
                const string regExpr = @"(\w*\.\w*\.\w*\.\w*):(\w*)";

                Match match = Regex.Match(value, regExpr);
                if (match.Success)
                {
                    string portvalue = match.Groups[2].Value;
                    int nPort = Int32.Parse(portvalue);

                    string ipValue = match.Groups[1].Value;

                    return new System.Net.IPEndPoint(System.Net.IPAddress.Parse(ipValue), nPort);
                }
                
                return new System.Net.IPEndPoint(System.Net.IPAddress.Parse("127.0.0.1"), 8500);
            }

            // create a new instance of the object
            obj = Activator.CreateInstance(objType);

            var xmlSer = obj as IXmlSerializable;
            if (xmlSer == null)
            {
                var lst = obj as IList;
                if (lst == null)
                {
                    var dict = obj as IDictionary;
                    if (dict == null)
                    {
                        if (objType == typeof(DictionaryEntry) ||
                            (objType.IsGenericType &&
                             objType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>)))
                        {
                            // load all field contents in a dictionary
                            var properties = new Dictionary<string, object>(element.ChildNodes.Count);
                            for (XmlNode node = firstChild; node != null; node = node.NextSibling)
                            {
                                object val = DeserializeCore((XmlElement)node);
                                properties.Add(node.Name, val);
                            }
                            // return the dictionary
                            return properties;
                        }
                        // complex type
                        DeserializeComplexType(obj, objType, firstChild);
                    }
                    else
                    {
                        // it's a dictionary
                        foreach (object val in ValuesFromNode(firstChild))
                        {
                            // should be a Dictionary                                    
                            var dictVal = (Dictionary<string, object>)val;
                            if (dictVal.ContainsKey("key"))
                            {
                                // should be a KeyValuePair
                                dict.Add(dictVal["key"], dictVal["value"]);
                            }
                            else
                            {
                                // should be a DictionaryEntry
                                dict.Add(dictVal["_key"], dictVal["_value"]);
                            }
                        }
                    }
                }
                else
                {
                    // it's a list
                    foreach (object val in ValuesFromNode(firstChild))
                    {
                        lst.Add(val);
                    }
                }
            }
            else
            {
                // the object can deserialize itself
                var sr = new StringReader(element.InnerXml);
                XmlReader rd = XmlReader.Create(sr);
                xmlSer.ReadXml(rd);
                rd.Close();
                sr.Close();
            }
            return obj;
        }

        IEnumerable ValuesFromNode(XmlNode firstChild, Type type = null)
        {
            for (XmlNode node = firstChild; node != null; node = node.NextSibling)
            {
                yield return DeserializeCore((XmlElement)node, type);
            }
        }

        Type InferTypeFromElement(XmlElement element)
        {
            Type objType = null;
            string typeFullName = element.GetAttribute("type");

            if (!String.IsNullOrEmpty(typeFullName))
            {
                objType = AppDomain.CurrentDomain
                              .GetAssemblies()
                              .Select(a => a.GetType(typeFullName))
                              .FirstOrDefault(b => b != null) 
                          ??
                          Type.GetType(typeFullName, true);
            }

            return objType;
        }

    }
}