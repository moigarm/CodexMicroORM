﻿/***********************************************************************
Copyright 2018 CodeX Enterprises LLC

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
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Collections;
using CodexMicroORM.Core.Helper;
using System.Threading;
using CodexMicroORM.Core.Collections;

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
        private static ConcurrentDictionary<Type, Type> _directTypeMap = new ConcurrentDictionary<Type, Type>(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);
        private static ConcurrentDictionary<Type, string> _cachedTypeMap = new ConcurrentDictionary<Type, string>(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);
        private static ConcurrentDictionary<Type, object> _defValMap = new ConcurrentDictionary<Type, object>(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);
        private static ConcurrentDictionary<Type, bool> _isWrapListCache = new ConcurrentDictionary<Type, bool>(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);
        private static ConcurrentDictionary<Type, IDictionary<string, Type>> _propCache = new ConcurrentDictionary<Type, IDictionary<string, Type>>(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);
        private static ConcurrentDictionary<Type, bool> _sourceValTypeOk = new ConcurrentDictionary<Type, bool>(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);
        private static SlimConcurrentDictionary<string, Type> _typeByName = new SlimConcurrentDictionary<string, Type>();
        private static long _copyNesting = 0;


        public static object GetDefaultForType(Type t)
        {
            if (t == null)
                return null;

            if (_defValMap.TryGetValue(t, out object val))
            {
                return val;
            }

            MethodInfo mi = typeof(WrappingHelper).GetMethod("InternalGetDefaultForType", BindingFlags.NonPublic | BindingFlags.InvokeMethod | BindingFlags.Static);
            mi = mi.MakeGenericMethod(t);
            var val2 = mi.Invoke(null, new object[] { });
            _defValMap[t] = val2;
            return val2;
        }

        private static object InternalGetDefaultForType<T>()
        {
            return default(T);
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
            if (_isWrapListCache.TryGetValue(sourceType, out bool v))
            {
                return v;
            }

            if (sourceType.IsValueType || !sourceType.IsConstructedGenericType || sourceType.GenericTypeArguments?.Length != 1 || sourceType.GenericTypeArguments[0].IsValueType)
            {
                _isWrapListCache[sourceType] = false;
                return false;
            }

            if (sourceVal != null && sourceVal.GetType().Name.StartsWith(Globals.PreferredEntitySetType.Name))
            {
                // Should already be wrapped when added to an EntitySet
                _isWrapListCache[sourceType] = false;
                return false;
            }

            var v2 = (sourceType.Name.StartsWith("IList`") || sourceType.Name.StartsWith("ICollection`") || sourceType.Name.StartsWith("IEnumerable`"));
            _isWrapListCache[sourceType] = v2;
            return v2;
        }

        internal static ICEFList CreateWrappingList(ServiceScope ss, Type sourceType, object host, string propName)
        {
            var setWrapType = Globals.PreferredEntitySetType.MakeGenericType(sourceType.GenericTypeArguments[0]);
            var wrappedCol = setWrapType.FastCreateNoParm() as ICEFList;

            ((ISupportInitializeNotification)wrappedCol).BeginInit();
            ((ICEFList)wrappedCol).Initialize(ss, host, host.GetBaseType().Name, propName);
            return wrappedCol;
        }

        internal static void CopyParsePropertyValues(IDictionary<string, object> sourceProps, object source, object target, bool isNew, ServiceScope ss, IDictionary<object, object> visits, bool justTraverse)
        {
            // Recursively parse property values for an object graph. This not only adjusts collection types to be trackable concrete types, but registers child objects into the current service scope.

            _propCache.TryGetValue(target.GetType(), out var dic);

            if (dic == null)
            {
                dic = (from a in target.FastGetAllProperties(true, true) select new { Name = a.name, PropertyType = a.type }).ToDictionary((p) => p.Name, (p) => p.PropertyType);
                _propCache[target.GetType()] = dic;
            }

            var iter = sourceProps == null ? 
                (from t in dic select (t.Key, target.FastGetValue(t.Key), t.Value)) 
                : (from s in sourceProps from t in dic where s.Key == t.Key select (s.Key, s.Value, t.Value ));

            var maxdop = Globals.EnableParallelPropertyParsing && Environment.ProcessorCount > 4 && iter.Count() >= Environment.ProcessorCount ? Environment.ProcessorCount >> 2 : 1;

            Interlocked.Add(ref _copyNesting, maxdop);

            try
            {
                Action<(string PropName, object SourceVal, Type TargPropType)> a = ((string PropName, object SourceVal, Type TargPropType) info) =>
                {
                    object wrapped = null;

                    if (ss != null && IsWrappableListType(info.TargPropType, info.SourceVal))
                    {
                        ICEFList wrappedCol = null;

                        if (ss.Settings.InitializeNullCollections || info.SourceVal != null)
                        {
                            // This by definition represents CHILDREN
                            // Use an observable collection we control - namely EntitySet
                            wrappedCol = CreateWrappingList(ss, info.TargPropType, target, info.PropName);
                            target.FastSetValue(info.PropName, wrappedCol);
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
                                    wrapped = ss.InternalCreateAddBase(sValEnum.Current, isNew, null, null, null, visits);
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
                        if (ss != null && info.SourceVal != null)
                        {
                            var svt = info.SourceVal.GetType();
                            bool svtok;

                            if (!_sourceValTypeOk.TryGetValue(svt, out svtok))
                            {
                                svtok = !svt.IsValueType && svt != typeof(string) && KeyService.ResolveKeyDefinitionForType(svt).Any();
                                _sourceValTypeOk[svt] = svtok;
                            }

                            if (svtok)
                            {
                                if (visits.ContainsKey(info.SourceVal))
                                {
                                    wrapped = visits[info.SourceVal] ?? info.SourceVal;
                                }
                                else
                                {
                                    wrapped = ss.InternalCreateAddBase(info.SourceVal, isNew, null, null, null, visits);
                                }

                                if (wrapped != null)
                                {
                                    target.FastSetValue(info.PropName, wrapped);
                                }
                            }
                            else
                            {
                                if (!justTraverse)
                                {
                                    target.FastSetValue(info.PropName, info.SourceVal);
                                }
                            }
                        }
                        else
                        {
                            if (!justTraverse)
                            {
                                target.FastSetValue(info.PropName, info.SourceVal);
                            }
                        }
                    }
                };

                int resdop = Interlocked.Read(ref _copyNesting) > 12 ? 1 : maxdop;

                if (resdop == 1)
                {
                    foreach (var info in iter)
                    {
                        a.Invoke(info);
                    }
                }
                else
                {
                    Parallel.ForEach(iter, new ParallelOptions() { MaxDegreeOfParallelism = resdop }, (info) =>
                    {
                        using (CEF.UseServiceScope(ss))
                        {
                            a.Invoke(info);
                        }
                    });
                }
            }
            finally
            {
                Interlocked.Add(ref _copyNesting, -maxdop);
            }
        }

        internal static void CopyPropertyValuesObject(object source, object target, bool isNew, ServiceScope ss, IDictionary<string, object> removeIfSet, IDictionary<object, object> visits)
        {
            Dictionary<string, object> props = new Dictionary<string, object>(Globals.DefaultDictionaryCapacity);

            var pkFields = KeyService.ResolveKeyDefinitionForType(source.GetBaseType());

            foreach (var pi in source.FastGetAllProperties(true, true))
            {
                // For new rows, ignore the PK since it should be assigned by key service
                if ((!isNew || !pkFields.Contains(pi.name)))
                {
                    props[pi.name] = source.FastGetValue(pi.name);

                    if (removeIfSet != null && removeIfSet.ContainsKey(pi.name))
                    {
                        removeIfSet.Remove(pi.name);
                    }
                }
            }

            CopyParsePropertyValues(props, source, target, isNew, ss, visits, false);
        }

        private static ICEFWrapper InternalCreateWrapper(WrappingSupport need, WrappingAction action, bool isNew, object o, bool missingAllowed, ServiceScope ss, IDictionary<object, ICEFWrapper> wrappers, IDictionary<string, object> props, IDictionary<string, Type> types, IDictionary<object, object> visits)
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

                if (!_typeByName.TryGetValue(fqn, out Type t))
                {
                    t = Type.GetType(fqn, false, true);
                    _typeByName[fqn] = t;
                }

                if (t == null)
                {
                    if (missingAllowed)
                    {
                        return null;
                    }
                    throw new CEFInvalidOperationException($"Failed to create wrapper object of type {fqn} for object of type {o.GetType().Name}.");
                }

                // Relies on parameterless constructor
                var wrapper = t.FastCreateNoParm();

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

        public static ICEFWrapper CreateWrapper(WrappingSupport need, WrappingAction action, bool isNew, object o, ServiceScope ss, IDictionary<string, object> props = null, IDictionary<string, Type> types = null, IDictionary<object, object> visits = null)
        {
            return InternalCreateWrapper(need, action, isNew, o, Globals.MissingWrapperAllowed, ss, new Dictionary<object, ICEFWrapper>(Globals.DefaultDictionaryCapacity), props, types, visits ?? new Dictionary<object, object>(Globals.DefaultDictionaryCapacity));
        }

        public static ICEFInfraWrapper CreateInfraWrapper(WrappingSupport need, WrappingAction action, bool isNew, object o, ObjectState? initState, IDictionary<string, object> props, IDictionary<string, Type> types)
        {
            // Goal is to provision the lowest overhead object based on need!
            ICEFInfraWrapper infrawrap = null;

            if (action != WrappingAction.NoneOrProvisionedAlready)
            {
                if ((o is INotifyPropertyChanged) || (need & WrappingSupport.Notifications) == 0)
                {
                    if ((need & WrappingSupport.DataErrors) != 0)
                    {
                        infrawrap = new DynamicWithValuesBagErrors(o, initState.GetValueOrDefault(isNew ? ObjectState.Added : ObjectState.Unchanged), props, types);
                    }
                    else
                    {
                        if ((need & WrappingSupport.OriginalValues) != 0)
                        {
                            infrawrap = new DynamicWithValuesAndBag(o, initState.GetValueOrDefault(isNew ? ObjectState.Added : ObjectState.Unchanged), props, types);
                        }
                        else
                        {
                            infrawrap = new DynamicWithBag(o, props, types);
                        }
                    }
                }
                else
                {
                    infrawrap = new DynamicWithAll(o, initState.GetValueOrDefault(isNew ? ObjectState.Added : ObjectState.Unchanged), props, types);
                }
            }

            return infrawrap;
        }

        /// <summary>
        /// Performs a depth-first traversal of object graph, invoking a delegate of choice for each infrastructure wrapper found.
        /// </summary>
        /// <param name="iw">Root object to start traversal.</param>
        /// <param name="toRun">A delegate to invoke for each infrastructure wrapper found.</param>
        public static void NodeVisiter(ICEFInfraWrapper iw, Action<ICEFInfraWrapper> toRun)
        {
            InternalNodeVisiter(iw, toRun, new ConcurrentDictionary<object, bool>(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity));
        }

        private static void InternalNodeVisiter(ICEFInfraWrapper iw, Action<ICEFInfraWrapper> toRun, IDictionary<object, bool> visits)
        {
            if (visits.ContainsKey(iw))
            {
                return;
            }

            visits[iw] = true;

            var av = (from a in iw.GetAllValues() where a.Value != null && !a.Value.GetType().IsValueType && !a.Value.GetType().FullName.StartsWith("System.") select a).ToList();
            var maxdop = Globals.EnableParallelPropertyParsing && Environment.ProcessorCount > 4 && av.Count() > Environment.ProcessorCount ? Environment.ProcessorCount >> 2 : 1;

            Parallel.ForEach(av, new ParallelOptions() { MaxDegreeOfParallelism = maxdop }, (kvp) =>
            {
                var asEnum = kvp.Value as IEnumerable;

                if (asEnum != null)
                {
                    var sValEnum = asEnum.GetEnumerator();

                    while (sValEnum.MoveNext())
                    {
                        var iw2 = CEF.CurrentServiceScope.GetOrCreateInfra(sValEnum.Current, false);

                        if (iw2 != null)
                        {
                            InternalNodeVisiter(iw2, toRun, visits);
                        }
                    }
                }
                else
                {
                    // If it's a tracked object, recurse
                    var iw2 = CEF.CurrentServiceScope.GetOrCreateInfra(kvp.Value, false);

                    if (iw2 != null)
                    {
                        InternalNodeVisiter(iw2, toRun, visits);
                    }
                }
            });

            toRun.Invoke(iw);
        }
    }
}
