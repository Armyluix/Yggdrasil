﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Yggdrasil
{
    class TypeConverters
    {
        public class HexByteConverter : TypeConverter
        {
            public virtual Type DataType { get { return typeof(byte); } }
            public virtual string FormatString { get { return "0x{0:X2}"; } }

            public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
            {
                if (sourceType == typeof(string)) return true;
                else return base.CanConvertFrom(context, sourceType);
            }

            public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
            {
                if (destinationType == typeof(string)) return true;
                else return base.CanConvertTo(context, destinationType);
            }

            public override object ConvertTo(ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, Type destinationType)
            {
                if (destinationType == typeof(string) && value.GetType() == DataType)
                    return string.Format(FormatString, value);
                else
                    return base.ConvertTo(context, culture, value, destinationType);
            }

            public override object ConvertFrom(ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value)
            {
                if (value.GetType() == typeof(string))
                {
                    string input = (string)value;
                    if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) input = input.Substring(2);

                    try
                    {
                        return DataType.InvokeMember("Parse", BindingFlags.Public | BindingFlags.InvokeMethod | BindingFlags.Static, null, null,
                            new object[] { input, System.Globalization.NumberStyles.HexNumber, culture });
                    }
                    catch (Exception)
                    {
                        throw;
                    }
                }
                else
                    return base.ConvertFrom(context, culture, value);
            }
        }

        public class HexSbyteConverter : HexByteConverter
        {
            public override Type DataType { get { return typeof(sbyte); } }
        }

        public class HexUshortConverter : HexByteConverter
        {
            public override Type DataType { get { return typeof(ushort); } }
            public override string FormatString { get { return "0x{0:X4}"; } }
        }

        public class HexShortConverter : HexByteConverter
        {
            public override Type DataType { get { return typeof(short); } }
        }

        public class HexUintConverter : HexByteConverter
        {
            public override Type DataType { get { return typeof(uint); } }
            public override string FormatString { get { return "0x{0:X8}"; } }
        }

        public class HexIntConverter : HexByteConverter
        {
            public override Type DataType { get { return typeof(int); } }
        }

        public class SuffixByteConverter : TypeConverter
        {
            public virtual Type DataType { get { return typeof(byte); } }
            public virtual string FormatString { get { return "{0} {1}"; } }
            public virtual string Suffix { get { return string.Empty; } }

            public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
            {
                if (sourceType == typeof(string)) return true;
                else return base.CanConvertFrom(context, sourceType);
            }

            public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
            {
                if (destinationType == typeof(string)) return true;
                else return base.CanConvertTo(context, destinationType);
            }

            public override object ConvertTo(ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, Type destinationType)
            {
                if (destinationType == typeof(string) && value.GetType() == DataType)
                    return string.Format(FormatString, value, Suffix);
                else
                    return base.ConvertTo(context, culture, value, destinationType);
            }

            public override object ConvertFrom(ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value)
            {
                if (value.GetType() == typeof(string))
                {
                    try
                    {
                        string input = Regex.Replace((string)value, @"[^-?\d]", "");
                        return DataType.InvokeMember("Parse", BindingFlags.Public | BindingFlags.InvokeMethod | BindingFlags.Static, null, null,
                            new object[] { input, culture });
                    }
                    catch (Exception)
                    {
                        throw;
                    }
                }
                else
                    return base.ConvertFrom(context, culture, value);
            }
        }

        public class BytePercentageConverter : SuffixByteConverter
        {
            public override Type DataType { get { return typeof(byte); } }
            public override string FormatString { get { return "{0}{1}"; } }
            public override string Suffix { get { return "%"; } }
        }

        public class SbytePercentageConverter : SuffixByteConverter
        {
            public override Type DataType { get { return typeof(sbyte); } }
            public override string FormatString { get { return "{0}{1}"; } }
            public override string Suffix { get { return "%"; } }
        }

        public class UshortPercentageConverter : SuffixByteConverter
        {
            public override Type DataType { get { return typeof(ushort); } }
            public override string FormatString { get { return "{0}{1}"; } }
            public override string Suffix { get { return "%"; } }
        }

        public class EtrianEnConverter : SuffixByteConverter
        {
            public override Type DataType { get { return typeof(uint); } }
            public override string Suffix { get { return "en"; } }
        }

        public class ByteItemCountConverter : SuffixByteConverter
        {
            public override Type DataType { get { return typeof(byte); } }
            public override string Suffix { get { return "x"; } }
        }

        public class ExpConverter : SuffixByteConverter
        {
            public override Type DataType { get { return typeof(uint); } }
            public override string Suffix { get { return "EXP"; } }
        }

        public abstract class DictionaryStringConverter : TypeConverter
        {
            public virtual Dictionary<ushort, string> Dictionary { get { return null; } }

            List<ushort> valueList;

            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override System.ComponentModel.TypeConverter.StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                if (valueList == null)
                {
                    valueList = new List<ushort>();
                    foreach (KeyValuePair<ushort, string> pair in Dictionary) valueList.Add(pair.Key);
                }

                return new StandardValuesCollection(valueList.ToArray());
            }

            public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
            {
                if (sourceType == typeof(string)) return true;
                else return base.CanConvertFrom(context, sourceType);
            }

            public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
            {
                if (destinationType == typeof(string)) return true;
                else return base.CanConvertTo(context, destinationType);
            }

            public override object ConvertTo(ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, Type destinationType)
            {
                if (destinationType == typeof(string) && value.GetType() == typeof(ushort))
                    return Dictionary[(ushort)value];
                else
                    return base.ConvertTo(context, culture, value, destinationType);
            }

            public override object ConvertFrom(ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value)
            {
                if (value.GetType() == typeof(string))
                {
                    try
                    {
                        KeyValuePair<ushort, string> pair =
                            Dictionary
                            .OrderBy(x => x.Key)
                            .FirstOrDefault(x => x.Value.ToLowerInvariant().Contains((value as string).ToLowerInvariant()));

                        return pair.Key;
                    }
                    catch (Exception)
                    {
                        throw;
                    }
                }
                else
                    return base.ConvertFrom(context, culture, value);
            }
        }

        public class ItemNameConverter : DictionaryStringConverter
        {
            public override Dictionary<ushort, string> Dictionary { get { return GameDataManager.ItemNames; } }
        }

        public class EnemyNameConverter : DictionaryStringConverter
        {
            public override Dictionary<ushort, string> Dictionary { get { return GameDataManager.EnemyNames; } }
        }

        public class EncounterConverter : DictionaryStringConverter
        {
            public override Dictionary<ushort, string> Dictionary { get { return GameDataManager.EncounterDescriptions; } }
        }

        public class PlayerSkillNameConverter : DictionaryStringConverter
        {
            public override Dictionary<ushort, string> Dictionary { get { return GameDataManager.PlayerSkillNames; } }
        }
    }
}