﻿/***********************************************************************
Copyright 2017 CodeX Enterprises LLC

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
***********************************************************************/
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Reflection;
using System.Data;
using System.ComponentModel;
using System.Linq;

namespace CodexMicroORM.Core.Services
{
    /// <summary>
    /// Wrappers are specialized infrastructure objects that extend the capabilities of "regular wrappers". (Regular wrappers are typically the code generated variety which may add some services but not everything needed by all services.)
    /// The main benefit of "regular wrappers": strong typing and intellisense. This extends to use of stored procedures where wrappers for calls can be included against the regular wrapper infrastructure (although we could as easily create a static library of calls, too, which offers similar benefits).
    /// You may choose not to use any regular wrappers, so you might have only your POCO objects and infra(structure) wrappers.
    /// You may choose to use poco objects, regular wrappers, AND infra wrappers. (Use of infra wrappers is transparent - you should only care about your poco in and some cases, your regular wrappers for data binding.)
    /// You may choose to treat your generated regular wrappers as if your poco objects (i.e. you're fine with using what's generated from the database: database-first design); you'll likely also use infra wrappers under the covers too unless you build very heavy biz objects and advertise this capability.
    /// You may choose to just work directly with infra wrappers only. I personally dislike this: no strong typing means schema changes can cut you.
    /// </summary>
    internal static class WrappingHelper
    {
        private static ConcurrentDictionary<Type, Type> _directTypeMap = new ConcurrentDictionary<Type, Type>();
        private static ConcurrentDictionary<Type, string> _cachedTypeMap = new ConcurrentDictionary<Type, string>();

        public static void RegisterTypeMap<T, W>()
        {
            if (!(typeof(W) is ICEFWrapper))
            {
            }

            _directTypeMap[typeof(T)] = typeof(W);
        }

        private static string GetFullyQualifiedWrapperName(object o)
        {
            var ot = o?.GetType() ?? throw new ArgumentNullException("o");

            if (_directTypeMap.ContainsKey(ot))
            {
                return _directTypeMap[ot].FullName;
            }

            if (_cachedTypeMap.ContainsKey(ot))
            {
                return _cachedTypeMap[ot];
            }

            string cn = null;

            if (!string.IsNullOrEmpty(Globals.WrapperClassNamePattern))
            {
                cn = string.Format(Globals.WrapperClassNamePattern, ot.Name);
            }
            else
            {
                cn = ot.Name;
            }

            string ns = null;

            if (!string.IsNullOrEmpty(Globals.WrappingClassNamespace))
            {
                ns = string.Format(Globals.WrappingClassNamespace, ot.Namespace);
            }
            else
            {
                ns = ot.Namespace;
            }

            string ass = null;

            if (!string.IsNullOrEmpty(Globals.WrapperClassAssembly))
            {
                ass = string.Format(Globals.WrapperClassAssembly, ot.Assembly.GetName().Name);
            }
            else
            {
                ass = ot.Assembly.GetName().Name;
            }

            var fullName = $"{ns}.{cn}, {ass}";

            _cachedTypeMap[ot] = fullName;

            return fullName;
        }

        internal static bool IsWrappableListType(Type sourceType, object sourceVal)
        {
            if (sourceVal != null && sourceVal.GetType().Name.StartsWith("EntitySet`"))
            {
                // Should already be wrapped when added to an EntitySet
                return false;
            }

            return (sourceType.IsConstructedGenericType 
                && sourceType.GenericTypeArguments?.Length == 1 
                && !sourceType.GenericTypeArguments[0].IsValueType 
                && (sourceType.Name.StartsWith("IList`") || sourceType.Name.StartsWith("ICollection`") || sourceType.Name.StartsWith("IEnumerable`")));
        }

