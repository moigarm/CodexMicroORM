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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using CodexMicroORM.Core.Collections;
using System.Collections.Immutable;
using CodexMicroORM.Core.Helper;
using System.Diagnostics;
using System.Threading;

namespace CodexMicroORM.Core.Services
{
    public class KeyService : ICEFKeyHost
    {
        public const char SHADOW_PROP_PREFIX = '\\';

        #region "Internal state"

        private static long _lowLongKey = long.MinValue;
        private static int _lowIntKey = int.MinValue;

        // Populated during startup, represents all object primary keys we track - should be eastablished prior to relationships!
        private static ConcurrentDictionary<Type, IList<string>> _primaryKeys = new ConcurrentDictionary<Type, IList<string>>(Globals.DefaultCollectionConcurrencyLevel, Globals.DefaultDictionaryCapacity);

        // Populated during startup, represents all object relationships we track
        private static ConcurrentIndexedList<TypeChildRelationship> _relations = new ConcurrentIndexedList<TypeChildRelationship>(
            nameof(TypeChildRelationship.ParentType),
            nameof(TypeChildRelationship.ChildType),
            nameof(TypeChildRelationship.FullParentChildPropertyName),
            nameof(TypeChildRelationship.FullChildParentPropertyName));


        #endregion

        public KeyService()
        {
        }

        #region "Public state"

        public static bool DefaultPrimaryKeysCanBeDBAssigned
        {
            get;
            set;
        } = true;

        public static MergeBehavior DefaultMergeBehavior
        {
            get;
            set;
        } = Globals.DefaultMergeBehavior;

        #endregion

        #region "Static methods"

        public static TypeChildRelationship GetMappedPropertyByFieldName(string fn)
        {
            var pcr = _relations.GetFirstByName(nameof(TypeChildRelationship.FullParentChildPropertyName), fn);

            if (pcr != null)
            {
                return pcr;
            }

            return _relations.GetFirstByName(nameof(TypeChildRelationship.FullChildParentPropertyName), fn);
        }

        /// <summary>
        /// For type T, register one or more properties that represent its primary key (unique, non-null).
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="fields"></param>
        public static void RegisterKey<T>(params string[] fields)
        {
            if (fields == null || fields.Length == 0)
                throw new ArgumentException("Fields must be non-blank.");

            _primaryKeys[typeof(T)] = fields;

            CEF.RegisterForType<T>(new KeyService());
        }

        /// <summary>
        /// For parent type T, register a relationship to a child entity (optional child role name and property accessor names).
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="relations"></param>
        public static void RegisterRelationship<T>(params TypeChildRelationship[] relations)
        {
            if (relations == null || relations.Length == 0)
                throw new ArgumentException("Relations must be non-blank.");

            foreach (var r in relations)
            {
                IList<string> parentPk = null;

                if (!_primaryKeys.TryGetValue(typeof(T), out parentPk))
                {
                    throw new CEFInvalidOperationException($"You need to define the primary key for type {typeof(T).Name} prior to establishing a relationship using it.");
                }

                r.ParentKey = parentPk;
                r.ParentType = typeof(T);

                _relations.Add(r);
            }

            CEF.RegisterForType<T>(new KeyService());
        }

