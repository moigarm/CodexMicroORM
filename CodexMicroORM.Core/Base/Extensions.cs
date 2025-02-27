﻿/***********************************************************************
Copyright 2021 CodeX Enterprises LLC

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.

Major Changes:
12/2017    0.2     Initial release (Joel Champagne)
2/2020     0.9.5   Enabled nullable (Joel Champagne)
***********************************************************************/
#nullable enable
using System;
using System.ComponentModel;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Text;
using System.Collections.Concurrent;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
#if CGEH
namespace CodexMicroORM.Core.CG
#else
using CodexMicroORM.Core.Services;
using CodexMicroORM.Core.Helper;

namespace CodexMicroORM.Core
#endif
{
    /// <summary>
    /// Not intended for general consumption, helper functions for the framework.
    /// </summary>
    internal static class InternalExtensions
    {
#if !CGEH
        public static bool HasProperty(this object o, string propName)
        {
            if (o == null)
                return false;

            if (o is ICEFInfraWrapper wrapper)
            {
                return wrapper.HasProperty(propName);
            }

            return o.FastPropertyReadable(propName);
        }
#endif

        public static TV AssignReturn<TK, TV>(this Dictionary<TK, TV> dic, TK key, TV val)
        {
            dic[key] = val;
            return val;
        }

        public static TV TestAssignReturn<TK, TV>(this Dictionary<TK, TV> dic, TK key, Func<TV> getval)
        {
            if (dic.ContainsKey(key))
            {
                return dic[key];
            }

            var val = getval();
            dic[key] = val;
            return val;
        }
    }

    /// <summary>
    /// Mostly syntactic sugar for existing methods such as for static methods on the CEF class.
    /// </summary>
    public static class PublicExtensions
    {
        private static readonly ConcurrentDictionary<Type, Type> _typeMap = new();

        public static int MinOf(this int i1, int i2)
        {
            if (i1 < i2)
                return i1;

            return i2;
        }

        public static int MaxOf(this int i1, int i2)
        {
            if (i1 > i2)
                return i1;

            return i2;
        }

        public static T[] Arrayize<T>(this T item)
        {
            return new T[] { item };
        }

        public static string? LeftWithEllipsis(this string? str, int count)
        {
            if (string.IsNullOrEmpty(str))
            {
                return str;
            }

            if (str.Length <= count - 4)
            {
                return str;
            }

            return str.Substring(0, count - 4) + " ...";
        }

        public static string? Left(this string? str, int count)
        {
            if (string.IsNullOrEmpty(str))
            {
                return str;
            }

            if (str.Length <= count)
            {
                return str;
            }

            return str.Substring(0, count);
        }

        public static string? Right(this string? str, int count)
        {
            if (string.IsNullOrEmpty(str))
            {
                return str;
            }

            if (str.Length <= count)
            {
                return str;
            }

            return str.Substring(str.Length - count, count);
        }

        public static string InnerTextSafe(this System.Xml.XmlNode xn, string defval = "")
        {
            if (xn == null)
            {
                return defval;
            }

            return xn.InnerText ?? defval;
        }

        public static bool IsSame(this object? o1, object? o2, bool canCompareNull = true)
        {
            if (o1 == DBNull.Value)
                o1 = null;

            if (o2 == DBNull.Value)
                o2 = null;

            if (canCompareNull)
            {
                if (o1 == null && o2 == null)
                    return true;

                if (o1 == null && o2 != null)
                    return false;

                if (o2 == null && o1 != null)
                    return false;
            }
            else
            {
                if (o1 == null || o2 == null)
                    return false;
            }

            if (o1!.GetType() == o2!.GetType())
            {
                return o1.Equals(o2);
            }

            if (o1 is IConvertible && o2 is IConvertible)
            {
                return Convert.ChangeType(o1, o2.GetType()).Equals(o2);
            }

            return o1.ToString() == o2.ToString();
        }

        public static void ForAll<T>(this IEnumerable<T> items, Action<T> work)
        {
            foreach (var i in items)
            {
                work(i);
            }
        }

        public static object? CoerceType(this string? source, Type prefType, object? defaultIfFail = null)
        {
            if (source == null)
            {
                return null;
            }

            if (prefType == typeof(string))
            {
                return source;
            }

            var nt = Nullable.GetUnderlyingType(prefType);

            if (nt != null)
            {
                if (string.IsNullOrEmpty(source))
                {
                    return Activator.CreateInstance(prefType);
                }

                prefType = nt;
            }

            if (prefType.IsEnum)
            {
                return Enum.Parse(prefType, source);
            }

            if (prefType == typeof(TimeSpan))
            {
                if (TimeSpan.TryParse(source, out TimeSpan ts))
                {
                    return ts;
                }
            }

            if (source is IConvertible)
            {
                // Special conversion possibilities for booleans
                if (prefType == typeof(bool))
                {
                    if (source == "0")
                    {
                        return false;
                    }
                    if (source == "-1")
                    {
                        return true;
                    }
                    if (source == "1")
                    {
                        return true;
                    }
                    if (string.Compare(source, "false", true) == 0)
                    {
                        return false;
                    }
                    if (string.Compare(source, "true", true) == 0)
                    {
                        return true;
                    }
                }
                else
                {
                    if (prefType == typeof(Guid))
                    {
                        return new Guid(source);
                    }
                }

                return Convert.ChangeType(source, prefType);
            }

            if (!source.GetType().IsValueType)
            {
                return source;
            }

            if (defaultIfFail != null)
            {
                return defaultIfFail;
            }

            throw new InvalidCastException("Cannot coerce type.");
        }

        public static object EnsureNullable(this object source, Type nullType)
        {
            if (source == null)
            {
                return Activator.CreateInstance(nullType);
            }

            if (source.GetType().Equals(nullType))
            {
                return source;
            }

            return Activator.CreateInstance(nullType, source);
        }

        public static object? CoerceDBNullableType(this object? source, Type prefType)
        {
            if (source == null || DBNull.Value.Equals(source))
            {
                return null;
            }

            // Avoid casting to string if the types match
            if (source.GetType() == prefType)
            {
                return source;
            }

            if (source.GetType() == typeof(DateTime))
            {
                return ((DateTime)source).ToString("O").CoerceType(prefType);
            }

            return source.ToString().CoerceType(prefType);
        }

        public static T CoerceType<T>(this string? source)
        {
            if (source == null)
            {
                return default!;
            }

            if (typeof(T) == typeof(string))
            {
                return (T)(object)source;
            }

            if (Nullable.GetUnderlyingType(typeof(T)) != null)
            {
                if (string.IsNullOrEmpty(source))
                {
                    return (T)Activator.CreateInstance(typeof(T));
                }

                return (T)Activator.CreateInstance(typeof(T), Convert.ChangeType(source, Nullable.GetUnderlyingType(typeof(T))));
            }

            if (typeof(T).IsEnum)
            {
                return (T)Enum.Parse(typeof(T), source);
            }

            if (source is IConvertible)
            {
                return (T)Convert.ChangeType(source, typeof(T));
            }

            if (!source.GetType().IsValueType)
            {
                return (T)(object)source;
            }

            throw new InvalidCastException("Cannot coerce type.");
        }

#if !CGEH
        /// <summary>
        /// Save a specific entity set. Restricts/fitlers to rows present in the collection.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="toSave">EntitySet to use as a save filter.</param>
        /// <param name="settings">Optional save config settings.</param>
        /// <returns></returns>
        public static EntitySet<T> DBSave<T>(this EntitySet<T> toSave, DBSaveSettings? settings = null) where T: class, new()
        {
            if (settings == null)
            {
                settings = new DBSaveSettings();
            }

            settings.SourceList = toSave;
            settings.EntityPersistName ??= CEF.GetEntityPersistName<T>(toSave);
            settings.EntityPersistType = typeof(T);

            CEF.DBSave(settings);
            return toSave;
        }

        public static void ValidateOrAssignMandatoryValue<T>(this EntitySet<T> toCheck, string field, object value) where T : class, new()
        {
            foreach (var t in toCheck)
            {
                var iw = t.AsInfraWrapped();
                var ov = iw?.GetValue(field);

                if (string.Compare(value?.ToString(), ov?.ToString(), true) != 0)
                {
                    if (ov?.ToString().Length == 0 && value?.ToString().Length > 0)
                    {
                        iw?.SetValue(field, value);
                    }
                    else
                    {
                        throw new CEFInvalidStateException(InvalidStateType.LowLevelState);
                    }
                }
            }

            foreach (var to in CEF.CurrentServiceScope.GetAllTrackedByType(typeof(T)))
            {
                var iw = to.GetCreateInfra();

                if (iw != null)
                {
                    if (iw.GetRowState() == ObjectState.Deleted)
                    {
                        var ov = iw.GetOriginalValue(field, false);

                        if (string.Compare(value?.ToString(), ov?.ToString(), true) != 0)
                        {
                            if (ov?.ToString().Length != 0 || value?.ToString().Length == 0)
                            {
                                throw new CEFInvalidStateException(InvalidStateType.LowLevelState);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Takes a potentially unwrapped object and returns a dynamic (DLR) object that exposes the same properties plus all available "extended properties".
        /// Note: this is not always "bindable" for UI data binding (see GenericBindableSet for WPF, for example).
        /// </summary>
        /// <param name="unwrapped">Any object that's tracked within the current service scope.</param>
        /// <returns></returns>
        public static dynamic? AsDynamic(this object unwrapped)
        {
            return CEF.CurrentServiceScope.GetDynamicWrapperFor(unwrapped);
        }

        /// <summary>
        /// Derivative of AsInfraWrapped, returns non-nullable value and throws exception if for any reason wrapper creation was not possible.
        /// </summary>
        /// <param name="o">Any object that's tracked within the current service scope.</param>
        /// <param name="canCreate">If false, the object must have an existing infrastructure wrapper or null is returned; if true, a new wrapper can be created.</param>
        /// <param name="ss">Optional explicit service scope tracking object.</param>
        /// <returns></returns>
        public static ICEFInfraWrapper MustInfraWrap(this object o, bool canCreate = true, ServiceScope? ss = null)
        {
            return AsInfraWrapped(o, canCreate, ss) ?? throw new CEFInvalidStateException("Could not create wrapper object. This indicates a likely programming issue.");
        }

        /// <summary>
        /// Infrastructure wrappers offer extended information about tracked objects, such as their "row state" (added, modified, etc.).
        /// </summary>
        /// <param name="o">Any object that's tracked within the current service scope.</param>
        /// <param name="canCreate">If false, the object must have an existing infrastructure wrapper or null is returned; if true, a new wrapper can be created.</param>
        /// <param name="ss">Optional explicit service scope tracking object.</param>
        /// <returns></returns>
        public static ICEFInfraWrapper? AsInfraWrapped(this object o, bool canCreate = true, ServiceScope? ss = null)
        {
            ss ??= CEF.CurrentServiceScope;

            ICEFInfraWrapper? w = ss.GetOrCreateInfra(o, canCreate);

            if (w == null && canCreate)
            {
                var t = ss.IncludeObjectNonGeneric(o, null);
                
                if (t != null)
                {
                    w = ss.GetOrCreateInfra(t, canCreate);
                }

                if (w == null)
                {
                    throw new CEFInvalidStateException(InvalidStateType.ObjectTrackingIssue);
                }
            }

            return w;
        }

        public static T? AsNullValue<T>(this object v) where T : struct
        {
            if (v == null)
            {
                return null;
            }

            if (v is T?)
            {
                return (T?)v;
            }

            if (v is T t)
            {
                return new T?(t);
            }

            throw new CEFInvalidStateException(InvalidStateType.DataTypeIssue, $"Invalid cast of type {typeof(T).Name}?.");
        }

        /// <summary>
        /// Abstracts property value access to work with virtually any type of object, accessing a named property that's a value type (returning a nullable form of it).
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="o"></param>
        /// <param name="propName"></param>
        /// <returns></returns>
        public static T? PropertyNullValue<T>(this object o, string propName) where T : struct
        {
            var iw = o.AsInfraWrapped(true) ?? throw new CEFInvalidStateException(InvalidStateType.ObjectTrackingIssue);

            if (iw.HasProperty(propName))
            {
                var v = iw.GetValue(propName);

                if (v == null)
                {
                    return null;
                }

                if (v is T?)
                {
                    return (T?)v;
                }

                if (v is T t)
                {
                    return new T?(t);
                }

                throw new CEFInvalidStateException(InvalidStateType.DataTypeIssue, $"Invalid cast of type {typeof(T).Name}?.");
            }

            return null;
        }

        public static T WrappedValueTypeValue<T>(this ICEFInfraWrapper iw, string propName) where T : struct
        {
            if (iw.HasProperty(propName))
            {
                var v = iw.GetValue(propName);

                if (v != null)
                {
                    if (v is T?)
                    {
                        return (T)v;
                    }

                    if (v is T t)
                    {
                        return t;
                    }

                    throw new CEFInvalidStateException(InvalidStateType.DataTypeIssue, $"Invalid cast of type {typeof(T).Name}?.");
                }
            }

            return default;
        }

        public static T? WrappedValueTypeNullValue<T>(this ICEFInfraWrapper iw, string propName) where T : struct
        {
            if (iw.HasProperty(propName))
            {
                var v = iw.GetValue(propName);

                if (v == null)
                {
                    return default;
                }

                if (v is T?)
                {
                    return (T?)v;
                }

                if (v is T t)
                {
                    return new T?(t);
                }

                throw new CEFInvalidStateException(InvalidStateType.DataTypeIssue, $"Invalid cast of type {typeof(T).Name}?.");
            }

            return default;
        }

        public static T WrappedRefTypeValue<T>(this ICEFInfraWrapper iw, string propName) where T : class
        {
            if (iw.HasProperty(propName))
            {
                var v = iw.GetValue(propName);

                if (v != null)
                {
                    if (typeof(T) == typeof(string))
                    {
                        return (T)(object)v.ToString();
                    }

                    if (v is T t)
                    {
                        return t;
                    }

                    return (T)Convert.ChangeType(v, typeof(T));
                }
            }

            throw new NullReferenceException($"Property {propName} expects a non-null value.");
        }

        public static T? WrappedRefTypeNullValue<T>(this ICEFInfraWrapper iw, string propName) where T : class
        {
            if (iw.HasProperty(propName))
            {
                var v = iw.GetValue(propName);

                if (v == null)
                {
                    return default;
                }

                if (typeof(T) == typeof(string))
                {
                    return (T)(object)v.ToString();
                }

                if (v is T t)
                {
                    return t;
                }

                return (T)Convert.ChangeType(v, typeof(T));
            }

            return default;
        }

        public static void SetMultipleProperties(this object target, object src, ServiceScope? ss = null)
        {
            ss ??= CEF.CurrentServiceScope;

            using (CEF.UseServiceScope(ss))
            {
                var iw = src.AsInfraWrapped(false);

                if (iw != null)
                {
                    foreach (var (name, _, _, _) in target.FastGetAllProperties(true, true))
                    {
                        if (iw.BagValuesOnly().TryGetValue(name, out var sv))
                        {
                            target.FastSetValue(name, sv);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Abstracts property value access to work with virtually any type of object, accessing a named property that's a reference type.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="o"></param>
        /// <param name="propName"></param>
        /// <returns></returns>
        public static T PropertyValue<T>(this object o, string propName)
        {
            var iw = o.AsInfraWrapped(true) ?? throw new CEFInvalidStateException(InvalidStateType.ObjectTrackingIssue);

            if (iw.HasProperty(propName))
            {
                var v = iw.GetValue(propName);

                if (v == null)
                {
                    return default!;
                }

                if (v is T t)
                {
                    return t;
                }

                throw new CEFInvalidStateException(InvalidStateType.DataTypeIssue, $"Invalid cast of type {typeof(T).Name}.");
            }

            return default!;
        }

        public static IEnumerable<ICEFInfraWrapper> AllAsInfraWrapped<T>(this IEnumerable<T> items) where T: class, new()
        {
            foreach (var i in items)
            {
                var iw = i.AsInfraWrapped();

                if (iw != null)
                {
                    yield return iw;
                }
            }
        }

        public static IEnumerable<dynamic> AllAsDynamic<T>(this IEnumerable<T> items) where T : class, new()
        {
            foreach (var i in items)
            {
                var d = i.AsDynamic();

                if (d != null)
                {
                    yield return d;
                }
            }
        }

        public static (int code, string message) AsString(this IEnumerable<(ValidationErrorCode error, string? message)> msgs, ValidationErrorCode? only = null, string concat = " ")
        {
            int code = 0;
            StringBuilder sb = new();

            foreach (var (error, message) in msgs)
            {
                if (!only.HasValue || (only.Value & error) != 0)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append(concat);
                    }

                    sb.Append(message);
                    code |= (int)error;
                }
            }

            return (-code, sb.ToString());
        }

        /// <summary>
        /// Given a potentially wrapped object, returns the base object type that it maps to. (E.g. an instance of a derived class from a base POCO object passed in would return the base POCO Type.)
        /// </summary>
        /// <param name="wrapped">Any object that's tracked within the current service scope.</param>
        /// <returns></returns>
        public static Type GetBaseType(this object wrapped)
        {
            if (wrapped == null)
            {
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(wrapped));
            }

            if (wrapped is ICEFInfraWrapper wrapper)
            {
                var wo = wrapper.GetWrappedObject();

                if (wo != null)
                {
                    wrapped = wo;
                }
            }

            if (wrapped is ICEFWrapper wrapper1)
            {
                return wrapper1.GetBaseType();
            }

            var wt = wrapped.GetType();

            if (_typeMap.ContainsKey(wt))
            {
                return _typeMap[wt];
            }

            var uw = CEF.CurrentServiceScope.GetWrapperOrTarget(wrapped);

            if (uw is ICEFWrapper wrapper2)
            {
                var rt = wrapper2.GetBaseType();
                _typeMap[wt] = rt;
                return rt;
            }

            if (uw == null)
            {
                // It's an error if the wrapped object presents itself as an IW object at this point!
                if (wrapped is ICEFInfraWrapper)
                {
                    throw new CEFInvalidStateException(InvalidStateType.ObjectTrackingIssue);
                }

                _typeMap[wt] = wt;
                return wt;
            }

            _typeMap[wt] = uw.GetType();
            return uw.GetType();
        }

        /// <summary>
        /// Similar to AsUnwrapped but will throw an exception if null would have been returned.
        /// </summary>
        /// <param name="wrapped">An object in the current service scope (can be wrapped or unwrapped).</param>
        /// <returns>The "least wrapped" instance of the input object.</returns>
        public static object MustUnwrap(this object wrapped)
        {
            return AsUnwrapped(wrapped) ?? throw new CEFInvalidStateException("Could not unwrap object. This indicates a likely programming issue.");
        }

        /// <summary>
        /// Returns the "least wrapped" version if the input (potentially) wrapped object.
        /// </summary>
        /// <param name="wrapped">An object in the current service scope (can be wrapped or unwrapped).</param>
        /// <returns>The "least wrapped" instance of the input object.</returns>
        public static object? AsUnwrapped(this object wrapped)
        {
            if (wrapped != null)
            {
                ICEFWrapper? w;

                if ((w = (wrapped as ICEFWrapper)) != null)
                {
                    var uw = w.GetCopyTo();

                    if (uw != null)
                    {
                        return uw;
                    }
                }

                if (wrapped is ICEFInfraWrapper iw)
                {
                    var wo = iw.GetWrappedObject();

                    if (wo != null)
                    {
                        w = wo as ICEFWrapper;

                        if (w != null)
                        {
                            wo = w.GetCopyTo();

                            if (wo == null)
                            {
                                wo = w;
                            }
                        }

                        return wo;
                    }
                }

                return wrapped;
            }

            return null;
        }

        /// <summary>
        /// Returns a wrapped version of the input object, if one is available.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="unwrapped">A potentially unwrapped object.</param>
        /// <returns></returns>
        public static T? AsWrapped<T>(this object unwrapped) where T : class, ICEFWrapper
        {
            return CEF.CurrentServiceScope.GetWrapperFor(unwrapped) as T;
        }

        /// <summary>
        /// Returns true if the underlying infra wrapper indicates the row state is not unchanged.
        /// </summary>
        /// <param name="iw">An infra wrapper object.</param>
        /// <returns></returns>
        public static bool IsDirty(this ICEFInfraWrapper iw)
        {
            if (iw != null)
            {
                return iw.GetRowState() != ObjectState.Unchanged;
            }

            return false;
        }

        /// <summary>
        /// Returns the JSON representation of the object. If it's an infrastructure wrapped object, used CEF rules, otherwise plain Newtonsoft serialization rules.
        /// </summary>
        /// <param name="o">Object to serialize.</param>
        /// <param name="mode">Serialization mode (applicable if an infrastructure wrapped object).</param>
        /// <returns>String representation of object.</returns>
        public static string? AsJSON(this object o, SerializationMode? mode = null)
        {
            if (o == null)
                return null;

            // Special case - if o is a session scope, we're asking to serialize everything in scope, as one big array of objects!
            if (o is ServiceScope scope)
            {
                return scope.GetScopeSerializationText(mode);
            }

            if (o is ICEFList list)
            {
                return list.GetSerializationText(mode);
            }

            if (o.AsInfraWrapped(false) is not ICEFSerializable iw)
            {
                return JsonConvert.SerializeObject(o);
            }

            CEF.CurrentServiceScope.ReconcileModifiedState(null);

            return iw.GetSerializationText(mode);
        }

        public static void AcceptAllChanges(this ICEFInfraWrapper iw)
        {
            WrappingHelper.NodeVisiter(iw, (iw2) =>
            {
                iw2.AcceptChanges();
            });
        }

        public static void AcceptAllChanges(this ICEFInfraWrapper iw, Type onlyForType)
        {
            if (onlyForType == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(onlyForType));

            WrappingHelper.NodeVisiter(iw, (iw2) =>
            {
                if (iw2.GetBaseType().Equals(onlyForType))
                {
                    iw2.AcceptChanges();
                }
            });
        }

        public static void AcceptAllChanges(this ICEFInfraWrapper iw, Func<ICEFInfraWrapper, bool> check)
        {
            if (check == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(check));

            WrappingHelper.NodeVisiter(iw, (iw2) =>
            {
                if (check(iw2))
                {
                    iw2.AcceptChanges();
                }
            });
        }

        public static string DictionaryKeyFromColumns(this ICEFInfraWrapper iw, IEnumerable<string> cols)
        {
            StringBuilder sb = new(128);

            foreach (var c in cols)
            {
                if (sb.Length > 0)
                {
                    sb.Append("~");
                }

                sb.Append(iw.GetValue(c));
            }

            return sb.ToString();
        }

        /// <summary>
        /// A target EntitySet is updated to look "the same" as a source DataView. Rows may be added, updated or deleted in the EntitySet based on the primary key set for type T.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="source">A source DataView containing data to be merged into target.</param>
        /// <param name="target">A collection of entities that will be updated to match the source DataView.</param>
        public static void ReconcileDataViewToEntitySet<T>(this DataView source, EntitySet<T> target) where T : class, new()
        {
            // A natural key must be available!
            var key = KeyService.ResolveKeyDefinitionForType(typeof(T));

            if (key == null || key.Count == 0)
            {
                throw new CEFInvalidStateException(InvalidStateType.BadParameterValue, $"Type {typeof(T).Name} does not have a key defined.");
            }

            var nonKeyCol = (from a in source.Table.Columns.Cast<DataColumn>() where !(from b in key where b == a.ColumnName select b).Any() select a);
            var ss = CEF.CurrentServiceScope;

            // Build a dictionary for faster lookup
            var setData = target.ToDictionary(key);

            // First pass for inserts, updates
            foreach (DataRowView drv in source)
            {
                StringBuilder sb = new(128);

                foreach (var k in key)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append("~");
                    }
                    sb.Append(drv[k]);
                }

                if (!setData.TryGetValue(sb.ToString(), out T entRow))
                {
                    entRow = target.Add();
                }

                var iw = entRow.AsInfraWrapped();

                if (iw != null)
                {
                    foreach (DataColumn dc in nonKeyCol)
                    {
                        var setter = ss.GetSetter(iw, dc.ColumnName);
                        setter.setter?.Invoke(drv[dc.ColumnName].CoerceDBNullableType(setter.type ?? dc.DataType));
                    }
                }
            }

            // Second pass for deletes - use a separate DV we can sort for fast lookup
            using DataView dv = new(source.Table, source.RowFilter, string.Join(",", key.ToArray()), source.RowStateFilter);

            foreach (var kvp in setData.ToList())
            {
                var iw = kvp.Value.AsInfraWrapped();

                if (iw != null)
                {
                    if (dv.Find((from a in key select iw.GetValue(a)).ToArray()) < 0)
                    {
                        CEF.DeleteObject(kvp.Value);
                        target.Remove(kvp.Value);
                    }
                }
            }
        }

        /// <summary>
        /// Creates a deep copy of the source object, returning it.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="src"></param>
        /// <returns></returns>
        public static T DeepCopyObject<T>(this T? src) where T : class, new()
        {
            var n = (T)(typeof(T).FastCreateNoParm() ?? throw new InvalidOperationException("Could not instantiate object."));

            if (src != null)
            {
                foreach (var (name, _, _, _) in typeof(T).FastGetAllProperties(true, true))
                {
                    n.FastSetValue(name, src.FastGetValue(name));
                }
            }

            return n;
        }

        /// <summary>
        /// Copies all shared properties from one instance to another instance of any arbitrary type.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="src"></param>
        /// <param name="dest"></param>
        public static void CopySharedTo(this object src, object dest)
        {
            if (src == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(src));

            if (dest == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(dest));

            // We can copy from non-nullable to nullable (always)
            foreach (var name in (from a in src.FastGetAllProperties(true, true)
                                  join b in dest.FastGetAllProperties(true, true) on a.name equals b.name
                                  where a.type == b.type || Nullable.GetUnderlyingType(b.type) == a.type
                                  select a.name))
            {
                dest.FastSetValue(name, src.FastGetValue(name));
            }

            // We can additionally copy from nullable to non-nullable, but only if the nullable actually holds a non-null value
            foreach (var fi in (from a in src.FastGetAllProperties(true, true)
                                  join b in dest.FastGetAllProperties(true, true) on a.name equals b.name
                                  where a.type != b.type && Nullable.GetUnderlyingType(a.type) == b.type
                                  let v = src.FastGetValue(a.name)
                                  where v != null
                                  select new { a.name, value = v }))
            {
                dest.FastSetValue(fi.name, fi.value);
            }
        }

        /// <summary>
        /// Copies all shared properties from one instance to another instance of any arbitrary type.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="src"></param>
        /// <param name="dest"></param>
        public static void CopySharedTo(this object src, object dest, bool isExclude, params string[] fields)
        {
            if (src == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(src));

            if (dest == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(dest));

            foreach (var name in (from a in src.FastGetAllProperties(true, true) join b in dest.FastGetAllProperties(true, true) on a.name equals b.name
                                  where (a.type == b.type || Nullable.GetUnderlyingType(b.type) == a.type) &&
                                    (fields?.Length == 0 || (isExclude && !fields.Contains(a.name)) || (!isExclude && fields.Contains(a.name)))
                                  select a.name))
            {
                dest.FastSetValue(name, src.FastGetValue(name));
            }

            foreach (var fi in (from a in src.FastGetAllProperties(true, true)
                                join b in dest.FastGetAllProperties(true, true) on a.name equals b.name
                                where (a.type != b.type && Nullable.GetUnderlyingType(a.type) == b.type) &&
                                    (fields?.Length == 0 || (isExclude && !fields.Contains(a.name)) || (!isExclude && fields.Contains(a.name)))
                                let v = src.FastGetValue(a.name)
                                where v != null
                                select new { a.name, value = v }))
            {
                dest.FastSetValue(fi.name, fi.value);
            }
        }

        /// <summary>
        /// Similar to CopySharedTo but is less restrictive about how empty strings are compared to null, causing less dirty state changes.
        /// </summary>
        /// <param name="src"></param>
        /// <param name="dest"></param>
        /// <param name="isExclude"></param>
        /// <param name="fields"></param>
        public static void CopySharedToNullifyEmptyStrings(this object src, object dest, bool isExclude, params string[] fields)
        {
            if (src == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(src));

            if (dest == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(dest));

            foreach (var info in (from a in src.FastGetAllProperties(true, true)
                                  join b in dest.FastGetAllProperties(true, true) on a.name equals b.name
                                  where (a.type == b.type || Nullable.GetUnderlyingType(b.type) == a.type) &&
                                    (fields?.Length == 0 || (isExclude && !fields.Contains(a.name)) || (!isExclude && fields.Contains(a.name)))
                                  select new { a.name, a.type }))
            {
                var v = src.FastGetValue(info.name);

                if (info.type == typeof(string) && string.IsNullOrEmpty(v?.ToString()))
                {
                    var dv = dest.FastGetValue(info.name);

                    if (string.IsNullOrEmpty(dv?.ToString()))
                    {
                        continue;
                    }

                    v = null;
                }

                dest.FastSetValue(info.name, v);
            }

            foreach (var fi in (from a in src.FastGetAllProperties(true, true)
                                join b in dest.FastGetAllProperties(true, true) on a.name equals b.name
                                where (a.type != b.type && Nullable.GetUnderlyingType(a.type) == b.type) &&
                                    (fields?.Length == 0 || (isExclude && !fields.Contains(a.name)) || (!isExclude && fields.Contains(a.name)))
                                let v = src.FastGetValue(a.name)
                                where v != null
                                select new { a.name, value = v }))
            {
                dest.FastSetValue(fi.name, fi.value);
            }
        }

        /// <summary>
        /// Returns a list of property names where shared property values differ between two instances of arbitrary types.
        /// </summary>
        /// <param name="src"></param>
        /// <param name="dest"></param>
        /// <param name="isExclude"></param>
        /// <param name="fields"></param>
        /// <returns></returns>
        public static IEnumerable<string> FindDifferentSharedValues(this object src, object dest)
        {
            if (src == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(src));

            if (dest == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(dest));

            foreach (var name in (from a in src.FastGetAllProperties(true, true)
                                  join b in dest.FastGetAllProperties(true, true) on a.name equals b.name
                                  where a.type == b.type || Nullable.GetUnderlyingType(b.type) == a.type
                                  select a.name))
            {
                if (!dest.FastGetValue(name).IsSame(src.FastGetValue(name)))
                {
                    yield return name;
                }
            }

            foreach (var fi in (from a in src.FastGetAllProperties(true, true)
                                join b in dest.FastGetAllProperties(true, true) on a.name equals b.name
                                where a.type != b.type && Nullable.GetUnderlyingType(a.type) == b.type
                                let v = src.FastGetValue(a.name)
                                where v != null
                                select new { a.name, value = v }))
            {
                if (!dest.FastGetValue(fi.name).IsSame(fi.value))
                {
                    yield return fi.name;
                }
            }
        }

        /// <summary>
        /// Copies all shared properties from one instance to another instance of any arbitrary type - except does not overwrite values that are not null/default in target.
        /// </summary>
        /// <param name="src"></param>
        /// <param name="dest"></param>
        public static void CopySharedToOnlyOverwriteDefault(this object src, object dest)
        {
            if (src == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(src));

            if (dest == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(dest));

            foreach (var name in (from a in src.FastGetAllProperties(true, true)
                                  join b in dest.FastGetAllProperties(true, true) on a.name equals b.name
                                  where a.type == b.type || Nullable.GetUnderlyingType(b.type) == a.type
                                  select a.name))
            {
                var sv = src.FastGetValue(name);
                var dv = dest.FastGetValue(name);

                if (sv.IsSame(dv))
                {
                    continue;
                }

                if (dv != null)
                {
                    var di = dv.GetType().FastCreateNoParm();

                    if (di == dv)
                    {
                        continue;
                    }
                }

                dest.FastSetValue(name, sv);
            }
        }

        /// <summary>
        /// Copies all properties from one instance to another instance of the same type.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="src"></param>
        /// <param name="dest"></param>
        public static void CopyToObject<T>(this T src, T dest) where T : class, new()
        {
            if (src == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(src));

            if (dest == null)
                throw new CEFInvalidStateException(InvalidStateType.ArgumentNull, nameof(dest));

            foreach (var (name, _, _, _) in typeof(T).FastGetAllProperties(true, true))
            {
                dest.FastSetValue(name, src.FastGetValue(name));
            }
        }

        /// <summary>
        /// Creates a DataTable with the same structure as the source EntitySet collection. Columns are determined based on properties (CLR and extended). Changes to the DataTable do NOT reflect back to the EntitySet instance.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="source"></param>
        /// <returns></returns>
        public static DataTable DeepCopyDataTable<T>(this EntitySet<T> source) where T : class, new()
        {
            List<(string name, Type type, bool nullable)> columns = new();

            if (source.Any())
            {
                // Use first row's properties (could include extended props)
                var iw = source.First().AsInfraWrapped();

                if (iw != null)
                {
                    columns.AddRange(from a in iw.GetAllValues(false, true)
                                     let pt = (from b in iw.GetAllPreferredTypes(false, true) where b.Key == a.Key select b.Value).FirstOrDefault() ?? (a.Value == null ? typeof(object) : a.Value.GetType())
                                     select (a.Key, pt, pt.IsGenericType && pt.GetGenericTypeDefinition() == typeof(Nullable<>)));
                }
            }
            else
            {
                // Use type's properties only, nothing else to go on
                columns.AddRange(from a in typeof(T).GetProperties() select (a.Name, a.PropertyType, a.PropertyType.IsGenericType && a.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>)));
            }

            DataTable dt = new();

            foreach (var (name, type, nullable) in columns)
            {
                if (nullable)
                {
                    dt.Columns.Add(name, Nullable.GetUnderlyingType(type));
                }
                else
                {
                    dt.Columns.Add(name, type);
                }
            }

            foreach (var i in source)
            {
                var iw = i.AsInfraWrapped();

                if (iw != null)
                {
                    var rs = iw.GetRowState();

                    if (rs != ObjectState.Deleted && rs != ObjectState.Unlinked)
                    {
                        var dr = dt.NewRow();

                        if (rs == ObjectState.Modified || rs == ObjectState.ModifiedPriority)
                        {
                            dr.AcceptChanges();
                        }

                        foreach (var v in iw.GetAllValues(true))
                        {
                            if (v.Value != null && dt.Columns.Contains(v.Key))
                            {
                                dr[v.Key] = v.Value;
                            }
                        }

                        dt.Rows.Add(dr);

                        if (rs == ObjectState.Unchanged)
                        {
                            dr.AcceptChanges();
                        }
                    }
                }
            }

            return dt;
        }

        /// <summary>
        /// Creates a DataView with the same structure as the source EntitySet collection. Columns are determined based on properties (CLR and extended). Changes to the DataView do NOT reflect back to the EntitySet instance.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="source"></param>
        /// <param name="sort"></param>
        /// <param name="filter"></param>
        /// <returns></returns>
        public static DataView DeepCopyDataView<T>(this EntitySet<T> source, string? sort = null, string? filter = null) where T : class, new()
        {
            var dv = DeepCopyDataTable(source).DefaultView;
            dv.Sort = sort;
            dv.RowFilter = filter;
            return dv;
        }

        /// <summary>
        /// Traverses object graph looking for cases where collections can be replaced with EntitySet.
        /// </summary>
        /// <param name="o">Starting object for traversal.</param>
        /// <param name="isNew">If true, assumes objects being added represent "new" rows to insert in the database.</param>
        public static void CreateLists(this object o, bool isNew = false)
        {
            if (o == null)
                return;

            foreach (var (name, type, _, _) in o.FastGetAllProperties(true, null))
            {
                if (o.FastPropertyReadable(name))
                {
                    var val = o.FastGetValue(name);

                    if (WrappingHelper.IsWrappableListType(type, val))
                    {
                        if (!(val is ICEFList))
                        {
                            var wrappedCol = WrappingHelper.CreateWrappingList(CEF.CurrentServiceScope, type, o, name);
                            o.FastSetValue(name, wrappedCol);

                            if (wrappedCol != null)
                            {
                                if (val != null)
                                {
                                    // Based on the above type checks, we know this thing supports IEnumerable
                                    var sValEnum = ((System.Collections.IEnumerable)val).GetEnumerator();

                                    while (sValEnum.MoveNext())
                                    {
                                        // Need to use non-generic method for this!
                                        var wi = CEF.CurrentServiceScope.InternalCreateAddBase(sValEnum.Current, isNew, null, null, null, null, true, false);

                                        if (wi != null)
                                        {
                                            wrappedCol.AddWrappedItem(wi);
                                        }
                                    }
                                }

                                ((ISupportInitializeNotification)wrappedCol).EndInit();
                            }
                        }
                    }
                }
            }
        }
#endif

    }
}