        internal static ICEFList CreateWrappingList(ServiceScope ss, Type sourceType, object host, string propName)
        {
            string setWrapType = $"CodexMicroORM.Core.Services.EntitySet`1[[{sourceType.GenericTypeArguments[0].AssemblyQualifiedName}]], {Assembly.GetExecutingAssembly().GetName().FullName}";
            var wrappedCol = Activator.CreateInstance(Type.GetType(setWrapType)) as ICEFList;

            ((ISupportInitializeNotification)wrappedCol).BeginInit();
            ((ICEFList)wrappedCol).Initialize(ss, host, host.GetBaseType().Name, propName);
            return wrappedCol;
        }

        internal static void CopyParsePropertyValues(IDictionary<string, object> sourceProps, object source, object target, bool isNew, ServiceScope ss, IDictionary<object, object> visits)
        {
            // Recursively parse property values for an object graph. This not only adjusts collection types to be trackable concrete types, but registers child objects into the current service scope.

            var iter = sourceProps == null ? (from t in target.GetType().GetProperties() where t.CanWrite select new { PropName = t.Name, SourceVal = t.GetValue(target), Target = t, TargPropType = t.PropertyType }) 
                : (from s in sourceProps from t in target.GetType().GetProperties() where s.Key == t.Name && t.CanWrite select new { PropName = s.Key, SourceVal = s.Value, Target = t, TargPropType = t.PropertyType });

            foreach (var info in iter)
            {
                object wrapped = null;

                if (ss != null && IsWrappableListType(info.TargPropType, info.SourceVal))
                {
                    ICEFList wrappedCol = null;

                    if (ss.Settings.InitializeNullCollections || info.SourceVal != null)
                    {
                        // This by definition represents CHILDREN
                        // Use an observable collection we control - namely EntitySet
                        wrappedCol = CreateWrappingList(ss, info.TargPropType, target, info.Target.Name);
                        info.Target.SetValue(target, wrappedCol);
                    }
                    else
                    {
                        wrappedCol = info.SourceVal as ICEFList;
                    }

                    // Merge any existing data into the collection - as we do this, recursively construct wrappers!
                    if (info.SourceVal != null && wrappedCol != null)
                    {
                        // Based on the above type checks, we know this thing supports IEnumerable
                        var sValEnum = ((System.Collections.IEnumerable)info.SourceVal).GetEnumerator();

                        while (sValEnum.MoveNext())
                        {
                            if (visits.ContainsKey(sValEnum.Current))
                            {
                                wrapped = visits[sValEnum.Current] ?? sValEnum.Current;
                            }
                            else
                            {
                                wrapped = ss.InternalCreateAddBase(sValEnum.Current, isNew, null, null, visits);
                            }

                            wrappedCol.AddWrappedItem(wrapped);
                        }
                    }

                    if (ss.Settings.InitializeNullCollections || info.SourceVal != null)
                    {
                        ((ISupportInitializeNotification)wrappedCol).EndInit();
                    }
                }
                else
                {
                    // If the type is a ref type that we manage, then this property represents a PARENT and we should replace/track it (only if we have a PK for it: without one, can't be tracked)
                    if (ss != null && info.SourceVal != null && !info.SourceVal.GetType().IsValueType && info.SourceVal.GetType() != typeof(string) && KeyService.ResolveKeyDefinitionForType(info.SourceVal.GetType()).Any())
                    {
                        if (visits.ContainsKey(info.SourceVal))
                        {
                            wrapped = visits[info.SourceVal] ?? info.SourceVal;
                        }
                        else
                        {
                            wrapped = ss.InternalCreateAddBase(info.SourceVal, isNew, null, null, visits);
                        }

                        if (wrapped != null)
                        {
                            info.Target.SetValue(target, wrapped);
                        }
                    }
                    else
                    {
                        info.Target.SetValue(target, info.SourceVal);
                    }
                }
            }
        }

        internal static void CopyPropertyValuesObject(object source, object target, bool isNew, ServiceScope ss, IDictionary<string, object> removeIfSet, IDictionary<object, object> visits)
        {
            Dictionary<string, object> props = new Dictionary<string, object>();

            var pkFields = KeyService.ResolveKeyDefinitionForType(source.GetBaseType());

            foreach (var pi in source.GetType().GetProperties())
            {
                // For new rows, ignore the PK since it should be assigned by key service
                if (!isNew || !pkFields.Contains(pi.Name))
                {
                    props[pi.Name] = pi.GetValue(source);
                }
            }

            CopyParsePropertyValues(props, source, target, isNew, ss, visits);

            if (removeIfSet != null)
            {
                foreach (var k in (from a in removeIfSet join b in props on a.Key equals b.Key select a.Key))
                {
                    removeIfSet.Remove(k);
                }
            }
        }

