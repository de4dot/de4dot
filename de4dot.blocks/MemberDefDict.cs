/*
    Copyright (C) 2011-2015 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using dnlib.DotNet;

namespace de4dot.blocks {
	public class TypeDefDict<TValue> {
		Dictionary<ScopeAndTokenKey, TValue> tokenToValue = new Dictionary<ScopeAndTokenKey, TValue>();
		Dictionary<ScopeAndTokenKey, TypeDef> tokenToKey = new Dictionary<ScopeAndTokenKey, TypeDef>();
		Dictionary<IType, TValue> refToValue = new Dictionary<IType, TValue>(TypeEqualityComparer.Instance);
		Dictionary<IType, TypeDef> refToKey = new Dictionary<IType, TypeDef>(TypeEqualityComparer.Instance);

		public int Count {
			get { return tokenToValue.Count; }
		}

		public IEnumerable<TypeDef> GetKeys() {
			return tokenToKey.Values;
		}

		public IEnumerable<TValue> GetValues() {
			return tokenToValue.Values;
		}

		ScopeAndTokenKey GetTokenKey(TypeDef typeDef) {
			return new ScopeAndTokenKey(typeDef);
		}

		public TValue Find(IType typeRef) {
			TValue value;
			var typeDef = typeRef as TypeDef;
			if (typeDef != null)
				tokenToValue.TryGetValue(GetTokenKey(typeDef), out value);
			else if (typeRef != null)
				refToValue.TryGetValue(typeRef, out value);
			else
				value = default(TValue);
			return value;
		}

		public TValue FindAny(IType type) {
			TValue value;
			var typeDef = type as TypeDef;
			if (typeDef != null && tokenToValue.TryGetValue(GetTokenKey(typeDef), out value))
				return value;

			refToValue.TryGetValue(type, out value);
			return value;
		}

		public void Add(TypeDef typeDef, TValue value) {
			var tokenKey = GetTokenKey(typeDef);
			tokenToValue[tokenKey] = value;
			tokenToKey[tokenKey] = typeDef;

			if (!refToValue.ContainsKey(typeDef) ||
				GetAccessibilityOrder(typeDef) < GetAccessibilityOrder(refToKey[typeDef])) {
				refToKey[typeDef] = typeDef;
				refToValue[typeDef] = value;
			}
		}

		// Order: public, family, assembly, private
		static int[] accessibilityOrder = new int[8] {
			40,		// NotPublic
			0,		// Public
			10,		// NestedPublic
			70,		// NestedPrivate
			20,		// NestedFamily
			50,		// NestedAssembly
			60,		// NestedFamANDAssem
			30,		// NestedFamORAssem
		};
		static int GetAccessibilityOrder(TypeDef typeDef) {
			return accessibilityOrder[(int)typeDef.Attributes & 7];
		}

		public void OnTypesRenamed() {
			var newTypeRefToValue = new Dictionary<IType, TValue>(refToValue.Count);
			foreach (var kvp in refToValue)
				newTypeRefToValue[kvp.Key] = kvp.Value;
			refToValue = newTypeRefToValue;
		}
	}

	public abstract class FieldDefDictBase<TValue> {
		Dictionary<ScopeAndTokenKey, TValue> tokenToValue = new Dictionary<ScopeAndTokenKey, TValue>();
		Dictionary<ScopeAndTokenKey, FieldDef> tokenToKey = new Dictionary<ScopeAndTokenKey, FieldDef>();
		Dictionary<IFieldRefKey, TValue> refToValue = new Dictionary<IFieldRefKey, TValue>();
		Dictionary<IFieldRefKey, FieldDef> refToKey = new Dictionary<IFieldRefKey, FieldDef>();

		public int Count {
			get { return tokenToValue.Count; }
		}

		public IEnumerable<FieldDef> GetKeys() {
			return tokenToKey.Values;
		}

		public IEnumerable<TValue> GetValues() {
			return tokenToValue.Values;
		}

		ScopeAndTokenKey GetTokenKey(FieldDef fieldDef) {
			return new ScopeAndTokenKey(fieldDef);
		}

		internal abstract IFieldRefKey GetRefKey(IField fieldRef);

		public TValue Find(IField fieldRef) {
			TValue value;
			var fieldDef = fieldRef as FieldDef;
			if (fieldDef != null)
				tokenToValue.TryGetValue(GetTokenKey(fieldDef), out value);
			else
				refToValue.TryGetValue(GetRefKey(fieldRef), out value);
			return value;
		}

		public TValue FindAny(IField fieldRef) {
			TValue value;
			var fieldDef = fieldRef as FieldDef;
			if (fieldDef != null && tokenToValue.TryGetValue(GetTokenKey(fieldDef), out value))
				return value;

			refToValue.TryGetValue(GetRefKey(fieldRef), out value);
			return value;
		}

		public void Add(FieldDef fieldDef, TValue value) {
			var tokenKey = GetTokenKey(fieldDef);
			tokenToValue[tokenKey] = value;
			tokenToKey[tokenKey] = fieldDef;

			var refKey = GetRefKey(fieldDef);
			if (!refToValue.ContainsKey(refKey) ||
				GetAccessibilityOrder(fieldDef) < GetAccessibilityOrder(refToKey[refKey])) {
				refToKey[refKey] = fieldDef;
				refToValue[refKey] = value;
			}
		}

		// Order: public, family, assembly, private
		static int[] accessibilityOrder = new int[8] {
			60,		// PrivateScope
			50,		// Private
			40,		// FamANDAssem
			30,		// Assembly
			10,		// Family
			20,		// FamORAssem
			0,		// Public
			70,		// <reserved>
		};
		static int GetAccessibilityOrder(FieldDef fieldDef) {
			return accessibilityOrder[(int)fieldDef.Attributes & 7];
		}

		public void OnTypesRenamed() {
			var newFieldRefToDef = new Dictionary<IFieldRefKey, TValue>(refToValue.Count);
			foreach (var kvp in refToValue)
				newFieldRefToDef[GetRefKey((FieldDef)kvp.Key.FieldRef)] = kvp.Value;
			refToValue = newFieldRefToDef;
		}
	}

	public class FieldDefDict<TValue> : FieldDefDictBase<TValue> {
		internal override IFieldRefKey GetRefKey(IField fieldRef) {
			return new FieldRefKey(fieldRef);
		}
	}

	public class FieldDefAndDeclaringTypeDict<TValue> : FieldDefDictBase<TValue> {
		internal override IFieldRefKey GetRefKey(IField fieldRef) {
			return new FieldRefAndDeclaringTypeKey(fieldRef);
		}
	}

	public abstract class MethodDefDictBase<TValue> {
		Dictionary<ScopeAndTokenKey, TValue> tokenToValue = new Dictionary<ScopeAndTokenKey, TValue>();
		Dictionary<ScopeAndTokenKey, MethodDef> tokenToKey = new Dictionary<ScopeAndTokenKey, MethodDef>();
		Dictionary<IMethodRefKey, TValue> refToValue = new Dictionary<IMethodRefKey, TValue>();
		Dictionary<IMethodRefKey, MethodDef> refToKey = new Dictionary<IMethodRefKey, MethodDef>();

		public int Count {
			get { return tokenToValue.Count; }
		}

		public IEnumerable<MethodDef> GetKeys() {
			return tokenToKey.Values;
		}

		public IEnumerable<TValue> GetValues() {
			return tokenToValue.Values;
		}

		ScopeAndTokenKey GetTokenKey(MethodDef methodDef) {
			return new ScopeAndTokenKey(methodDef);
		}

		internal abstract IMethodRefKey GetRefKey(IMethod methodRef);

		public TValue Find(IMethod methodRef) {
			TValue value;
			var methodDef = methodRef as MethodDef;
			if (methodDef != null)
				tokenToValue.TryGetValue(GetTokenKey(methodDef), out value);
			else
				refToValue.TryGetValue(GetRefKey(methodRef), out value);
			return value;
		}

		public TValue FindAny(IMethod methodRef) {
			TValue value;
			var methodDef = methodRef as MethodDef;
			if (methodDef != null && tokenToValue.TryGetValue(GetTokenKey(methodDef), out value))
				return value;

			refToValue.TryGetValue(GetRefKey(methodRef), out value);
			return value;
		}

		public void Add(MethodDef methodDef, TValue value) {
			var tokenKey = GetTokenKey(methodDef);
			tokenToValue[tokenKey] = value;
			tokenToKey[tokenKey] = methodDef;

			var refKey = GetRefKey(methodDef);
			if (!refToValue.ContainsKey(refKey) ||
				GetAccessibilityOrder(methodDef) < GetAccessibilityOrder(refToKey[refKey])) {
				refToKey[refKey] = methodDef;
				refToValue[refKey] = value;
			}
		}

		// Order: public, family, assembly, private
		static int[] accessibilityOrder = new int[8] {
			60,		// PrivateScope
			50,		// Private
			40,		// FamANDAssem
			30,		// Assembly
			10,		// Family
			20,		// FamORAssem
			0,		// Public
			70,		// <reserved>
		};
		static int GetAccessibilityOrder(MethodDef methodDef) {
			return accessibilityOrder[(int)methodDef.Attributes & 7];
		}

		public void OnTypesRenamed() {
			var newFieldRefToDef = new Dictionary<IMethodRefKey, TValue>(refToValue.Count);
			foreach (var kvp in refToValue)
				newFieldRefToDef[GetRefKey((MethodDef)kvp.Key.MethodRef)] = kvp.Value;
			refToValue = newFieldRefToDef;
		}
	}

	public class MethodDefDict<TValue> : MethodDefDictBase<TValue> {
		internal override IMethodRefKey GetRefKey(IMethod methodRef) {
			return new MethodRefKey(methodRef);
		}
	}

	public class MethodDefAndDeclaringTypeDict<TValue> : MethodDefDictBase<TValue> {
		internal override IMethodRefKey GetRefKey(IMethod methodRef) {
			return new MethodRefAndDeclaringTypeKey(methodRef);
		}
	}

	public abstract class EventDefDictBase<TValue> {
		Dictionary<ScopeAndTokenKey, TValue> tokenToValue = new Dictionary<ScopeAndTokenKey, TValue>();
		Dictionary<ScopeAndTokenKey, EventDef> tokenToKey = new Dictionary<ScopeAndTokenKey, EventDef>();
		Dictionary<IEventRefKey, TValue> refToValue = new Dictionary<IEventRefKey, TValue>();

		public int Count {
			get { return tokenToValue.Count; }
		}

		public IEnumerable<EventDef> GetKeys() {
			return tokenToKey.Values;
		}

		public IEnumerable<TValue> GetValues() {
			return tokenToValue.Values;
		}

		ScopeAndTokenKey GetTokenKey(EventDef eventRef) {
			return new ScopeAndTokenKey(eventRef);
		}

		internal abstract IEventRefKey GetRefKey(EventDef eventRef);

		public TValue Find(EventDef eventRef) {
			TValue value;
			tokenToValue.TryGetValue(GetTokenKey(eventRef), out value);
			return value;
		}

		public TValue FindAny(EventDef eventRef) {
			TValue value;
			if (tokenToValue.TryGetValue(GetTokenKey(eventRef), out value))
				return value;

			refToValue.TryGetValue(GetRefKey(eventRef), out value);
			return value;
		}

		public void Add(EventDef eventDef, TValue value) {
			var tokenKey = GetTokenKey(eventDef);
			tokenToValue[tokenKey] = value;
			tokenToKey[tokenKey] = eventDef;

			refToValue[GetRefKey(eventDef)] = value;
		}

		public void OnTypesRenamed() {
			var newFieldRefToDef = new Dictionary<IEventRefKey, TValue>(refToValue.Count);
			foreach (var kvp in refToValue)
				newFieldRefToDef[GetRefKey((EventDef)kvp.Key.EventDef)] = kvp.Value;
			refToValue = newFieldRefToDef;
		}
	}

	public class EventDefDict<TValue> : EventDefDictBase<TValue> {
		internal override IEventRefKey GetRefKey(EventDef eventRef) {
			return new EventRefKey(eventRef);
		}
	}

	public class EventDefAndDeclaringTypeDict<TValue> : EventDefDictBase<TValue> {
		internal override IEventRefKey GetRefKey(EventDef eventRef) {
			return new EventRefAndDeclaringTypeKey(eventRef);
		}
	}

	public abstract class PropertyDefDictBase<TValue> {
		Dictionary<ScopeAndTokenKey, TValue> tokenToValue = new Dictionary<ScopeAndTokenKey, TValue>();
		Dictionary<ScopeAndTokenKey, PropertyDef> tokenToKey = new Dictionary<ScopeAndTokenKey, PropertyDef>();
		Dictionary<IPropertyRefKey, TValue> refToValue = new Dictionary<IPropertyRefKey, TValue>();

		public int Count {
			get { return tokenToValue.Count; }
		}

		public IEnumerable<PropertyDef> GetKeys() {
			return tokenToKey.Values;
		}

		public IEnumerable<TValue> GetValues() {
			return tokenToValue.Values;
		}

		ScopeAndTokenKey GetTokenKey(PropertyDef propertyRef) {
			return new ScopeAndTokenKey(propertyRef);
		}

		internal abstract IPropertyRefKey GetRefKey(PropertyDef propertyRef);

		public TValue Find(PropertyDef propRef) {
			TValue value;
			tokenToValue.TryGetValue(GetTokenKey(propRef), out value);
			return value;
		}

		public TValue FindAny(PropertyDef propRef) {
			TValue value;
			if (tokenToValue.TryGetValue(GetTokenKey(propRef), out value))
				return value;

			refToValue.TryGetValue(GetRefKey(propRef), out value);
			return value;
		}

		public void Add(PropertyDef propDef, TValue value) {
			var tokenKey = GetTokenKey(propDef);
			tokenToValue[tokenKey] = value;
			tokenToKey[tokenKey] = propDef;

			refToValue[GetRefKey(propDef)] = value;
		}

		public void OnTypesRenamed() {
			var newFieldRefToDef = new Dictionary<IPropertyRefKey, TValue>(refToValue.Count);
			foreach (var kvp in refToValue)
				newFieldRefToDef[GetRefKey((PropertyDef)kvp.Key.PropertyDef)] = kvp.Value;
			refToValue = newFieldRefToDef;
		}
	}

	public class PropertyDefDict<TValue> : PropertyDefDictBase<TValue> {
		internal override IPropertyRefKey GetRefKey(PropertyDef propRef) {
			return new PropertyRefKey(propRef);
		}
	}

	public class PropertyDefAndDeclaringTypeDict<TValue> : PropertyDefDictBase<TValue> {
		internal override IPropertyRefKey GetRefKey(PropertyDef propRef) {
			return new PropertyRefAndDeclaringTypeKey(propRef);
		}
	}

	sealed class ScopeAndTokenKey {
		readonly IScope scope;
		readonly uint token;

		public ScopeAndTokenKey(TypeDef type)
			: this(type.Module, type.MDToken.Raw) {
		}

		public ScopeAndTokenKey(FieldDef field)
			: this(field.DeclaringType == null ? null : field.DeclaringType.Module, field.MDToken.Raw) {
		}

		public ScopeAndTokenKey(MethodDef method)
			: this(method.DeclaringType == null ? null : method.DeclaringType.Module, method.MDToken.Raw) {
		}

		public ScopeAndTokenKey(PropertyDef prop)
			: this(prop.DeclaringType == null ? null : prop.DeclaringType.Module, prop.MDToken.Raw) {
		}

		public ScopeAndTokenKey(EventDef evt)
			: this(evt.DeclaringType == null ? null : evt.DeclaringType.Module, evt.MDToken.Raw) {
		}

		public ScopeAndTokenKey(IScope scope, uint token) {
			this.scope = scope;
			this.token = token;
		}

		public override int GetHashCode() {
			return (int)token + GetHashCode(scope);
		}

		public override bool Equals(object obj) {
			var other = obj as ScopeAndTokenKey;
			if (other == null)
				return false;
			return token == other.token &&
				Equals(scope, other.scope);
		}

		public override string ToString() {
			return string.Format("{0:X8} {1}", token, scope);
		}

		static bool Equals(IScope a, IScope b) {
			if (a == b)
				return true;
			if (a == null || b == null)
				return false;
			return GetCanonicalizedScopeName(a) == GetCanonicalizedScopeName(b);
		}

		static int GetHashCode(IScope a) {
			if (a == null)
				return 0;
			return GetCanonicalizedScopeName(a).GetHashCode();
		}

		static string GetAssemblyName(IScope a) {
			switch (a.ScopeType) {
			case ScopeType.AssemblyRef:
				return ((AssemblyRef)a).Name.String;
			case ScopeType.ModuleDef:
				var asm = ((ModuleDef)a).Assembly;
				if (asm != null)
					return asm.Name.String;
				break;
			}
			return null;
		}

		static string GetCanonicalizedScopeName(IScope a) {
			if (a == null)
				return string.Empty;
			var asmName = GetAssemblyName(a);
			if (asmName != null) {
				// The version number should be ignored. Older code may reference an old version of
				// the assembly, but if the newer one has been loaded, that one is used.
				return asmName.ToUpperInvariant();
			}
			return a.ScopeName.ToUpperInvariant();
		}
	}

	interface IFieldRefKey {
		IField FieldRef { get; }
	}

	interface IMethodRefKey {
		IMethod MethodRef { get; }
	}

	interface IEventRefKey {
		EventDef EventDef { get; }
	}

	interface IPropertyRefKey {
		PropertyDef PropertyDef { get; }
	}

	sealed class FieldRefKey : IFieldRefKey {
		static SigComparerOptions SIG_COMPARER_FLAGS = SigComparerOptions.PrivateScopeFieldIsComparable;
		readonly IField fieldRef;

		public IField FieldRef {
			get { return fieldRef; }
		}

		public FieldRefKey(IField fieldRef) {
			this.fieldRef = fieldRef;
		}

		public override int GetHashCode() {
			return new SigComparer(SIG_COMPARER_FLAGS).GetHashCode(fieldRef);
		}

		public override bool Equals(object obj) {
			var other = obj as FieldRefKey;
			if (other == null)
				return false;
			return new SigComparer(SIG_COMPARER_FLAGS).Equals(fieldRef, other.fieldRef);
		}

		public override string ToString() {
			return fieldRef.ToString();
		}
	}

	sealed class MethodRefKey : IMethodRefKey {
		static SigComparerOptions SIG_COMPARER_FLAGS = SigComparerOptions.PrivateScopeMethodIsComparable;
		readonly IMethod methodRef;

		public IMethod MethodRef {
			get { return methodRef; }
		}

		public MethodRefKey(IMethod methodRef) {
			this.methodRef = methodRef;
		}

		public override int GetHashCode() {
			return new SigComparer(SIG_COMPARER_FLAGS).GetHashCode(methodRef);
		}

		public override bool Equals(object obj) {
			var other = obj as MethodRefKey;
			if (other == null)
				return false;
			return new SigComparer(SIG_COMPARER_FLAGS).Equals(methodRef, other.methodRef);
		}

		public override string ToString() {
			return methodRef.ToString();
		}
	}

	sealed class FieldRefAndDeclaringTypeKey : IFieldRefKey {
		static SigComparerOptions SIG_COMPARER_FLAGS = SigComparerOptions.CompareMethodFieldDeclaringType | SigComparerOptions.PrivateScopeFieldIsComparable;
		readonly IField fieldRef;

		public IField FieldRef {
			get { return fieldRef; }
		}

		public FieldRefAndDeclaringTypeKey(IField fieldRef) {
			this.fieldRef = fieldRef;
		}

		public override int GetHashCode() {
			return new SigComparer(SIG_COMPARER_FLAGS).GetHashCode(fieldRef);
		}

		public override bool Equals(object obj) {
			var other = obj as FieldRefAndDeclaringTypeKey;
			if (other == null)
				return false;
			return new SigComparer(SIG_COMPARER_FLAGS).Equals(fieldRef, other.fieldRef);
		}

		public override string ToString() {
			return fieldRef.ToString();
		}
	}

	sealed class MethodRefAndDeclaringTypeKey : IMethodRefKey {
		static SigComparerOptions SIG_COMPARER_FLAGS = SigComparerOptions.CompareMethodFieldDeclaringType | SigComparerOptions.PrivateScopeMethodIsComparable;
		readonly IMethod methodRef;

		public IMethod MethodRef {
			get { return methodRef; }
		}

		public MethodRefAndDeclaringTypeKey(IMethod methodRef) {
			this.methodRef = methodRef;
		}

		public override int GetHashCode() {
			return new SigComparer(SIG_COMPARER_FLAGS).GetHashCode(methodRef);
		}

		public override bool Equals(object obj) {
			var other = obj as MethodRefAndDeclaringTypeKey;
			if (other == null)
				return false;
			return new SigComparer(SIG_COMPARER_FLAGS).Equals(methodRef, other.methodRef);
		}

		public override string ToString() {
			return methodRef.ToString();
		}
	}

	sealed class EventRefKey : IEventRefKey {
		readonly EventDef eventRef;

		public EventDef EventDef {
			get { return eventRef; }
		}

		public EventRefKey(EventDef eventRef) {
			this.eventRef = eventRef;
		}

		public override int GetHashCode() {
			return new SigComparer().GetHashCode(eventRef);
		}

		public override bool Equals(object obj) {
			var other = obj as EventRefKey;
			if (other == null)
				return false;
			return new SigComparer().Equals(eventRef, other.eventRef);
		}

		public override string ToString() {
			return eventRef.ToString();
		}
	}

	sealed class EventRefAndDeclaringTypeKey : IEventRefKey {
		readonly EventDef eventRef;

		public EventDef EventDef {
			get { return eventRef; }
		}

		public EventRefAndDeclaringTypeKey(EventDef eventRef) {
			this.eventRef = eventRef;
		}

		public override int GetHashCode() {
			return new SigComparer(SigComparerOptions.CompareEventDeclaringType).GetHashCode(eventRef);
		}

		public override bool Equals(object obj) {
			var other = obj as EventRefAndDeclaringTypeKey;
			if (other == null)
				return false;
			return new SigComparer(SigComparerOptions.CompareEventDeclaringType).Equals(eventRef, other.eventRef);
		}

		public override string ToString() {
			return eventRef.ToString();
		}
	}

	sealed class PropertyRefKey : IPropertyRefKey {
		readonly PropertyDef propRef;

		public PropertyDef PropertyDef {
			get { return propRef; }
		}

		public PropertyRefKey(PropertyDef propRef) {
			this.propRef = propRef;
		}

		public override int GetHashCode() {
			return new SigComparer().GetHashCode(propRef);
		}

		public override bool Equals(object obj) {
			var other = obj as PropertyRefKey;
			if (other == null)
				return false;
			return new SigComparer().Equals(propRef, other.propRef);
		}

		public override string ToString() {
			return propRef.ToString();
		}
	}

	sealed class PropertyRefAndDeclaringTypeKey : IPropertyRefKey {
		readonly PropertyDef propRef;

		public PropertyDef PropertyDef {
			get { return propRef; }
		}

		public PropertyRefAndDeclaringTypeKey(PropertyDef propRef) {
			this.propRef = propRef;
		}

		public override int GetHashCode() {
			return new SigComparer(SigComparerOptions.ComparePropertyDeclaringType).GetHashCode(propRef);
		}

		public override bool Equals(object obj) {
			var other = obj as PropertyRefAndDeclaringTypeKey;
			if (other == null)
				return false;
			return new SigComparer(SigComparerOptions.ComparePropertyDeclaringType).Equals(propRef, other.propRef);
		}

		public override string ToString() {
			return propRef.ToString();
		}
	}
}