        public static IList<string> ResolveKeyDefinitionForType(Type t)
        {
            if (_primaryKeys.TryGetValue(t, out IList<string> pk))
            {
                return pk;
            }
            else
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Returns all possible child role names associated with this child type.
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        private static IList<string> ResolveChildRolesForType(Type t)
        {
            return (from a in _relations.GetAllByName(nameof(TypeChildRelationship.ChildType), t) select a.ChildRoleName ?? a.ParentKey).SelectMany((p) => p).ToList();
        }

        private static IEnumerable<object> InternalGetParentObjects(KeyServiceState state, ServiceScope ss, object o, RelationTypes types, HashSet<object> visits)
        {
            var uw = o.AsUnwrapped();

            if (visits.Contains(uw))
            {
                yield break;
            }

            var to = ss.Objects.GetFirstByName(nameof(ServiceScope.TrackedObject.Target), uw);

            if (to != null)
            {
                visits.Add(to.GetTarget());

                foreach (var fk in state.AllParents(uw))
                {
                    foreach (var n in InternalGetParentObjects(state, ss, fk.Parent.GetTarget(), types, visits))
                    {
                        yield return n;
                    }

                    yield return fk.Parent.GetInfraWrapperTarget();
                }

                if ((types & RelationTypes.Children) != 0)
                {
                    foreach (var fk in state.AllChildren(uw))
                    {
                        var ch = fk.Child.GetTarget();

                        if (!visits.Contains(ch))
                        {
                            yield return fk.Child.GetInfraWrapperTarget();

                            foreach (var nc in InternalGetChildObjects(state, ss, ch, types, visits))
                            {
                                yield return nc;
                            }
                        }
                    }
                }
            }
        }

        private static IEnumerable<object> InternalGetChildObjects(KeyServiceState state, ServiceScope ss, object o, RelationTypes types, HashSet<object> visits)
        {
            var uw = o.AsUnwrapped();

            if (visits.Contains(uw))
            {
                yield break;
            }

            var to = ss.Objects.GetFirstByName(nameof(ServiceScope.TrackedObject.Target), uw);

            if (to != null)
            {
                var cur = to.GetTarget();

                visits.Add(cur);

                foreach (var fk in state.AllChildren(uw))
                {
                    foreach (var n in InternalGetChildObjects(state, ss, fk.Child.GetTarget(), types, visits))
                    {
                        yield return n;
                    }

                    yield return fk.Child.GetInfraWrapperTarget();
                }

                if ((types & RelationTypes.Parents) != 0)
                {
                    foreach (var fk in state.AllParents(uw))
                    {
                        var par = fk.Parent.GetTarget();

                        if (!visits.Contains(par))
                        {
                            yield return fk.Parent.GetInfraWrapperTarget();

                            foreach (var np in InternalGetParentObjects(state, ss, par, types, visits))
                            {
                                yield return np;
                            }
                        }
                    }
                }
            }
        }

        private static void InternalGetChildTypes(Type t, IDictionary<Type, IList<string>> visits, bool all)
        {
            if (visits.ContainsKey(t))
            {
                return;
            }

            var children = _relations.GetAllByName(nameof(TypeChildRelationship.ParentType), t);

            foreach (var child in (from a in children select a.ChildType).Distinct())
            {
                if (_primaryKeys.TryGetValue(child, out IList<string> pk))
                {
                    visits[child] = pk;
                }

                if (all)
                {
                    InternalGetChildTypes(child, visits, all);
                }
            }
        }

        private static void InternalGetParentTypes(Type t, IDictionary<Type, IList<string>> visits, bool all)
        {
            if (visits.ContainsKey(t))
            {
                return;
            }

            var parents = _relations.GetAllByName(nameof(TypeChildRelationship.ChildType), t);

            foreach (var parent in (from a in parents select a.ParentType).Distinct())
            {
                if (_primaryKeys.TryGetValue(parent, out IList<string> pk))
                {
                    visits[parent] = pk;
                }

                if (all)
                {
                    InternalGetParentTypes(parent, visits, all);
                }
            }
        }

        #endregion

        public IEnumerable<TypeChildRelationship> GetRelationsForChild(Type childType) => _relations.GetAllByName(nameof(TypeChildRelationship.ChildType), childType);

        IList<Type> ICEFService.RequiredServices() => null;

        Type ICEFService.IdentifyStateType(object o, ServiceScope ss, bool isNew) => typeof(KeyServiceState);

        WrappingSupport ICEFService.IdentifyInfraNeeds(object o, object replaced, ServiceScope ss, bool isNew)
        {
            // If you're using shadow properties, you definitely need a property bag and notification to know when a key might be changed/assigned
            if (Globals.UseShadowPropertiesForNew)
            {
                return WrappingSupport.PropertyBag | WrappingSupport.Notifications;
            }

            var need = WrappingSupport.None;

            if (o != null)
            {
                // Need prop bag only if does not contain key fields or child role names, plus require notifications to track changes in these values
                foreach (var field in ResolveKeyDefinitionForType(o.GetBaseType()).Union(ResolveChildRolesForType(o.GetBaseType())))
                {
                    if (((object)replaced ?? o).GetType().GetProperty(field) == null)
                    {
                        need = WrappingSupport.PropertyBag | WrappingSupport.Notifications;
                        break;
                    }
                }
            }

            return need;
        }

        void ICEFService.FinishSetup(ServiceScope.TrackedObject to, ServiceScope ss, bool isNew, IDictionary<string, object> props, ICEFServiceObjState state)
        {
            var uw = to.GetTarget();

            if (uw == null)
                return;

            var pkFields = ResolveKeyDefinitionForType(uw.GetBaseType());

            // Assign keys - new or not, we rely on unqiue values
            if (isNew)
            {
                foreach (var k in pkFields)
                {
                    // Don't overwrite properties that we may have copied!
                    if (props == null || !props.ContainsKey(k))
                    {
                        var ks = ss.GetSetter(uw, k);

                        // Shadow props in use, we rely on the above for the data type, but we'll be updating the shadow prop instead
                        var kssp = (!Globals.UseShadowPropertiesForNew || (ks.type != null && ks.type.Equals(typeof(Guid))) ? (null, null) : ss.GetSetter(uw, SHADOW_PROP_PREFIX + k));

                        if ((kssp.setter ?? ks.setter) != null)
                        {
                            var keyType = (ks.type ?? kssp.type);

                            if (keyType == null)
                            {
                                keyType = Globals.DefaultKeyType;
                            }

                            if (keyType.Equals(typeof(int)))
                            {
                                (kssp.setter ?? ks.setter).Invoke(System.Threading.Interlocked.Increment(ref _lowIntKey));
                            }
                            else
                            {
                                if (keyType.Equals(typeof(long)))
                                {
                                    (kssp.setter ?? ks.setter).Invoke(System.Threading.Interlocked.Increment(ref _lowLongKey));
                                }
                                else
                                {
                                    // Only do this if no current value assigned
                                    var kg = ss.GetGetter(uw, k);

                                    if (kg.getter != null)
                                    {
                                        var cv = kg.getter();

                                        if (keyType.Equals(typeof(Guid)))
                                        {
                                            if (cv == null || Guid.Empty.Equals(cv))
                                            {
                                                (kssp.setter ?? ks.setter).Invoke(Guid.NewGuid());
                                            }
                                        }
                                        else
                                        {
                                            if (keyType.Equals(typeof(string)))
                                            {
                                                if (cv == null || string.Empty.Equals(cv))
                                                {
                                                    (kssp.setter ?? ks.setter).Invoke(Guid.NewGuid());
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        if (Globals.UseShadowPropertiesForNew && props.ContainsKey(k) && props.ContainsKey(SHADOW_PROP_PREFIX + k))
                        {
                            var spval = props[SHADOW_PROP_PREFIX + k];
                            var defVal = WrappingHelper.GetDefaultForType(spval?.GetType());

                            if (defVal.IsSame(props[k]) && !defVal.IsSame(spval))
                            {
                                var sptype = spval.GetType();

                                if (sptype.Equals(typeof(int)))
                                {
                                    while (Interlocked.Increment(ref _lowIntKey) < (int)spval) ;
                                }
                                else
                                {
                                    if (sptype.Equals(typeof(long)))
                                    {
                                        while (Interlocked.Increment(ref _lowLongKey) < (long)spval) ;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            var keystate = (KeyServiceState)state;

            keystate.AddPK(ss, to, to.GetNotifyFriendly(), ResolveKeyDefinitionForType(to.BaseType), (changedPK, oldval, newval) =>
            {
                // Tracking dictionary may no longer correctly reflect composite key - force an update
                keystate.UpdatePK((KeyServiceState.CompositeWrapper)oldval, (KeyServiceState.CompositeWrapper)newval);
            });

            LinkByValuesInScope(to, ss, keystate, isNew);
        }

        public IEnumerable<object> GetParentObjects(ServiceScope ss, object o, RelationTypes types = RelationTypes.None)
        {
            return InternalGetParentObjects(ss.GetServiceState<KeyServiceState>(), ss, o, types, new HashSet<object>());
        }

        public IEnumerable<object> GetChildObjects(ServiceScope ss, object o, RelationTypes types = RelationTypes.None)
        {
            return InternalGetChildObjects(ss.GetServiceState<KeyServiceState>(), ss, o, types, new HashSet<object>());
        }

        public void UpdateBoundKeys(ServiceScope.TrackedObject to, ServiceScope ss, string fieldName, object oval, object nval)
        {
            foreach (var rel in _relations.GetAllByName(nameof(TypeChildRelationship.FullChildParentPropertyName), $"{to.GetTarget().GetBaseType().Name}.{fieldName}"))
            {
                if (nval != null)
                {
                    var toParent = ss.Objects.GetFirstByName(nameof(ServiceScope.TrackedObject.Target), nval);

                    if (toParent != null)
                    {
                        foreach (var k in GetKeyValues(toParent.GetInfraWrapperTarget()))
                        {
                            var (setter, type) = ss.GetSetter(to.GetInfraWrapperTarget(), rel.ChildResolvedKey[k.ordinal]);
                            setter.Invoke(k.value);
                        }
                    }
                }
            }
        }

        private void LinkByValuesInScope(ServiceScope.TrackedObject to, ServiceScope ss, KeyServiceState objstate, bool isNew)
        {
            var iw = to.GetCreateInfra();

            if (iw != null)
            {
                var uw = to.GetTarget();

                if (uw == null)
                    return;

                var w = to.GetWrapper();

                bool didLink = false;

                var childRels = _relations.GetAllByName(nameof(TypeChildRelationship.ChildType), uw.GetBaseType()).ToArray();

                Dictionary<TypeChildRelationship, List<(int ordinal, string name, object value)>> kvCache = new Dictionary<TypeChildRelationship, List<(int ordinal, string name, object value)>>();
                List<(int ordinal, string name, object value)> curPK = null;

                foreach (var rel in (from a in childRels where !string.IsNullOrEmpty(a.ParentPropertyName) select a))
                {
                    var val = iw.GetValue(rel.ParentPropertyName);

                    if (val != null)
                    {
                        var testParent = ss.GetTrackedByWrapperOrTarget(val);

                        if (testParent != null)
                        {
                            objstate.AddFK(ss, rel, testParent, to, testParent.GetNotifyFriendly(), true, false);
                        }
                        else
                        {
                            objstate.TrackMissingParentByRef(rel, val, to);
                        }
                    }
                    else
                    {
                        var chRoleVals = GetKeyValues(uw, rel.ChildResolvedKey);
                        kvCache[rel] = chRoleVals;

                        var testParent = objstate.GetTrackedByPKValue(ss, rel.ParentType, (from a in chRoleVals select a.value));

                        if (testParent != null)
                        {
                            iw.SetValue(rel.ParentPropertyName, testParent.GetWrapperTarget());
                            objstate.AddFK(ss, rel, testParent, to, testParent.GetNotifyFriendly(), true, false);
                        }
                        else
                        {
                            if (Globals.ResolveForArbitraryLoadOrder)
                            {
                                // We only track this case when asked to: it's more expensive and usually unnecessary, but during ZDB load phase, things can get loaded out-of-order due to async nature
                                objstate.TrackMissingParentByValue(rel, to, rel.ParentType, (from a in chRoleVals select a.value));
                            }
                        }
                    }
                }

                foreach (var rel in (from a in childRels where !string.IsNullOrEmpty(a.ChildPropertyName) select a))
                {
                    // Current entity role name used to look up any existing parent and add to their child collection if not already there
                    var chRoleVals = kvCache.TestAssignReturn(rel, () => { return GetKeyValues(uw, rel.ChildResolvedKey); });

                    if ((from a in chRoleVals where a.value != null select a).Any())
                    {
                        var testParent = objstate.GetTrackedByPKValue(ss, rel.ParentType, (from a in chRoleVals select a.value));

                        if (testParent != null)
                        {
                            var (getter, type) = ss.GetGetter(testParent.GetInfraWrapperTarget(), rel.ChildPropertyName);

                            if (WrappingHelper.IsWrappableListType(type, null))
                            {
                                var parVal = getter.Invoke();

                                if (parVal == null)
                                {
                                    parVal = WrappingHelper.CreateWrappingList(ss, type, testParent.AsUnwrapped(), rel.ChildPropertyName);

                                    var parChildSet = ss.GetSetter(testParent.GetInfraWrapperTarget(), rel.ChildPropertyName);
                                    parChildSet.setter.Invoke(parVal);
                                }

                                if (parVal is ICEFList asCefList)
                                {
                                    asCefList.AddWrappedItem(w ?? uw);
                                }

                                didLink = true;
                            }
                        }
                        else
                        {
                            if (Globals.ResolveForArbitraryLoadOrder)
                            {
                                // We only track this case when asked to: it's more expensive and usually unnecessary, but during ZDB load phase, things can get loaded out-of-order due to async nature
                                objstate.TrackMissingParentChildLinkback(rel, to, rel.ParentType, (from a in chRoleVals select a.value));
                            }
                        }
                    }
                }

                var parentRels = _relations.GetAllByName(nameof(TypeChildRelationship.ParentType), uw.GetBaseType());

                foreach (var rel in (from a in parentRels where !string.IsNullOrEmpty(a.ParentPropertyName) select a))
                {
                    foreach (var testChild in objstate.GetMissingChildrenForParentByRef(rel, w ?? uw))
                    {
                        var (getter, type) = ss.GetGetter(testChild.GetInfraWrapperTarget(), rel.ParentPropertyName);

                        if (getter != null)
                        {
                            var val = getter.Invoke();

                            if (val != null)
                            {
                                if (val.Equals(uw) || val.Equals(w))
                                {
                                    objstate.AddFK(ss, rel, to, testChild, to.GetNotifyFriendly(), true, true);
                                    didLink = true;
                                }
                            }
                        }
                    }

                    // Special case where a child record refers to a parent record by ID - normally would load parent *first*, but if misordered, we go back to the current scope and look for it now
                    if (!didLink && Globals.ResolveForArbitraryLoadOrder)
                    {
                        var parKeyVal = kvCache.TestAssignReturn(rel, () => { return GetKeyValues(uw, rel.ParentKey); });
                        curPK = parKeyVal;

                        // Maintaining an unlinked list for "child with no parent by value", look for cases that meet this condition now (parent appears, need to update child to point at "this" parent)
                        foreach (var testChild in objstate.GetMissingChildrenByValue(rel, (from a in parKeyVal select a.value)))
                        {
                            var (setter, type) = ss.GetSetter(testChild.Child.GetInfraWrapperTarget(), rel.ParentPropertyName);

                            if (setter != null)
                            {
                                setter.Invoke(to.GetWrapperTarget());
                                objstate.AddFK(ss, rel, to, testChild.Child, to.GetNotifyFriendly(), true, true);
                                testChild.Processed = true;
                                didLink = true;
                            }
                        }
                    }
                }

                foreach (var rel in (from a in parentRels where !string.IsNullOrEmpty(a.ChildPropertyName) select a))
                {
                    var (getter, type) = ss.GetGetter(iw, rel.ChildPropertyName);

                    if (getter != null)
                    {
                        if (WrappingHelper.IsWrappableListType(type, null))
                        {
                            var parVal = getter.Invoke();

                            if (parVal != null)
                            {
                                var sValEnum = ((System.Collections.IEnumerable)parVal).GetEnumerator();

                                while (sValEnum.MoveNext())
                                {
                                    var chVal = sValEnum.Current;
                                    var chTo = ss.GetTrackedByWrapperOrTarget(chVal);

                                    if (chTo != null)
                                    {
                                        objstate.AddFK(ss, rel, to, chTo, to.GetNotifyFriendly(), true, false);
                                    }
                                }
                            }
                        }
                    }
                }

                if (Globals.ResolveForArbitraryLoadOrder)
                {
                    // In this mode, we try to identify if the current object was previously passed over for linking as a child ref
                    // Normally this should not occur if we populate in a "good order" - but when loading from data store in parallel, could encounter out-of-order
                    if (curPK == null)
                    {
                        curPK = GetKeyValues(uw);
                    }

                    if (curPK?.Count() > 0)
                    {
                        foreach (var pobj in objstate.GetMissingChildLinkbacksForParentByValue(to.BaseType, (from k in curPK select k.value)))
                        {
                            var (getter, type) = ss.GetGetter(to.GetInfraWrapperTarget(), pobj.Relationship.ChildPropertyName);

                            if (WrappingHelper.IsWrappableListType(type, null))
                            {
                                var parVal = getter.Invoke();

                                if (parVal == null)
                                {
                                    parVal = WrappingHelper.CreateWrappingList(ss, type, to.GetWrapperTarget(), pobj.Relationship.ChildPropertyName);

                                    var parChildSet = ss.GetSetter(to.GetInfraWrapperTarget(), pobj.Relationship.ChildPropertyName);
                                    parChildSet.setter.Invoke(parVal);
                                }

                                if (parVal is ICEFList asCefList)
                                {
                                    if (asCefList.AddWrappedItem(pobj.Child.GetWrapperTarget()))
                                    {
                                        objstate.AddFK(ss, pobj.Relationship, to, pobj.Child, to.GetNotifyFriendly(), false, false);
                                        pobj.Processed = true;
                                    }
                                }

                                didLink = true;
                            }
                        }
                    }
                }

                if (didLink)
                {
                    // Short-circuit - we did what's below previously
                    return;
                }

                foreach (var rel in (from a in parentRels where !string.IsNullOrEmpty(a.ChildPropertyName) && a.ChildType.Equals(uw.GetBaseType()) select a))
                {
                    var chRoleVals = kvCache.TestAssignReturn(rel, () => { return GetKeyValues(uw, rel.ChildResolvedKey); });

                    if ((from a in chRoleVals where a.value != null select a).Any())
                    {
                        var testParent = objstate.GetTrackedByPKValue(ss, rel.ParentType, (from a in chRoleVals select a.value));

                        if (testParent != null)
                        {
                            var parGet = ss.GetGetter(testParent.GetInfraWrapperTarget(), rel.ChildPropertyName);
                            var parVal = parGet.getter.Invoke();
                            var asCefList = parVal as ICEFList;

                            if (parVal == null)
                            {
                                // If this is a wrappable type, create instance now to host the child value
                                if (WrappingHelper.IsWrappableListType(parGet.type, parVal))
                                {
                                    parVal = WrappingHelper.CreateWrappingList(ss, parGet.type, testParent.AsUnwrapped(), rel.ChildPropertyName);
                                    var parSet = ss.GetSetter(testParent.GetInfraWrapperTarget(), rel.ChildPropertyName);
                                    parSet.setter.Invoke(parVal);
                                    asCefList = parVal as ICEFList;
                                }
                            }
                            else
                            {
                                // Parent value might not be a CEF list type, see if we can convert it to be one now
                                if (asCefList == null && parVal is System.Collections.IEnumerable && WrappingHelper.IsWrappableListType(parGet.type, parVal))
                                {
                                    asCefList = WrappingHelper.CreateWrappingList(ss, parGet.type, testParent.AsUnwrapped(), rel.ChildPropertyName);

                                    var sValEnum = ((System.Collections.IEnumerable)parVal).GetEnumerator();

                                    while (sValEnum.MoveNext())
                                    {
                                        var toAdd = sValEnum.Current;
                                        var toAddWrapped = ss.GetDynamicWrapperFor(toAdd, false);
                                        var toAddTracked = ss.InternalCreateAddBase(toAdd, toAddWrapped != null && toAddWrapped.GetRowState() == ObjectState.Added, null, null, null, null);

                                        if (!asCefList.ContainsItem(toAddTracked))
                                        {
                                            asCefList.AddWrappedItem(toAddTracked);
                                        }
                                    }

                                    var parSet = ss.GetSetter(testParent.GetInfraWrapperTarget(), rel.ChildPropertyName);
                                    parSet.setter.Invoke(asCefList);
                                }
                            }

                            if (asCefList != null)
                            {
                                if (!asCefList.ContainsItem(w ?? uw))
                                {
                                    asCefList.AddWrappedItem(w ?? uw);
                                    objstate.AddFK(ss, rel, testParent, to, testParent.GetNotifyFriendly(), true, false);
                                }
                            }
                        }
                    }
                }
            }
        }

        public List<(int ordinal, string name, object value)> GetKeyValues(object o, IEnumerable<string> cols = null)
        {
            if (o == null)
                throw new ArgumentNullException("o");

            if (cols == null)
            {
                cols = ResolveKeyDefinitionForType(o.GetBaseType());

                if (cols == null)
                {
                    throw new CEFInvalidOperationException($"Failed to identify primary key for {o.GetType().Name}.");
                }
            }

            List<(int ordinal, string name, object value)> values = new List<(int ordinal, string name, object value)>();
            int ordinal = 0;

            var ss = CEF.CurrentServiceScope;

            foreach (var kf in cols)
            {
                var (getter, type) = ss.GetGetter(o, kf);

                if (getter != null)
                {
                    values.Add((ordinal, kf, getter.Invoke()));
                }

                ++ordinal;
            }

            return values;
        }

        public void UnlinkChildFromParentContainer(ServiceScope ss, string parentTypeName, string parentFieldName, object parContainer, object child)
        {
            string key = $"{parentTypeName}.{parentFieldName}";
            var ki = KeyService.GetMappedPropertyByFieldName(key);

            if (ki != null)
            {
                var top = ss.GetTrackedByWrapperOrTarget(parContainer);

                if (top != null)
                {
                    var toc = ss.GetTrackedByWrapperOrTarget(child);

                    if (toc != null)
                    {
                        var kss = ss.GetServiceState<KeyServiceState>();
                        kss.RemoveFK(ss, ki, top, toc, top.GetNotifyFriendly(), true);
                    }
                }
            }
        }

        public void LinkChildInParentContainer(ServiceScope ss, string parentTypeName, string parentFieldName, object parContainer, object child)
        {
            string key = $"{parentTypeName}.{parentFieldName}";
            var ki = KeyService.GetMappedPropertyByFieldName(key);

            if (ki != null)
            {
                var top = ss.GetTrackedByWrapperOrTarget(parContainer);

                if (top != null)
                {
                    var toc = ss.GetTrackedByWrapperOrTarget(child);

                    if (toc != null)
                    {
                        var kss = ss.GetServiceState<KeyServiceState>();
                        kss.AddFK(ss, ki, top, toc, top.GetNotifyFriendly(), true, false);
                    }
                }
            }
        }

        public void WireDependents(object o, object replaced, ServiceScope ss, ICEFList list, bool? objectModelOnly)
        {
            var state = ss.GetServiceState<KeyServiceState>();

            if (state == null)
            {
                throw new CEFInvalidOperationException("Could not find key service state.");
            }

            var uw = o.AsUnwrapped();

            if (uw == null)
            {
                // No ability to track this, so no need to wire up anything
                return;
            }

            var tcrs = _relations.GetAllByName(nameof(TypeChildRelationship.ChildType), uw.GetBaseType());

            if (tcrs.Any())
            {
                var child = ss.GetInfraOrWrapperOrTarget(o);

                if (child != null)
                {
                    // If the unwrapped object exists in an EntitySet, replace it with a wrapped version
                    foreach (var tcr in (from a in tcrs where a.ChildPropertyName != null select a))
                    {
                        List<object> childVals = new List<object>();

                        foreach (var crn in tcr.ChildResolvedKey)
                        {
                            var chGet = ss.GetGetter(child, crn);
                            childVals.Add(chGet.getter.Invoke());
                        }

                        var testParent = state.GetTrackedByPKValue(ss, tcr.ParentType, childVals);

                        if (testParent != null)
                        {
                            var wt = testParent.GetWrapperTarget();

                            var cpi = wt.GetType().GetProperty(tcr.ChildPropertyName);

                            if (cpi != null && cpi.CanRead)
                            {
                                var es = wt.FastGetValue(cpi.Name) as ICEFList;

                                if (es != null)
                                {
                                    var toRemove = (es.ContainsItem(o) && !es.ContainsItem(replaced)) ? o : null;

                                    try
                                    {
                                        list?.SuspendNotifications(true);
                                        es.AddWrappedItem(replaced ?? o);

                                        // Special case - trigger removal of unwrapped and add wrapped
                                        if (toRemove != null)
                                        {
                                            es.RemoveItem(toRemove);
                                        }
                                    }
                                    finally
                                    {
                                        list?.SuspendNotifications(false);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        public IDictionary<Type, IList<string>> GetChildTypes(object o, bool all = true)
        {
            Dictionary<Type, IList<string>> visits = new Dictionary<Type, IList<string>>();
            InternalGetChildTypes(o.GetBaseType(), visits, all);
            return visits;
        }

        public IDictionary<Type, IList<string>> GetParentTypes(object o, bool all = true)
        {
            Dictionary<Type, IList<string>> visits = new Dictionary<Type, IList<string>>();
            InternalGetParentTypes(o.GetBaseType(), visits, all);
            return visits;
        }

        public int GetObjectNestLevel(object o)
        {
            return GetParentObjects(CEF.CurrentServiceScope, o, RelationTypes.Parents).Count();
        }

        public virtual void Disposing(ServiceScope ss)
        {
        }

        #region "Key Object State"

        public sealed class KeyServiceState : ICEFServiceObjState
        {
            /// <summary>
            /// This value type is used internally to represent the key value of an object - done in a way to avoid using ref types such as strings (previously used a single stringized version) which can be costly for memory use.
            /// </summary>
            internal struct CompositeItemWrapper
            {
                private long? AsWhole;
                private Guid? AsGuid;
                private string AsString;
                private bool IsNull;

                public override bool Equals(object obj)
                {
                    if (!(obj is CompositeItemWrapper))
                    {
                        return false;
                    }

                    var other = (CompositeItemWrapper)obj;

                    if (AsWhole.HasValue && other.AsWhole.HasValue)
                        return AsWhole.Value == other.AsWhole.Value;

                    if (AsGuid.HasValue && other.AsGuid.HasValue)
                        return AsGuid.Value == other.AsGuid.Value;

                    if (IsNull)
                        return other.IsNull;

                    return AsString.IsSame(other.AsString);
                }

                public override int GetHashCode()
                {
                    if (AsWhole.HasValue)
                        return AsWhole.Value.GetHashCode();

                    if (AsGuid.HasValue)
                        return AsGuid.Value.GetHashCode();

                    if (IsNull)
                        return -42;

                    return AsString.GetHashCode();
                }

                public CompositeItemWrapper(object val)
                {
                    AsGuid = null;
                    AsWhole = null;
                    AsString = null;
                    IsNull = false;

                    if (val == null)
                    {
                        IsNull = true;
                    }
                    else
                    {
                        if (val is Guid ag)
                        {
                            AsGuid = ag;
                        }
                        else
                        {
                            if (val is int ai)
                            {
                                AsWhole = ai;
                            }
                            else
                            {
                                if (val is long al)
                                {
                                    AsWhole = al;
                                }
                                else
                                {
                                    if (val is string astr)
                                    {
                                        AsString = astr;
                                    }
                                    else
                                    {
                                        if (val is Int16 ash)
                                        {
                                            AsWhole = ash;
                                        }
                                        else
                                        {
                                            if (val is byte ab)
                                            {
                                                AsWhole = ab;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // Assertion: must have at least 1 type match or a problem!
                    if (!IsNull && !AsWhole.HasValue && !AsGuid.HasValue && AsString == null)
                    {
                        throw new ArgumentException("Type of value (val) is not supported currently, add to CEF.");
                    }
                }
            }

            internal struct CompositeWrapper
            {
                private ImmutableArray<CompositeItemWrapper> Items;
                private Type BaseType;

                public CompositeWrapper(Type basetype)
                {
                    BaseType = basetype ?? throw new ArgumentNullException("basetype");
                    Items = ImmutableArray.Create<CompositeItemWrapper>();
                }

                public void Add(CompositeItemWrapper item)
                {
                    Items = Items.Add(item);
                }

                public override bool Equals(object obj)
                {
                    if (!(obj is CompositeWrapper))
                    {
                        return false;
                    }

                    var other = (CompositeWrapper)obj;

                    if (!(BaseType?.Equals(other.BaseType)).GetValueOrDefault())
                    {
                        return false;
                    }

                    var otherItems = other.Items;

                    if (otherItems.Length != Items.Length)
                    {
                        return false;
                    }

                    for (int i = 0; i < Items.Length; ++i)
                    {
                        if (!otherItems[i].Equals(Items[i]))
                        {
                            return false;
                        }
                    }

                    return true;
                }

                public override int GetHashCode()
                {
                    int hc = BaseType.GetHashCode();

                    for (int i = 0; i < Items.Length; ++i)
                    {
                        hc = hc ^ Items[i].GetHashCode();
                    }

                    return hc;
                }
            }

            public class KeyObjectStatePK : IDisposable, ICEFIndexedListItem
            {
                private ServiceScope LinkedScope { get; set; }

                internal CompositeWrapper Composite { get; set; }
                public ServiceScope.TrackedObject Parent { get; set; }
                public IList<string> ParentKeyFields { get; set; }
                public CEFWeakReference<INotifyPropertyChanged> NotifyParent { get; set; }
                private Action<ServiceScope.TrackedObject, object, object> KeyChangeAlert { get; set; }

                public KeyObjectStatePK(ServiceScope ss, ServiceScope.TrackedObject parent, INotifyPropertyChanged notifyParent, IList<string> parentFields, Action<ServiceScope.TrackedObject, object, object> notifyKeyChange)
                {
                    LinkedScope = ss;
                    ParentKeyFields = parentFields;
                    Parent = parent;
                    NotifyParent = new CEFWeakReference<INotifyPropertyChanged>(notifyParent);
                    KeyChangeAlert = notifyKeyChange;

                    // Wire notifications on parent: if key assigned (or changed), need to update dictionary to reflect this and support efficient key lookups
                    notifyParent.PropertyChanged += Parent_PropertyChanged;

                    Composite = BuildComposite(null);
                }

                private void Parent_PropertyChanged(object sender, PropertyChangedEventArgs e)
                {
                    var ordinal = ParentKeyFields.IndexOf(e.PropertyName);

                    // Was it a key change?
                    if (ordinal >= 0 && NotifyParent.IsAlive && NotifyParent.Target != null)
                    {
                        var oldVal = Composite;

                        // If using shadow props and no longer set to default, can remove shadow prop - for now, assume that any assignment will be to a non-null/valid value
                        if (Globals.UseShadowPropertiesForNew)
                        {
                            var iw = Parent.GetInfra();

                            if (iw != null)
                            {
                                if (iw.HasShadowProperty(e.PropertyName))
                                {
                                    iw.RemoveProperty(SHADOW_PROP_PREFIX + e.PropertyName);
                                }
                            }
                        }

                        Composite = BuildComposite(sender as INotifyPropertyChanged);

                        if (!oldVal.Equals(Composite))
                        {
                            // Since composite key has likely changed, need to update this in the dictionary - link no longer correct
                            KeyChangeAlert?.Invoke(Parent, oldVal, Composite);
                        }
                    }
                }

                private CompositeWrapper BuildComposite(INotifyPropertyChanged sender)
                {
                    var uw = Parent.GetTarget() ?? throw new CEFInvalidOperationException("Cannot determine object identity.");
                    var bt = uw.GetBaseType() ?? throw new CEFInvalidOperationException("Cannot determine object identity.");

                    CompositeWrapper cw = new CompositeWrapper(bt);

                    foreach (var f in ParentKeyFields)
                    {
                        Func<object> pkGet = null;

                        if (sender != null)
                        {
                            var (readable, value) = sender.FastPropertyReadableWithValue(f);

                            if (readable)
                            {
                                pkGet = () =>
                                {
                                    return value;
                                };
                            }
                        }

                        if (pkGet == null)
                        {
                            var o = Parent.GetInfraWrapperTarget();

                            if (Globals.UseShadowPropertiesForNew && o is ICEFInfraWrapper && ((ICEFInfraWrapper)o).HasShadowProperty(f))
                            {
                                pkGet = LinkedScope.GetGetter(o, SHADOW_PROP_PREFIX + f).getter;
                            }

                            if (pkGet == null)
                            {
                                pkGet = LinkedScope.GetGetter(o, f).getter;
                            }
                        }

                        if (pkGet != null)
                        {
                            cw.Add(new CompositeItemWrapper(pkGet.Invoke()));
                        }
                    }

                    return cw;
                }
                public override bool Equals(object obj)
                {

                    if (obj is KeyObjectStatePK otherPK)
                    {
                        return otherPK.Parent.Equals(this.Parent);
                    }

                    return false;
                }

                public override int GetHashCode()
                {
                    return Parent.GetHashCode();
                }

                public object GetValue(string propName, bool unwrap)
                {
                    switch (propName)
                    {
                        case nameof(KeyObjectStatePK.Composite):
                            return Composite;

                        case nameof(KeyObjectStateFK.Parent):
                            return Parent.GetTarget();

                        case nameof(KeyObjectStatePK.NotifyParent):
                            if (unwrap)
                                return NotifyParent.IsAlive ? NotifyParent.Target : null;
                            else
                                return NotifyParent;
                    }
                    throw new NotSupportedException("Unsupported property name.");
                }

                #region IDisposable Support
                private bool _disposedValue = false; // To detect redundant calls

                protected void Dispose(bool disposing)
                {
                    if (!_disposedValue)
                    {
                        if (disposing && this.GetValue(nameof(NotifyParent), true) != null)
                        {
                            ((INotifyPropertyChanged)NotifyParent.Target).PropertyChanged -= Parent_PropertyChanged;
                        }

                        Parent = null;
                        NotifyParent = null;
                        KeyChangeAlert = null;
                        _disposedValue = true;
                    }
                }

                public void Dispose()
                {
                    Dispose(true);
                }
                #endregion
            }

            public class KeyObjectStateFK : IDisposable, ICEFIndexedListItem
            {
                private ServiceScope LinkedScope { get; set; }
                public TypeChildRelationship Key { get; set; }
                public ServiceScope.TrackedObject Parent { get; set; }
                public ServiceScope.TrackedObject Child { get; set; }
                public CEFWeakReference<INotifyPropertyChanged> NotifyParent { get; set; }

                public override bool Equals(object obj)
                {
                    var otherFK = obj as KeyObjectStateFK;

                    if (otherFK != null)
                    {
                        return otherFK.Parent.IsSame(this.Parent) && otherFK.Child.IsSame(this.Child);
                    }

                    return false;
                }

                public override int GetHashCode()
                {
                    return (Parent?.GetHashCode()).GetValueOrDefault() ^ (Child?.GetHashCode()).GetValueOrDefault();
                }

                public void NullifyChild(bool silent = true)
                {
                    foreach (var ck in Key.ChildResolvedKey)
                    {
                        var forChild = LinkedScope.GetSetter(Child.GetInfraWrapperTarget(), ck);

                        if (forChild.setter == null)
                        {
                            throw new CEFInvalidOperationException($"Could not find property {ck} on object of type {Child.GetTarget()?.GetType()?.Name}.");
                        }

                        try
                        {
                            forChild.setter.Invoke(null);
                        }
                        catch (Exception ex)
                        {
                            if (!silent)
                            {
                                throw new CEFInvalidOperationException($"Failed to remove reference to {Child.GetTarget()?.GetType()?.Name} ({ck}).", ex);
                            }
                        }
                    }
                }

                public KeyObjectStateFK(ServiceScope ss, TypeChildRelationship key, ServiceScope.TrackedObject parent, ServiceScope.TrackedObject child, INotifyPropertyChanged notifyParent)
                {
                    LinkedScope = ss;
                    Key = key;
                    Parent = parent;
                    Child = child;
                    NotifyParent = new CEFWeakReference<INotifyPropertyChanged>(notifyParent);

                    // Wire notifications on parent: if key assigned (or changed), needs to cascade!
                    notifyParent.PropertyChanged += Parent_PropertyChanged;
                }

                private void Parent_PropertyChanged(object sender, PropertyChangedEventArgs e)
                {
                    var ordinal = Key.ParentKey.IndexOf(e.PropertyName);

                    // Was it a key change?
                    if (ordinal >= 0 && NotifyParent.IsAlive && NotifyParent.Target != null)
                    {
                        Func<object> forParent = null;

                        if (sender != null)
                        {
                            var r = sender.FastPropertyReadableWithValue(e.PropertyName);

                            if (r.readable)
                            {
                                forParent = () =>
                                {
                                    return r.value;
                                };
                            }
                        }

                        if (forParent == null)
                        {
                            forParent = LinkedScope.GetGetter(NotifyParent.Target, e.PropertyName).getter;
                        }

                        if (forParent == null)
                        {
                            throw new CEFInvalidOperationException($"Could not find property {e.PropertyName} on object of type {NotifyParent.Target?.GetType()?.Name}.");
                        }

                        var chCol = Key.ChildResolvedKey[ordinal];
                        var forChild = LinkedScope.GetSetter(Child.GetInfraWrapperTarget(), chCol);

                        if (forChild.setter == null)
                        {
                            throw new CEFInvalidOperationException($"Could not find property {chCol} on object of type {Child.GetTarget()?.GetType()?.Name}.");
                        }

                        forChild.setter.Invoke(forParent.Invoke());
                    }
                }

                internal IList<object> GetParentKeyValue()
                {
                    List<object> val = new List<object>();

                    for (int i = 0; i < Key.ParentKey.Count; ++i)
                    {
                        var forParent = LinkedScope.GetGetter(NotifyParent.Target, Key.ParentKey[i]);
                        var parVal = forParent.getter.Invoke();
                        val.Add(parVal);
                    }

                    return val;
                }

                internal IList<object> GetChildRoleValue()
                {
                    List<object> val = new List<object>();

                    for (int i = 0; i < Key.ChildResolvedKey.Count; ++i)
                    {
                        var (getter, type) = LinkedScope.GetGetter(Child.Target, Key.ChildResolvedKey[i]);
                        var chVal = getter.Invoke();
                        val.Add(chVal);
                    }

                    return val;
                }

                internal void CopyParentValueToChild()
                {
                    for (int i = 0; i < Key.ParentKey.Count && i < Key.ChildResolvedKey.Count; ++i)
                    {
                        var (getter, type) = LinkedScope.GetGetter(NotifyParent.Target, Key.ParentKey[i]);
                        var parVal = getter.Invoke();
                        var forChild = LinkedScope.GetSetter(Child.GetInfraWrapperTarget(), Key.ChildResolvedKey[i]);
                        forChild.setter.Invoke(parVal);
                    }
                }

                public object GetValue(string propName, bool unwrap)
                {
                    switch (propName)
                    {
                        case nameof(KeyObjectStateFK.Key):
                            return Key;

                        case nameof(KeyObjectStateFK.Parent):
                            return Parent.GetTarget();

                        case nameof(KeyObjectStateFK.Child):
                            return Child.GetTarget();

                        case nameof(KeyObjectStateFK.NotifyParent):
                            if (unwrap)
                                return NotifyParent.IsAlive ? NotifyParent.Target : null;
                            else
                                return NotifyParent;
                    }
                    throw new NotSupportedException("Unsupported property name.");
                }

                #region IDisposable Support
                private bool disposedValue = false; // To detect redundant calls

                protected void Dispose(bool disposing)
                {
                    if (!disposedValue)
                    {
                        if (disposing && this.GetValue(nameof(NotifyParent), true) != null)
                        {
                            ((INotifyPropertyChanged)NotifyParent.Target).PropertyChanged -= Parent_PropertyChanged;
                        }

                        Key = null;
                        Parent = null;
                        Child = null;
                        NotifyParent = null;
                        disposedValue = true;
                    }
                }

                public void Dispose()
                {
                    Dispose(true);
                }
                #endregion
            }

            internal class UnlinkedChildByValueInfo
            {
                public TypeChildRelationship Relationship;
                public ServiceScope.TrackedObject Child;
                public bool Processed;
            }

            private SlimConcurrentDictionary<TypeChildRelationship, SlimConcurrentDictionary<object, ConcurrentObservableCollection<ServiceScope.TrackedObject>>> _missingParentByRef = new SlimConcurrentDictionary<TypeChildRelationship, SlimConcurrentDictionary<object, ConcurrentObservableCollection<ServiceScope.TrackedObject>>>();
            private SlimConcurrentDictionary<TypeChildRelationship, SlimConcurrentDictionary<CompositeWrapper, ConcurrentObservableCollection<UnlinkedChildByValueInfo>>> _missingParentByVal = new SlimConcurrentDictionary<TypeChildRelationship, SlimConcurrentDictionary<CompositeWrapper, ConcurrentObservableCollection<UnlinkedChildByValueInfo>>>();
            private SlimConcurrentDictionary<Type, SlimConcurrentDictionary<CompositeWrapper, ConcurrentObservableCollection<UnlinkedChildByValueInfo>>> _missingParentChildLinkback = new SlimConcurrentDictionary<Type, SlimConcurrentDictionary<CompositeWrapper, ConcurrentObservableCollection<UnlinkedChildByValueInfo>>>();

            private readonly object _asNull = new object();
            private bool _firstInit = true;

            internal ConcurrentIndexedList<KeyObjectStateFK> AllFK { get; private set; } = new ConcurrentIndexedList<KeyObjectStateFK>(nameof(KeyObjectStateFK.Parent), nameof(KeyObjectStateFK.Child));

            internal ConcurrentIndexedList<KeyObjectStatePK> AllPK { get; private set; } = new ConcurrentIndexedList<KeyObjectStatePK>(nameof(KeyObjectStatePK.Composite)).AddUniqueConstraint(nameof(KeyObjectStatePK.Composite));

            public void Cleanup(ServiceScope ss)
            {
                ProcessUnprocessedParentNullLinks(ss);
                ProcessUnprocessedChildLinkbacks(ss);

                _missingParentByRef.Clear();
                _missingParentByVal.Clear();
                _missingParentChildLinkback.Clear();
            }

            public KeyObjectStateFK RemoveFK(ServiceScope ss, TypeChildRelationship key, ServiceScope.TrackedObject parent, ServiceScope.TrackedObject child, INotifyPropertyChanged parentWrapped, bool nullifyChild)
            {
                var findKey = new KeyObjectStateFK(ss, key, parent, child, parentWrapped);
                var existingKey = AllFK.Find(findKey);

                if (existingKey != null)
                {
                    AllFK.Remove(existingKey);

                    if (nullifyChild)
                    {
                        existingKey.NullifyChild();
                    }

                    existingKey.Dispose();
                }

                return existingKey;
            }

            public KeyObjectStateFK AddFK(ServiceScope ss, TypeChildRelationship key, ServiceScope.TrackedObject parent, ServiceScope.TrackedObject child, INotifyPropertyChanged parentWrapped, bool copyValues, bool checkUnlinked)
            {
                if (parentWrapped != null)
                {
                    var nk = new KeyObjectStateFK(ss, key, parent, child, parentWrapped);

                    AllFK.Add(nk, false);

                    // Remove from unlinked child list, if preset
                    if (checkUnlinked)
                    {
                        if (_missingParentByRef.TryGetValue(key, out var map))
                        {
                            lock (_missingParentByRef)
                            {
                                if (map.TryGetValue(_asNull, out var ul1))
                                {
                                    ul1.Remove(child);
                                }

                                var pwt = parent.GetWrapperTarget();

                                if (map.TryGetValue(pwt, out var ul2))
                                {
                                    var keep = (from a in ul2 where a != child select a);

                                    if (keep.Any())
                                    {
                                        var nlist = new ConcurrentObservableCollection<ServiceScope.TrackedObject>(keep.Count());

                                        foreach (var k in keep)
                                        {
                                            nlist.Add(k);
                                        }

                                        map[pwt] = nlist;
                                    }
                                    else
                                    {
                                        map.Remove(pwt);
                                    }
                                }
                            }
                        }
                    }

                    if (copyValues)
                    {
                        nk.CopyParentValueToChild();
                    }

                    return nk;
                }

                return null;
            }

            internal IEnumerable<(CompositeWrapper pk, UnlinkedChildByValueInfo info)> GetUnprocessedMissingParentLinksForChildren()
            {
                foreach (var kvp1 in _missingParentByVal)
                {
                    foreach (var kvp2 in kvp1.Value)
                    {
                        foreach (var i in kvp2.Value)
                        {
                            if (!i.Processed)
                            {
                                yield return (kvp2.Key, i);
                            }
                        }
                    }
                }
            }

            internal IEnumerable<(CompositeWrapper pk, UnlinkedChildByValueInfo info)> GetUnprocessedMissingChildLinkbacksForParent()
            {
                foreach (var kvp1 in _missingParentChildLinkback)
                {
                    foreach (var kvp2 in kvp1.Value)
                    {
                        foreach (var i in kvp2.Value)
                        {
                            if (!i.Processed)
                            {
                                yield return (kvp2.Key, i);
                            }
                        }
                    }
                }
            }

            internal IEnumerable<UnlinkedChildByValueInfo> GetMissingChildLinkbacksForParentByValue(Type parentType, IEnumerable<object> parentKey)
            {
                CompositeWrapper cw = new CompositeWrapper(parentType);

                foreach (var k in parentKey)
                {
                    cw.Add(new CompositeItemWrapper(k));
                }

                lock (_missingParentChildLinkback)
                {
                    if (_missingParentChildLinkback.TryGetValue(parentType, out var objs))
                    {
                        if (objs.TryGetValue(cw, out var chinfo))
                        {
                            return chinfo;
                        }
                    }
                }

                return Array.Empty<UnlinkedChildByValueInfo>();
            }

            internal IEnumerable<UnlinkedChildByValueInfo> GetMissingChildrenByValue(TypeChildRelationship key, IEnumerable<object> keyVal)
            {
                CompositeWrapper cw = new CompositeWrapper(key.ParentType);

                foreach (var f in keyVal)
                {
                    cw.Add(new CompositeItemWrapper(f));
                }

                if (_missingParentByVal.TryGetValue(key, out var cvl))
                {
                    if (cvl.TryGetValue(cw, out var cl))
                    {
                        return cl;
                    }
                }

                return Array.Empty<UnlinkedChildByValueInfo>();
            }

            public IEnumerable<ServiceScope.TrackedObject> GetMissingChildrenForParentByRef(TypeChildRelationship key, object parentWoTValue)
            {
                if (_missingParentByRef.TryGetValue(key, out var uk))
                {
                    uk.TryGetValue(parentWoTValue, out var tl1);

                    if (tl1 != null)
                    {
                        return tl1;
                    }
                }

                return Array.Empty<ServiceScope.TrackedObject>();
            }

            public void TrackMissingParentChildLinkback(TypeChildRelationship key, ServiceScope.TrackedObject child, Type parentType, IEnumerable<object> parentKey)
            {
                ConcurrentObservableCollection<UnlinkedChildByValueInfo> tl = null;
                CompositeWrapper cw = new CompositeWrapper(parentType);

                foreach (var f in parentKey)
                {
                    cw.Add(new CompositeItemWrapper(f));
                }

                lock (_missingParentChildLinkback)
                {
                    _missingParentChildLinkback.TryGetValue(parentType, out var uk);

                    if (uk == null)
                    {
                        uk = new SlimConcurrentDictionary<CompositeWrapper, ConcurrentObservableCollection<UnlinkedChildByValueInfo>>();
                        _missingParentChildLinkback[parentType] = uk;
                    }

                    uk.TryGetValue(cw, out tl);

                    if (tl == null)
                    {
                        tl = new ConcurrentObservableCollection<UnlinkedChildByValueInfo>();
                        uk[cw] = tl;
                    }
                }

                tl.Add(new UnlinkedChildByValueInfo() { Relationship = key, Child = child });
            }

            public void TrackMissingParentByValue(TypeChildRelationship key, ServiceScope.TrackedObject child, Type parentType, IEnumerable<object> parentKey)
            {
                ConcurrentObservableCollection<UnlinkedChildByValueInfo> tl = null;
                CompositeWrapper cw = new CompositeWrapper(parentType);

                foreach (var f in parentKey)
                {
                    cw.Add(new CompositeItemWrapper(f));
                }

                lock (_missingParentByVal)
                {
                    _missingParentByVal.TryGetValue(key, out var uk);

                    if (uk == null)
                    {
                        uk = new SlimConcurrentDictionary<CompositeWrapper, ConcurrentObservableCollection<UnlinkedChildByValueInfo>>();
                        _missingParentByVal[key] = uk;
                    }

                    uk.TryGetValue(cw, out tl);

                    if (tl == null)
                    {
                        tl = new ConcurrentObservableCollection<UnlinkedChildByValueInfo>();
                        uk[cw] = tl;
                    }
                }

                tl.Add(new UnlinkedChildByValueInfo() { Child = child, Relationship = key });
            }

            public void TrackMissingParentByRef(TypeChildRelationship key, object parentWoTValue, ServiceScope.TrackedObject child)
            {
                ConcurrentObservableCollection<ServiceScope.TrackedObject> tl = null;

                lock (_missingParentByRef)
                {
                    _missingParentByRef.TryGetValue(key, out var uk);

                    if (uk == null)
                    {
                        uk = new SlimConcurrentDictionary<object, ConcurrentObservableCollection<ServiceScope.TrackedObject>>();
                        _missingParentByRef[key] = uk;
                    }

                    uk.TryGetValue(parentWoTValue ?? _asNull, out tl);

                    if (tl == null)
                    {
                        tl = new ConcurrentObservableCollection<ServiceScope.TrackedObject>();
                        uk[parentWoTValue ?? _asNull] = tl;
                    }
                }

                tl.Add(child);
            }

            public KeyObjectStatePK AddPK(ServiceScope ss, ServiceScope.TrackedObject parent, INotifyPropertyChanged parentWrapped, IList<string> parentKeyFields, Action<ServiceScope.TrackedObject, object, object> notifyKeyChange)
            {
                lock (_asNull)
                {
                    if (_firstInit)
                    {
                        if (AllPK.InitialCapacity != ss.Settings.EstimatedScopeSize)
                        {
                            AllPK = new ConcurrentIndexedList<KeyObjectStatePK>(ss.Settings.EstimatedScopeSize, nameof(KeyObjectStatePK.Composite)).AddUniqueConstraint(nameof(KeyObjectStatePK.Composite));

                            // FKs are trickier - how many will we have per entity? we'll guess 2:1 but let it be controllable
                            AllFK = new ConcurrentIndexedList<KeyObjectStateFK>(Convert.ToInt32(ss.Settings.EstimatedScopeSize * Globals.EstimatedFKRatio), nameof(KeyObjectStateFK.Parent), nameof(KeyObjectStateFK.Child));
                        }
                        _firstInit = false;
                    }
                }

                if (parentWrapped != null)
                {
                    var nk = new KeyObjectStatePK(ss, parent, parentWrapped, parentKeyFields, notifyKeyChange);
                    AllPK.Add(nk, false);
                    return nk;
                }

                return null;
            }

            internal ServiceScope.TrackedObject GetTrackedByCompositePK(CompositeWrapper composite)
            {
                return AllPK.GetFirstByName(nameof(KeyObjectStatePK.Composite), composite)?.Parent;
            }

            internal void ProcessUnprocessedParentNullLinks(ServiceScope ss)
            {
                foreach (var (pk, info) in GetUnprocessedMissingParentLinksForChildren())
                {
                    var to = GetTrackedByCompositePK(pk);

                    if (to != null)
                    {
                        var (setter, type) = ss.GetSetter(info.Child.GetInfraWrapperTarget(), info.Relationship.ParentPropertyName);

                        if (setter != null)
                        {
                            setter.Invoke(to.GetWrapperTarget());
                            AddFK(ss, info.Relationship, to, info.Child, to.GetNotifyFriendly(), true, false);
                            info.Processed = true;
                        }
                    }
                }
            }

            internal void ProcessUnprocessedChildLinkbacks(ServiceScope ss)
            {
                foreach (var (pk, info) in GetUnprocessedMissingChildLinkbacksForParent())
                {
                    var to = GetTrackedByCompositePK(pk);

                    if (to != null)
                    {
                        var (getter, type) = ss.GetGetter(to.GetInfraWrapperTarget(), info.Relationship.ChildPropertyName);

                        if (WrappingHelper.IsWrappableListType(type, null))
                        {
                            var parVal = getter.Invoke();

                            if (parVal == null)
                            {
                                parVal = WrappingHelper.CreateWrappingList(ss, type, to.GetWrapperTarget(), info.Relationship.ChildPropertyName);

                                var parChildSet = ss.GetSetter(to.GetInfraWrapperTarget(), info.Relationship.ChildPropertyName);
                                parChildSet.setter.Invoke(parVal);
                            }

                            if (parVal is ICEFList asCefList)
                            {
                                if (asCefList.AddWrappedItem(info.Child.GetWrapperTarget()))
                                {
                                    AddFK(ss, info.Relationship, to, info.Child, to.GetNotifyFriendly(), false, false);
                                    info.Processed = true;
                                }
                            }
                        }
                    }
                }
            }

            public ServiceScope.TrackedObject GetTrackedByPKValue(ServiceScope ss, Type parentType, IEnumerable<object> keyValues)
            {
                CompositeWrapper cw = new CompositeWrapper(parentType);

                foreach (var f in keyValues)
                {
                    cw.Add(new CompositeItemWrapper(f));
                }

                var to = GetTrackedByCompositePK(cw);

                if (to == null || !to.IsAlive)
                {
                    return null;
                }

                return to;
            }

            internal void UpdatePK(CompositeWrapper oldcomp, CompositeWrapper newcomp)
            {
                AllPK.UpdateFieldIndex(nameof(KeyObjectStatePK.Composite), oldcomp, newcomp);
            }

            public IEnumerable<KeyObjectStateFK> AllChildren(object parent)
            {
                return AllFK.GetAllByName(nameof(KeyObjectStateFK.Parent), parent);
            }

            public IEnumerable<KeyObjectStateFK> AllParents(object child)
            {
                return AllFK.GetAllByName(nameof(KeyObjectStateFK.Child), child);
            }
        }

        #endregion
    }
}