        private static ICEFWrapper InternalCreateWrapper(WrappingSupport need, WrappingAction action, bool isNew, object o, bool missingAllowed, ServiceScope ss, IDictionary<object, ICEFWrapper> wrappers, IDictionary<string, object> props, IDictionary<object, object> visits)
        {
            // Try to not duplicate wrappers: return one if previously generated in this parsing instance
            if (wrappers.ContainsKey(o))
            {
                return wrappers[o];
            }

            ICEFWrapper replwrap = null;

            if (Globals.DefaultWrappingAction == WrappingAction.PreCodeGen)
            {
                var fqn = GetFullyQualifiedWrapperName(o);

                if (string.IsNullOrEmpty(fqn))
                {
                    throw new CEFInvalidOperationException($"Failed to determine name of wrapper class for object of type {o.GetType().Name}.");
                }

                var t = Type.GetType(fqn, false, true);

                if (t == null)
                {
                    if (missingAllowed)
                    {
                        return null;
                    }
                    throw new CEFInvalidOperationException($"Failed to create wrapper object of type {fqn} for object of type {o.GetType().Name}.");
                }

                // Relies on parameterless constructor
                var wrapper = Activator.CreateInstance(t);

                if (wrapper == null)
                {
                    if (missingAllowed)
                    {
                        return null;
                    }
                    throw new CEFInvalidOperationException($"Failed to create wrapper object of type {fqn} for object of type {o.GetType().Name}.");
                }

                if (!(wrapper is ICEFWrapper))
                {
                    if (missingAllowed)
                    {
                        return null;
                    }
                    throw new CEFInvalidOperationException($"Wrapper object of type {fqn} for object of type {o.GetType().Name} does not implement ICEFWrapper.");
                }

                visits[o] = wrapper;

                replwrap = (ICEFWrapper)wrapper;

                // Effectively presents all current values - we assume codegen has properly implemented use of storage
                replwrap.SetCopyTo(o);

                // Deep copy of properties on this object
                CopyPropertyValuesObject(o, replwrap, isNew, ss, null, visits);
            }

            return replwrap;
        }

        public static ICEFWrapper CreateWrapper(WrappingSupport need, WrappingAction action, bool isNew, object o, ServiceScope ss, IDictionary<string, object> props = null, IDictionary<object, object> visits = null)
        {
            return InternalCreateWrapper(need, action, isNew, o, Globals.MissingWrapperAllowed, ss, new Dictionary<object, ICEFWrapper>(), props, visits ?? new Dictionary<object, object>());
        }

        public static ICEFInfraWrapper CreateInfraWrapper(WrappingSupport need, WrappingAction action, bool isNew, object o, DataRowState? initState, IDictionary<string, object> props)
        {
            // Goal is to provision the lowest overhead object based on need!
            ICEFInfraWrapper infrawrap = null;

            if (action != WrappingAction.NoneOrProvisionedAlready)
            {
                if ((o is INotifyPropertyChanged) || (need & WrappingSupport.Notifications) == 0)
                {
                    if ((need & WrappingSupport.OriginalValues) != 0)
                    {
                        infrawrap = new DynamicWithValuesAndBag(o, initState.GetValueOrDefault(isNew ? DataRowState.Added : DataRowState.Unchanged), props);
                    }
                    else
                    {
                        infrawrap = new DynamicWithBag(o, props);
                    }
                }
                else
                {
                    infrawrap = new DynamicWithAll(o, initState.GetValueOrDefault(isNew ? DataRowState.Added : DataRowState.Unchanged), props);
                }
            }

            return infrawrap;
        }
    }
}
