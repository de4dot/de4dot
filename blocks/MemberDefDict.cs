/*
    Copyright (C) 2011-2012 de4dot@gmail.com

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
using dot10.DotNet;

namespace de4dot.blocks {
	public class TypeDefinitionDict<TValue> {
		Dictionary<ScopeAndTokenKey, TValue> tokenToValue = new Dictionary<ScopeAndTokenKey, TValue>();
		Dictionary<ScopeAndTokenKey, TypeDef> tokenToKey = new Dictionary<ScopeAndTokenKey, TypeDef>();
		Dictionary<IType, TValue> refToValue = new Dictionary<IType, TValue>(TypeEqualityComparer.Instance);
		Dictionary<IType, TypeDef> refToKey = new Dictionary<IType, TypeDef>();

		public int Count {
			get { return tokenToValue.Count; }
		}

		public IEnumerable<TypeDef> getKeys() {
			return tokenToKey.Values;
		}

		public IEnumerable<TValue> getValues() {
			return tokenToValue.Values;
		}

		ScopeAndTokenKey getTokenKey(TypeDef typeDef) {
			return new ScopeAndTokenKey(typeDef);
		}

		public TValue find(IType typeRef) {
			TValue value;
			var typeDef = typeRef as TypeDef;
			if (typeDef != null)
				tokenToValue.TryGetValue(getTokenKey(typeDef), out value);
			else if (typeRef != null)
				refToValue.TryGetValue(typeRef, out value);
			else
				value = default(TValue);
			return value;
		}

		public TValue findAny(IType type) {
			TValue value;
			var typeDef = type as TypeDef;
			if (typeDef != null && tokenToValue.TryGetValue(getTokenKey(typeDef), out value))
				return value;

			refToValue.TryGetValue(type, out value);
			return value;
		}

		public void add(TypeDef typeDef, TValue value) {
			var tokenKey = getTokenKey(typeDef);
			tokenToValue[tokenKey] = value;
			tokenToKey[tokenKey] = typeDef;

			if (!refToValue.ContainsKey(typeDef) ||
				getAccessibilityOrder(typeDef) < getAccessibilityOrder(refToKey[typeDef])) {
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
		static int getAccessibilityOrder(TypeDef typeDef) {
			return accessibilityOrder[(int)typeDef.Flags & 7];
		}

		public void onTypesRenamed() {
			var newTypeRefToValue = new Dictionary<IType, TValue>(refToValue.Count);
			foreach (var kvp in refToValue)
				newTypeRefToValue[kvp.Key] = kvp.Value;
			refToValue = newTypeRefToValue;
		}
	}

	public abstract class FieldDefinitionDictBase<TValue> {
		Dictionary<ScopeAndTokenKey, TValue> tokenToValue = new Dictionary<ScopeAndTokenKey, TValue>();
		Dictionary<ScopeAndTokenKey, FieldDef> tokenToKey = new Dictionary<ScopeAndTokenKey, FieldDef>();
		Dictionary<IFieldReferenceKey, TValue> refToValue = new Dictionary<IFieldReferenceKey, TValue>();
		Dictionary<IFieldReferenceKey, FieldDef> refToKey = new Dictionary<IFieldReferenceKey, FieldDef>();

		public int Count {
			get { return tokenToValue.Count; }
		}

		public IEnumerable<FieldDef> getKeys() {
			return tokenToKey.Values;
		}

		public IEnumerable<TValue> getValues() {
			return tokenToValue.Values;
		}

		ScopeAndTokenKey getTokenKey(FieldDef fieldDef) {
			return new ScopeAndTokenKey(fieldDef);
		}

		internal abstract IFieldReferenceKey getReferenceKey(IField fieldRef);

		public TValue find(IField fieldRef) {
			TValue value;
			var fieldDef = fieldRef as FieldDef;
			if (fieldDef != null)
				tokenToValue.TryGetValue(getTokenKey(fieldDef), out value);
			else
				refToValue.TryGetValue(getReferenceKey(fieldRef), out value);
			return value;
		}

		public TValue findAny(IField fieldRef) {
			TValue value;
			var fieldDef = fieldRef as FieldDef;
			if (fieldDef != null && tokenToValue.TryGetValue(getTokenKey(fieldDef), out value))
				return value;

			refToValue.TryGetValue(getReferenceKey(fieldRef), out value);
			return value;
		}

		public void add(FieldDef fieldDef, TValue value) {
			var tokenKey = getTokenKey(fieldDef);
			tokenToValue[tokenKey] = value;
			tokenToKey[tokenKey] = fieldDef;

			var refKey = getReferenceKey(fieldDef);
			if (!refToValue.ContainsKey(refKey) ||
				getAccessibilityOrder(fieldDef) < getAccessibilityOrder(refToKey[refKey])) {
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
		static int getAccessibilityOrder(FieldDef fieldDefinition) {
			return accessibilityOrder[(int)fieldDefinition.Flags & 7];
		}

		public void onTypesRenamed() {
			var newFieldRefToDef = new Dictionary<IFieldReferenceKey, TValue>(refToValue.Count);
			foreach (var kvp in refToValue)
				newFieldRefToDef[getReferenceKey((FieldDef)kvp.Key.FieldReference)] = kvp.Value;
			refToValue = newFieldRefToDef;
		}
	}

	public class FieldDefinitionDict<TValue> : FieldDefinitionDictBase<TValue> {
		internal override IFieldReferenceKey getReferenceKey(IField fieldRef) {
			return new FieldReferenceKey(fieldRef);
		}
	}

	public class FieldDefinitionAndDeclaringTypeDict<TValue> : FieldDefinitionDictBase<TValue> {
		internal override IFieldReferenceKey getReferenceKey(IField fieldRef) {
			return new FieldReferenceAndDeclaringTypeKey(fieldRef);
		}
	}

	public abstract class MethodDefinitionDictBase<TValue> {
		Dictionary<ScopeAndTokenKey, TValue> tokenToValue = new Dictionary<ScopeAndTokenKey, TValue>();
		Dictionary<ScopeAndTokenKey, MethodDef> tokenToKey = new Dictionary<ScopeAndTokenKey, MethodDef>();
		Dictionary<IMethodReferenceKey, TValue> refToValue = new Dictionary<IMethodReferenceKey, TValue>();
		Dictionary<IMethodReferenceKey, MethodDef> refToKey = new Dictionary<IMethodReferenceKey, MethodDef>();

		public int Count {
			get { return tokenToValue.Count; }
		}

		public IEnumerable<MethodDef> getKeys() {
			return tokenToKey.Values;
		}

		public IEnumerable<TValue> getValues() {
			return tokenToValue.Values;
		}

		ScopeAndTokenKey getTokenKey(MethodDef methodDef) {
			return new ScopeAndTokenKey(methodDef);
		}

		internal abstract IMethodReferenceKey getReferenceKey(IMethod methodRef);

		public TValue find(IMethod methodRef) {
			TValue value;
			var methodDef = methodRef as MethodDef;
			if (methodDef != null)
				tokenToValue.TryGetValue(getTokenKey(methodDef), out value);
			else
				refToValue.TryGetValue(getReferenceKey(methodRef), out value);
			return value;
		}

		public TValue findAny(IMethod methodRef) {
			TValue value;
			var methodDef = methodRef as MethodDef;
			if (methodDef != null && tokenToValue.TryGetValue(getTokenKey(methodDef), out value))
				return value;

			refToValue.TryGetValue(getReferenceKey(methodRef), out value);
			return value;
		}

		public void add(MethodDef methodDef, TValue value) {
			var tokenKey = getTokenKey(methodDef);
			tokenToValue[tokenKey] = value;
			tokenToKey[tokenKey] = methodDef;

			var refKey = getReferenceKey(methodDef);
			if (!refToValue.ContainsKey(refKey) ||
				getAccessibilityOrder(methodDef) < getAccessibilityOrder(refToKey[refKey])) {
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
		static int getAccessibilityOrder(MethodDef methodDefinition) {
			return accessibilityOrder[(int)methodDefinition.Flags & 7];
		}

		public void onTypesRenamed() {
			var newFieldRefToDef = new Dictionary<IMethodReferenceKey, TValue>(refToValue.Count);
			foreach (var kvp in refToValue)
				newFieldRefToDef[getReferenceKey((MethodDef)kvp.Key.MethodReference)] = kvp.Value;
			refToValue = newFieldRefToDef;
		}
	}

	public class MethodDefinitionDict<TValue> : MethodDefinitionDictBase<TValue> {
		internal override IMethodReferenceKey getReferenceKey(IMethod methodRef) {
			return new MethodReferenceKey(methodRef);
		}
	}

	public class MethodDefinitionAndDeclaringTypeDict<TValue> : MethodDefinitionDictBase<TValue> {
		internal override IMethodReferenceKey getReferenceKey(IMethod methodRef) {
			return new MethodReferenceAndDeclaringTypeKey(methodRef);
		}
	}

	public abstract class EventDefinitionDictBase<TValue> {
		Dictionary<ScopeAndTokenKey, TValue> tokenToValue = new Dictionary<ScopeAndTokenKey, TValue>();
		Dictionary<ScopeAndTokenKey, EventDef> tokenToKey = new Dictionary<ScopeAndTokenKey, EventDef>();
		Dictionary<IEventReferenceKey, TValue> refToValue = new Dictionary<IEventReferenceKey, TValue>();

		public int Count {
			get { return tokenToValue.Count; }
		}

		public IEnumerable<EventDef> getKeys() {
			return tokenToKey.Values;
		}

		public IEnumerable<TValue> getValues() {
			return tokenToValue.Values;
		}

		ScopeAndTokenKey getTokenKey(EventDef eventReference) {
			return new ScopeAndTokenKey(eventReference);
		}

		internal abstract IEventReferenceKey getReferenceKey(EventDef eventRef);

		public TValue find(EventDef eventRef) {
			TValue value;
			tokenToValue.TryGetValue(getTokenKey(eventRef), out value);
			return value;
		}

		public TValue findAny(EventDef eventRef) {
			TValue value;
			if (tokenToValue.TryGetValue(getTokenKey(eventRef), out value))
				return value;

			refToValue.TryGetValue(getReferenceKey(eventRef), out value);
			return value;
		}

		public void add(EventDef eventDef, TValue value) {
			var tokenKey = getTokenKey(eventDef);
			tokenToValue[tokenKey] = value;
			tokenToKey[tokenKey] = eventDef;

			refToValue[getReferenceKey(eventDef)] = value;
		}

		public void onTypesRenamed() {
			var newFieldRefToDef = new Dictionary<IEventReferenceKey, TValue>(refToValue.Count);
			foreach (var kvp in refToValue)
				newFieldRefToDef[getReferenceKey((EventDef)kvp.Key.EventDef)] = kvp.Value;
			refToValue = newFieldRefToDef;
		}
	}

	public class EventDefinitionDict<TValue> : EventDefinitionDictBase<TValue> {
		internal override IEventReferenceKey getReferenceKey(EventDef eventRef) {
			return new EventReferenceKey(eventRef);
		}
	}

	public class EventDefinitionAndDeclaringTypeDict<TValue> : EventDefinitionDictBase<TValue> {
		internal override IEventReferenceKey getReferenceKey(EventDef eventRef) {
			return new EventReferenceAndDeclaringTypeKey(eventRef);
		}
	}

	public abstract class PropertyDefinitionDictBase<TValue> {
		Dictionary<ScopeAndTokenKey, TValue> tokenToValue = new Dictionary<ScopeAndTokenKey, TValue>();
		Dictionary<ScopeAndTokenKey, PropertyDef> tokenToKey = new Dictionary<ScopeAndTokenKey, PropertyDef>();
		Dictionary<IPropertyReferenceKey, TValue> refToValue = new Dictionary<IPropertyReferenceKey, TValue>();

		public int Count {
			get { return tokenToValue.Count; }
		}

		public IEnumerable<PropertyDef> getKeys() {
			return tokenToKey.Values;
		}

		public IEnumerable<TValue> getValues() {
			return tokenToValue.Values;
		}

		ScopeAndTokenKey getTokenKey(PropertyDef propertyReference) {
			return new ScopeAndTokenKey(propertyReference);
		}

		internal abstract IPropertyReferenceKey getReferenceKey(PropertyDef propertyReference);

		public TValue find(PropertyDef propRef) {
			TValue value;
			tokenToValue.TryGetValue(getTokenKey(propRef), out value);
			return value;
		}

		public TValue findAny(PropertyDef propRef) {
			TValue value;
			if (tokenToValue.TryGetValue(getTokenKey(propRef), out value))
				return value;

			refToValue.TryGetValue(getReferenceKey(propRef), out value);
			return value;
		}

		public void add(PropertyDef propDef, TValue value) {
			var tokenKey = getTokenKey(propDef);
			tokenToValue[tokenKey] = value;
			tokenToKey[tokenKey] = propDef;

			refToValue[getReferenceKey(propDef)] = value;
		}

		public void onTypesRenamed() {
			var newFieldRefToDef = new Dictionary<IPropertyReferenceKey, TValue>(refToValue.Count);
			foreach (var kvp in refToValue)
				newFieldRefToDef[getReferenceKey((PropertyDef)kvp.Key.PropertyDef)] = kvp.Value;
			refToValue = newFieldRefToDef;
		}
	}

	public class PropertyDefinitionDict<TValue> : PropertyDefinitionDictBase<TValue> {
		internal override IPropertyReferenceKey getReferenceKey(PropertyDef propRef) {
			return new PropertyReferenceKey(propRef);
		}
	}

	public class PropertyDefinitionAndDeclaringTypeDict<TValue> : PropertyDefinitionDictBase<TValue> {
		internal override IPropertyReferenceKey getReferenceKey(PropertyDef propRef) {
			return new PropertyReferenceAndDeclaringTypeKey(propRef);
		}
	}

	sealed class ScopeAndTokenKey {
		readonly IScope scope;
		readonly uint token;

		public ScopeAndTokenKey(TypeDef type)
			: this(type.OwnerModule, type.MDToken.Raw) {
		}

		public ScopeAndTokenKey(FieldDef field)
			: this(field.DeclaringType == null ? null : field.DeclaringType.OwnerModule, field.MDToken.Raw) {
		}

		public ScopeAndTokenKey(MethodDef method)
			: this(method.DeclaringType == null ? null : method.DeclaringType.OwnerModule, method.MDToken.Raw) {
		}

		public ScopeAndTokenKey(PropertyDef prop)
			: this(prop.DeclaringType == null ? null : prop.DeclaringType.OwnerModule, prop.MDToken.Raw) {
		}

		public ScopeAndTokenKey(EventDef evt)
			: this(evt.DeclaringType == null ? null : evt.DeclaringType.OwnerModule, evt.MDToken.Raw) {
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
			return getCanonicalizedScopeName(a) == getCanonicalizedScopeName(b);
		}

		static int GetHashCode(IScope a) {
			if (a == null)
				return 0;
			return getCanonicalizedScopeName(a).GetHashCode();
		}

		static string getAssemblyName(IScope a) {
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

		static string getCanonicalizedScopeName(IScope a) {
			if (a == null)
				return string.Empty;
			var asmName = getAssemblyName(a);
			if (asmName != null) {
				// The version number should be ignored. Older code may reference an old version of
				// the assembly, but if the newer one has been loaded, that one is used.
				return asmName.ToUpperInvariant();
			}
			return a.ScopeName.ToUpperInvariant();
		}
	}

	interface IFieldReferenceKey {
		IField FieldReference { get; }
	}

	interface IMethodReferenceKey {
		IMethod MethodReference { get; }
	}

	interface IEventReferenceKey {
		EventDef EventDef { get; }
	}

	interface IPropertyReferenceKey {
		PropertyDef PropertyDef { get; }
	}

	sealed class FieldReferenceKey : IFieldReferenceKey {
		readonly IField fieldRef;

		public IField FieldReference {
			get { return fieldRef; }
		}

		public FieldReferenceKey(IField fieldRef) {
			this.fieldRef = fieldRef;
		}

		public override int GetHashCode() {
			return new SigComparer().GetHashCode(fieldRef);
		}

		public override bool Equals(object obj) {
			var other = obj as FieldReferenceKey;
			if (other == null)
				return false;
			return new SigComparer().Equals(fieldRef, other.fieldRef);
		}

		public override string ToString() {
			return fieldRef.ToString();
		}
	}

	sealed class MethodReferenceKey : IMethodReferenceKey {
		readonly IMethod methodRef;

		public IMethod MethodReference {
			get { return methodRef; }
		}

		public MethodReferenceKey(IMethod methodRef) {
			this.methodRef = methodRef;
		}

		public override int GetHashCode() {
			return new SigComparer().GetHashCode(methodRef);
		}

		public override bool Equals(object obj) {
			var other = obj as MethodReferenceKey;
			if (other == null)
				return false;
			return new SigComparer().Equals(methodRef, other.methodRef);
		}

		public override string ToString() {
			return methodRef.ToString();
		}
	}

	sealed class FieldReferenceAndDeclaringTypeKey : IFieldReferenceKey {
		readonly IField fieldRef;

		public IField FieldReference {
			get { return fieldRef; }
		}

		public FieldReferenceAndDeclaringTypeKey(IField fieldRef) {
			this.fieldRef = fieldRef;
		}

		public override int GetHashCode() {
			return new SigComparer(SigComparerOptions.CompareMethodFieldDeclaringType).GetHashCode(fieldRef);
		}

		public override bool Equals(object obj) {
			var other = obj as FieldReferenceAndDeclaringTypeKey;
			if (other == null)
				return false;
			return new SigComparer(SigComparerOptions.CompareMethodFieldDeclaringType).Equals(fieldRef, other.fieldRef);
		}

		public override string ToString() {
			return fieldRef.ToString();
		}
	}

	sealed class MethodReferenceAndDeclaringTypeKey : IMethodReferenceKey {
		readonly IMethod methodRef;

		public IMethod MethodReference {
			get { return methodRef; }
		}

		public MethodReferenceAndDeclaringTypeKey(IMethod methodRef) {
			this.methodRef = methodRef;
		}

		public override int GetHashCode() {
			return new SigComparer(SigComparerOptions.CompareMethodFieldDeclaringType).GetHashCode(methodRef);
		}

		public override bool Equals(object obj) {
			var other = obj as MethodReferenceAndDeclaringTypeKey;
			if (other == null)
				return false;
			return new SigComparer(SigComparerOptions.CompareMethodFieldDeclaringType).Equals(methodRef, other.methodRef);
		}

		public override string ToString() {
			return methodRef.ToString();
		}
	}

	sealed class EventReferenceKey : IEventReferenceKey {
		readonly EventDef eventRef;

		public EventDef EventDef {
			get { return eventRef; }
		}

		public EventReferenceKey(EventDef eventRef) {
			this.eventRef = eventRef;
		}

		public override int GetHashCode() {
			return new SigComparer().GetHashCode(eventRef);
		}

		public override bool Equals(object obj) {
			var other = obj as EventReferenceKey;
			if (other == null)
				return false;
			return new SigComparer().Equals(eventRef, other.eventRef);
		}

		public override string ToString() {
			return eventRef.ToString();
		}
	}

	sealed class EventReferenceAndDeclaringTypeKey : IEventReferenceKey {
		readonly EventDef eventRef;

		public EventDef EventDef {
			get { return eventRef; }
		}

		public EventReferenceAndDeclaringTypeKey(EventDef eventRef) {
			this.eventRef = eventRef;
		}

		public override int GetHashCode() {
			return new SigComparer(SigComparerOptions.CompareEventDeclaringType).GetHashCode(eventRef);
		}

		public override bool Equals(object obj) {
			var other = obj as EventReferenceAndDeclaringTypeKey;
			if (other == null)
				return false;
			return new SigComparer(SigComparerOptions.CompareEventDeclaringType).Equals(eventRef, other.eventRef);
		}

		public override string ToString() {
			return eventRef.ToString();
		}
	}

	sealed class PropertyReferenceKey : IPropertyReferenceKey {
		readonly PropertyDef propRef;

		public PropertyDef PropertyDef {
			get { return propRef; }
		}

		public PropertyReferenceKey(PropertyDef propRef) {
			this.propRef = propRef;
		}

		public override int GetHashCode() {
			return new SigComparer().GetHashCode(propRef);
		}

		public override bool Equals(object obj) {
			var other = obj as PropertyReferenceKey;
			if (other == null)
				return false;
			return new SigComparer().Equals(propRef, other.propRef);
		}

		public override string ToString() {
			return propRef.ToString();
		}
	}

	sealed class PropertyReferenceAndDeclaringTypeKey : IPropertyReferenceKey {
		readonly PropertyDef propRef;

		public PropertyDef PropertyDef {
			get { return propRef; }
		}

		public PropertyReferenceAndDeclaringTypeKey(PropertyDef propRef) {
			this.propRef = propRef;
		}

		public override int GetHashCode() {
			return new SigComparer(SigComparerOptions.ComparePropertyDeclaringType).GetHashCode(propRef);
		}

		public override bool Equals(object obj) {
			var other = obj as PropertyReferenceAndDeclaringTypeKey;
			if (other == null)
				return false;
			return new SigComparer(SigComparerOptions.ComparePropertyDeclaringType).Equals(propRef, other.propRef);
		}

		public override string ToString() {
			return propRef.ToString();
		}
	}
}
