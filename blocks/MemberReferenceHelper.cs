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

#if PORT
using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Metadata;

namespace de4dot.blocks.OLD_REMOVE {
	public class TypeDefinitionDict<TValue> {
		Dictionary<ScopeAndTokenKey, TValue> tokenToValue = new Dictionary<ScopeAndTokenKey, TValue>();
		Dictionary<ScopeAndTokenKey, TypeDefinition> tokenToKey = new Dictionary<ScopeAndTokenKey, TypeDefinition>();
		Dictionary<TypeReferenceKey, TValue> refToValue = new Dictionary<TypeReferenceKey, TValue>();
		Dictionary<TypeReferenceKey, TypeDefinition> refToKey = new Dictionary<TypeReferenceKey, TypeDefinition>();

		public int Count {
			get { return tokenToValue.Count; }
		}

		public IEnumerable<TypeDefinition> getKeys() {
			return tokenToKey.Values;
		}

		public IEnumerable<TValue> getValues() {
			return tokenToValue.Values;
		}

		ScopeAndTokenKey getTokenKey(TypeReference typeReference) {
			return new ScopeAndTokenKey(typeReference);
		}

		TypeReferenceKey getReferenceKey(TypeReference typeReference) {
			return new TypeReferenceKey(typeReference);
		}

		public TValue find(TypeReference typeReference) {
			TValue value;
			if (typeReference is TypeDefinition)
				tokenToValue.TryGetValue(getTokenKey(typeReference), out value);
			else
				refToValue.TryGetValue(getReferenceKey(typeReference), out value);
			return value;
		}

		public TValue findAny(TypeReference typeReference) {
			TValue value;
			if (tokenToValue.TryGetValue(getTokenKey(typeReference), out value))
				return value;

			refToValue.TryGetValue(getReferenceKey(typeReference), out value);
			return value;
		}

		public void add(TypeDefinition typeDefinition, TValue value) {
			var tokenKey = getTokenKey(typeDefinition);
			tokenToValue[tokenKey] = value;
			tokenToKey[tokenKey] = typeDefinition;

			var refKey = getReferenceKey(typeDefinition);
			if (!refToValue.ContainsKey(refKey) ||
				getAccessibilityOrder(typeDefinition) < getAccessibilityOrder(refToKey[refKey])) {
				refToKey[refKey] = typeDefinition;
				refToValue[refKey] = value;
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
		static int getAccessibilityOrder(TypeDefinition typeDefinition) {
			return accessibilityOrder[(int)typeDefinition.Attributes & 7];
		}

		public void onTypesRenamed() {
			var newTypeRefToValue = new Dictionary<TypeReferenceKey, TValue>(refToValue.Count);
			foreach (var kvp in refToValue)
				newTypeRefToValue[getReferenceKey((TypeDefinition)kvp.Key.TypeReference)] = kvp.Value;
			refToValue = newTypeRefToValue;
		}
	}

	public abstract class FieldDefinitionDictBase<TValue> {
		Dictionary<ScopeAndTokenKey, TValue> tokenToValue = new Dictionary<ScopeAndTokenKey, TValue>();
		Dictionary<ScopeAndTokenKey, FieldDefinition> tokenToKey = new Dictionary<ScopeAndTokenKey, FieldDefinition>();
		Dictionary<IFieldReferenceKey, TValue> refToValue = new Dictionary<IFieldReferenceKey, TValue>();
		Dictionary<IFieldReferenceKey, FieldDefinition> refToKey = new Dictionary<IFieldReferenceKey, FieldDefinition>();

		public int Count {
			get { return tokenToValue.Count; }
		}

		public IEnumerable<FieldDefinition> getKeys() {
			return tokenToKey.Values;
		}

		public IEnumerable<TValue> getValues() {
			return tokenToValue.Values;
		}

		ScopeAndTokenKey getTokenKey(FieldReference fieldReference) {
			return new ScopeAndTokenKey(fieldReference);
		}

		protected abstract IFieldReferenceKey getReferenceKey(FieldReference fieldReference);

		public TValue find(FieldReference fieldReference) {
			TValue value;
			if (fieldReference is FieldDefinition)
				tokenToValue.TryGetValue(getTokenKey(fieldReference), out value);
			else
				refToValue.TryGetValue(getReferenceKey(fieldReference), out value);
			return value;
		}

		public TValue findAny(FieldReference fieldReference) {
			TValue value;
			if (tokenToValue.TryGetValue(getTokenKey(fieldReference), out value))
				return value;

			refToValue.TryGetValue(getReferenceKey(fieldReference), out value);
			return value;
		}

		public void add(FieldDefinition fieldDefinition, TValue value) {
			var tokenKey = getTokenKey(fieldDefinition);
			tokenToValue[tokenKey] = value;
			tokenToKey[tokenKey] = fieldDefinition;

			var refKey = getReferenceKey(fieldDefinition);
			if (!refToValue.ContainsKey(refKey) ||
				getAccessibilityOrder(fieldDefinition) < getAccessibilityOrder(refToKey[refKey])) {
				refToKey[refKey] = fieldDefinition;
				refToValue[refKey] = value;
			}
		}

		// Order: public, family, assembly, private
		static int[] accessibilityOrder = new int[8] {
			60,		// CompilerControlled
			50,		// Private
			40,		// FamANDAssem
			30,		// Assembly
			10,		// Family
			20,		// FamORAssem
			0,		// Public
			70,		// <reserved>
		};
		static int getAccessibilityOrder(FieldDefinition fieldDefinition) {
			return accessibilityOrder[(int)fieldDefinition.Attributes & 7];
		}

		public void onTypesRenamed() {
			var newFieldRefToDef = new Dictionary<IFieldReferenceKey, TValue>(refToValue.Count);
			foreach (var kvp in refToValue)
				newFieldRefToDef[getReferenceKey((FieldDefinition)kvp.Key.FieldReference)] = kvp.Value;
			refToValue = newFieldRefToDef;
		}
	}

	public class FieldDefinitionDict<TValue> : FieldDefinitionDictBase<TValue> {
		protected override IFieldReferenceKey getReferenceKey(FieldReference fieldReference) {
			return new FieldReferenceKey(fieldReference);
		}
	}

	public class FieldDefinitionAndDeclaringTypeDict<TValue> : FieldDefinitionDictBase<TValue> {
		protected override IFieldReferenceKey getReferenceKey(FieldReference fieldReference) {
			return new FieldReferenceAndDeclaringTypeKey(fieldReference);
		}
	}

	public abstract class MethodDefinitionDictBase<TValue> {
		Dictionary<ScopeAndTokenKey, TValue> tokenToValue = new Dictionary<ScopeAndTokenKey, TValue>();
		Dictionary<ScopeAndTokenKey, MethodDefinition> tokenToKey = new Dictionary<ScopeAndTokenKey, MethodDefinition>();
		Dictionary<IMethodReferenceKey, TValue> refToValue = new Dictionary<IMethodReferenceKey, TValue>();
		Dictionary<IMethodReferenceKey, MethodDefinition> refToKey = new Dictionary<IMethodReferenceKey, MethodDefinition>();

		public int Count {
			get { return tokenToValue.Count; }
		}

		public IEnumerable<MethodDefinition> getKeys() {
			return tokenToKey.Values;
		}

		public IEnumerable<TValue> getValues() {
			return tokenToValue.Values;
		}

		ScopeAndTokenKey getTokenKey(MethodReference methodReference) {
			return new ScopeAndTokenKey(methodReference);
		}

		protected abstract IMethodReferenceKey getReferenceKey(MethodReference methodReference);

		public TValue find(MethodReference methodReference) {
			TValue value;
			if (methodReference is MethodDefinition)
				tokenToValue.TryGetValue(getTokenKey(methodReference), out value);
			else
				refToValue.TryGetValue(getReferenceKey(methodReference), out value);
			return value;
		}

		public TValue findAny(MethodReference methodReference) {
			TValue value;
			if (tokenToValue.TryGetValue(getTokenKey(methodReference), out value))
				return value;

			refToValue.TryGetValue(getReferenceKey(methodReference), out value);
			return value;
		}

		public void add(MethodDefinition methodDefinition, TValue value) {
			var tokenKey = getTokenKey(methodDefinition);
			tokenToValue[tokenKey] = value;
			tokenToKey[tokenKey] = methodDefinition;

			var refKey = getReferenceKey(methodDefinition);
			if (!refToValue.ContainsKey(refKey) ||
				getAccessibilityOrder(methodDefinition) < getAccessibilityOrder(refToKey[refKey])) {
				refToKey[refKey] = methodDefinition;
				refToValue[refKey] = value;
			}
		}

		// Order: public, family, assembly, private
		static int[] accessibilityOrder = new int[8] {
			60,		// CompilerControlled
			50,		// Private
			40,		// FamANDAssem
			30,		// Assembly
			10,		// Family
			20,		// FamORAssem
			0,		// Public
			70,		// <reserved>
		};
		static int getAccessibilityOrder(MethodDefinition methodDefinition) {
			return accessibilityOrder[(int)methodDefinition.Attributes & 7];
		}

		public void onTypesRenamed() {
			var newFieldRefToDef = new Dictionary<IMethodReferenceKey, TValue>(refToValue.Count);
			foreach (var kvp in refToValue)
				newFieldRefToDef[getReferenceKey((MethodDefinition)kvp.Key.MethodReference)] = kvp.Value;
			refToValue = newFieldRefToDef;
		}
	}

	public class MethodDefinitionDict<TValue> : MethodDefinitionDictBase<TValue> {
		protected override IMethodReferenceKey getReferenceKey(MethodReference methodReference) {
			return new MethodReferenceKey(methodReference);
		}
	}

	public class MethodDefinitionAndDeclaringTypeDict<TValue> : MethodDefinitionDictBase<TValue> {
		protected override IMethodReferenceKey getReferenceKey(MethodReference methodReference) {
			return new MethodReferenceAndDeclaringTypeKey(methodReference);
		}
	}

	public abstract class PropertyDefinitionDictBase<TValue> {
		Dictionary<ScopeAndTokenKey, TValue> tokenToValue = new Dictionary<ScopeAndTokenKey, TValue>();
		Dictionary<ScopeAndTokenKey, PropertyDefinition> tokenToKey = new Dictionary<ScopeAndTokenKey, PropertyDefinition>();
		Dictionary<IPropertyReferenceKey, TValue> refToValue = new Dictionary<IPropertyReferenceKey, TValue>();

		public int Count {
			get { return tokenToValue.Count; }
		}

		public IEnumerable<PropertyDefinition> getKeys() {
			return tokenToKey.Values;
		}

		public IEnumerable<TValue> getValues() {
			return tokenToValue.Values;
		}

		ScopeAndTokenKey getTokenKey(PropertyReference propertyReference) {
			return new ScopeAndTokenKey(propertyReference);
		}

		protected abstract IPropertyReferenceKey getReferenceKey(PropertyReference propertyReference);

		public TValue find(PropertyReference propertyReference) {
			TValue value;
			if (propertyReference is PropertyDefinition)
				tokenToValue.TryGetValue(getTokenKey(propertyReference), out value);
			else
				refToValue.TryGetValue(getReferenceKey(propertyReference), out value);
			return value;
		}

		public TValue findAny(PropertyReference propertyReference) {
			TValue value;
			if (tokenToValue.TryGetValue(getTokenKey(propertyReference), out value))
				return value;

			refToValue.TryGetValue(getReferenceKey(propertyReference), out value);
			return value;
		}

		public void add(PropertyDefinition propertyDefinition, TValue value) {
			var tokenKey = getTokenKey(propertyDefinition);
			tokenToValue[tokenKey] = value;
			tokenToKey[tokenKey] = propertyDefinition;

			refToValue[getReferenceKey(propertyDefinition)] = value;
		}

		public void onTypesRenamed() {
			var newFieldRefToDef = new Dictionary<IPropertyReferenceKey, TValue>(refToValue.Count);
			foreach (var kvp in refToValue)
				newFieldRefToDef[getReferenceKey((PropertyDefinition)kvp.Key.PropertyReference)] = kvp.Value;
			refToValue = newFieldRefToDef;
		}
	}

	public class PropertyDefinitionDict<TValue> : PropertyDefinitionDictBase<TValue> {
		protected override IPropertyReferenceKey getReferenceKey(PropertyReference propertyReference) {
			return new PropertyReferenceKey(propertyReference);
		}
	}

	public class PropertyDefinitionAndDeclaringTypeDict<TValue> : PropertyDefinitionDictBase<TValue> {
		protected override IPropertyReferenceKey getReferenceKey(PropertyReference propertyReference) {
			return new PropertyReferenceAndDeclaringTypeKey(propertyReference);
		}
	}

	public abstract class EventDefinitionDictBase<TValue> {
		Dictionary<ScopeAndTokenKey, TValue> tokenToValue = new Dictionary<ScopeAndTokenKey, TValue>();
		Dictionary<ScopeAndTokenKey, EventDefinition> tokenToKey = new Dictionary<ScopeAndTokenKey, EventDefinition>();
		Dictionary<IEventReferenceKey, TValue> refToValue = new Dictionary<IEventReferenceKey, TValue>();

		public int Count {
			get { return tokenToValue.Count; }
		}

		public IEnumerable<EventDefinition> getKeys() {
			return tokenToKey.Values;
		}

		public IEnumerable<TValue> getValues() {
			return tokenToValue.Values;
		}

		ScopeAndTokenKey getTokenKey(EventReference eventReference) {
			return new ScopeAndTokenKey(eventReference);
		}

		protected abstract IEventReferenceKey getReferenceKey(EventReference eventReference);

		public TValue find(EventReference eventReference) {
			TValue value;
			if (eventReference is EventDefinition)
				tokenToValue.TryGetValue(getTokenKey(eventReference), out value);
			else
				refToValue.TryGetValue(getReferenceKey(eventReference), out value);
			return value;
		}

		public TValue findAny(EventReference eventReference) {
			TValue value;
			if (tokenToValue.TryGetValue(getTokenKey(eventReference), out value))
				return value;

			refToValue.TryGetValue(getReferenceKey(eventReference), out value);
			return value;
		}

		public void add(EventDefinition eventDefinition, TValue value) {
			var tokenKey = getTokenKey(eventDefinition);
			tokenToValue[tokenKey] = value;
			tokenToKey[tokenKey] = eventDefinition;

			refToValue[getReferenceKey(eventDefinition)] = value;
		}

		public void onTypesRenamed() {
			var newFieldRefToDef = new Dictionary<IEventReferenceKey, TValue>(refToValue.Count);
			foreach (var kvp in refToValue)
				newFieldRefToDef[getReferenceKey((EventDefinition)kvp.Key.EventReference)] = kvp.Value;
			refToValue = newFieldRefToDef;
		}
	}

	public class EventDefinitionDict<TValue> : EventDefinitionDictBase<TValue> {
		protected override IEventReferenceKey getReferenceKey(EventReference eventReference) {
			return new EventReferenceKey(eventReference);
		}
	}

	public class EventDefinitionAndDeclaringTypeDict<TValue> : EventDefinitionDictBase<TValue> {
		protected override IEventReferenceKey getReferenceKey(EventReference eventReference) {
			return new EventReferenceAndDeclaringTypeKey(eventReference);
		}
	}

	public class ScopeAndTokenKey {
		readonly IMetadataScope scope;
		readonly int token;

		public ScopeAndTokenKey(TypeReference type)
			: this(type.Scope, type.MetadataToken.ToInt32()) {
		}

		public ScopeAndTokenKey(FieldReference field)
			: this(field.DeclaringType == null ? null : field.DeclaringType.Scope, field.MetadataToken.ToInt32()) {
		}

		public ScopeAndTokenKey(MethodReference method)
			: this(method.DeclaringType == null ? null : method.DeclaringType.Scope, method.MetadataToken.ToInt32()) {
		}

		public ScopeAndTokenKey(PropertyReference prop)
			: this(prop.DeclaringType == null ? null : prop.DeclaringType.Scope, prop.MetadataToken.ToInt32()) {
		}

		public ScopeAndTokenKey(EventReference evt)
			: this(evt.DeclaringType == null ? null : evt.DeclaringType.Scope, evt.MetadataToken.ToInt32()) {
		}

		public ScopeAndTokenKey(IMetadataScope scope, int token) {
			this.scope = scope;
			this.token = token;
		}

		public override int GetHashCode() {
			return token + MemberReferenceHelper.scopeHashCode(scope);
		}

		public override bool Equals(object obj) {
			var other = obj as ScopeAndTokenKey;
			if (other == null)
				return false;
			return token == other.token &&
				MemberReferenceHelper.compareScope(scope, other.scope);
		}

		public override string ToString() {
			return string.Format("{0:X8} {1}", token, scope);
		}
	}

	public class TypeReferenceKey {
		readonly TypeReference typeRef;

		public TypeReference TypeReference {
			get { return typeRef; }
		}

		public TypeReferenceKey(TypeReference typeRef) {
			this.typeRef = typeRef;
		}

		public override int GetHashCode() {
			throw new NotImplementedException();
		}

		public override bool Equals(object obj) {
			var other = obj as TypeReferenceKey;
			if (other == null)
				return false;
			throw new NotImplementedException();
		}

		public override string ToString() {
			return typeRef.ToString();
		}
	}

	public class TypeReferenceSameVersionKey {
		readonly TypeReference typeRef;

		public TypeReference TypeReference {
			get { return typeRef; }
		}

		public TypeReferenceSameVersionKey(TypeReference typeRef) {
			this.typeRef = typeRef;
		}

		public override int GetHashCode() {
			throw new NotImplementedException();
		}

		public override bool Equals(object obj) {
			var other = obj as TypeReferenceSameVersionKey;
			if (other == null)
				return false;
			throw new NotImplementedException();
		}

		public override string ToString() {
			return typeRef.ToString();
		}
	}

	public interface IFieldReferenceKey {
		FieldReference FieldReference { get; }
	}

	public interface IPropertyReferenceKey {
		PropertyReference PropertyReference { get; }
	}

	public interface IEventReferenceKey {
		EventReference EventReference { get; }
	}

	public interface IMethodReferenceKey {
		MethodReference MethodReference { get; }
	}

	public class FieldReferenceKey : IFieldReferenceKey {
		readonly FieldReference fieldRef;

		public FieldReference FieldReference {
			get { return fieldRef; }
		}

		public FieldReferenceKey(FieldReference fieldRef) {
			this.fieldRef = fieldRef;
		}

		public override int GetHashCode() {
			throw new NotImplementedException();
		}

		public override bool Equals(object obj) {
			var other = obj as FieldReferenceKey;
			if (other == null)
				return false;
			throw new NotImplementedException();
		}

		public override string ToString() {
			return fieldRef.ToString();
		}
	}

	public class PropertyReferenceKey : IPropertyReferenceKey {
		readonly PropertyReference propRef;

		public PropertyReference PropertyReference {
			get { return propRef; }
		}

		public PropertyReferenceKey(PropertyReference propRef) {
			this.propRef = propRef;
		}

		public override int GetHashCode() {
			throw new NotImplementedException();
		}

		public override bool Equals(object obj) {
			var other = obj as PropertyReferenceKey;
			if (other == null)
				return false;
			throw new NotImplementedException();
		}

		public override string ToString() {
			return propRef.ToString();
		}
	}

	public class EventReferenceKey : IEventReferenceKey {
		readonly EventReference eventRef;

		public EventReference EventReference {
			get { return eventRef; }
		}

		public EventReferenceKey(EventReference eventRef) {
			this.eventRef = eventRef;
		}

		public override int GetHashCode() {
			throw new NotImplementedException();
		}

		public override bool Equals(object obj) {
			var other = obj as EventReferenceKey;
			if (other == null)
				return false;
			throw new NotImplementedException();
		}

		public override string ToString() {
			return eventRef.ToString();
		}
	}

	public class MethodReferenceKey : IMethodReferenceKey {
		readonly MethodReference methodRef;

		public MethodReference MethodReference {
			get { return methodRef; }
		}

		public MethodReferenceKey(MethodReference methodRef) {
			this.methodRef = methodRef;
		}

		public override int GetHashCode() {
			throw new NotImplementedException();
		}

		public override bool Equals(object obj) {
			var other = obj as MethodReferenceKey;
			if (other == null)
				return false;
			throw new NotImplementedException();
		}

		public override string ToString() {
			return methodRef.ToString();
		}
	}

	public class FieldReferenceAndDeclaringTypeKey : IFieldReferenceKey {
		readonly FieldReference fieldRef;

		public FieldReference FieldReference {
			get { return fieldRef; }
		}

		public FieldReferenceAndDeclaringTypeKey(FieldReference fieldRef) {
			this.fieldRef = fieldRef;
		}

		public override int GetHashCode() {
			throw new NotImplementedException();
		}

		public override bool Equals(object obj) {
			var other = obj as FieldReferenceAndDeclaringTypeKey;
			if (other == null)
				return false;
			throw new NotImplementedException();
		}

		public override string ToString() {
			return fieldRef.ToString();
		}
	}

	public class PropertyReferenceAndDeclaringTypeKey : IPropertyReferenceKey {
		readonly PropertyReference propRef;

		public PropertyReference PropertyReference {
			get { return propRef; }
		}

		public PropertyReferenceAndDeclaringTypeKey(PropertyReference propRef) {
			this.propRef = propRef;
		}

		public override int GetHashCode() {
			throw new NotImplementedException();
		}

		public override bool Equals(object obj) {
			var other = obj as PropertyReferenceAndDeclaringTypeKey;
			if (other == null)
				return false;
			throw new NotImplementedException();
		}

		public override string ToString() {
			return propRef.ToString();
		}
	}

	public class EventReferenceAndDeclaringTypeKey : IEventReferenceKey {
		readonly EventReference eventRef;

		public EventReference EventReference {
			get { return eventRef; }
		}

		public EventReferenceAndDeclaringTypeKey(EventReference eventRef) {
			this.eventRef = eventRef;
		}

		public override int GetHashCode() {
			throw new NotImplementedException();
		}

		public override bool Equals(object obj) {
			var other = obj as EventReferenceAndDeclaringTypeKey;
			if (other == null)
				return false;
			throw new NotImplementedException();
		}

		public override string ToString() {
			return eventRef.ToString();
		}
	}

	public class MethodReferenceAndDeclaringTypeKey : IMethodReferenceKey {
		readonly MethodReference methodRef;

		public MethodReference MethodReference {
			get { return methodRef; }
		}

		public MethodReferenceAndDeclaringTypeKey(MethodReference methodRef) {
			this.methodRef = methodRef;
		}

		public override int GetHashCode() {
			throw new NotImplementedException();
		}

		public override bool Equals(object obj) {
			var other = obj as MethodReferenceAndDeclaringTypeKey;
			if (other == null)
				return false;
			throw new NotImplementedException();
		}

		public override string ToString() {
			return methodRef.ToString();
		}
	}
}

namespace de4dot.blocks {
	public static class MemberReferenceHelper {
		public static bool verifyType(TypeReference typeReference, string assembly, string type) {
			return verifyType(typeReference, assembly, type, "");
		}

		public static bool verifyType(TypeReference typeReference, string assembly, string type, string extra) {
			return typeReference != null &&
				MemberReferenceHelper.getCanonicalizedTypeRefName(typeReference.GetElementType()) == "[" + assembly + "]" + type &&
				typeReference.FullName == type + extra;
		}

		public static bool isSystemObject(TypeReference typeReference) {
			return typeReference != null && typeReference.EType == ElementType.Object;
		}

		public static string getCanonicalizedTypeRefName(TypeReference typeRef) {
			return getCanonicalizedTypeRefName(typeRef.Scope, typeRef.FullName);
		}

		public static string getCanonicalizedTypeRefName(IMetadataScope scope, string fullName) {
			return string.Format("[{0}]{1}", getCanonicalizedScopeName(scope), fullName);
		}

		public static AssemblyNameReference getAssemblyNameReference(IMetadataScope scope) {
			switch (scope.MetadataScopeType) {
			case MetadataScopeType.AssemblyNameReference:
				return (AssemblyNameReference)scope;
			case MetadataScopeType.ModuleDefinition:
				var module = (ModuleDefinition)scope;
				if (module.Assembly != null)
					return module.Assembly.Name;
				break;
			case MetadataScopeType.ModuleReference:
				break;
			default:
				throw new ApplicationException(string.Format("Invalid scope type: {0}", scope.GetType()));
			}

			return null;
		}

		public static string getCanonicalizedScopeName(IMetadataScope scope) {
			var asmRef = getAssemblyNameReference(scope);
			if (asmRef != null) {
				// The version number should be ignored. Older code may reference an old version of
				// the assembly, but if the newer one has been loaded, that one is used.
				return asmRef.Name.ToLowerInvariant();
			}
			return string.Format("{0}", scope.ToString().ToLowerInvariant());
		}

		public static string getCanonicalizedScopeAndVersion(IMetadataScope scope) {
			var asmRef = getAssemblyNameReference(scope);
			if (asmRef != null)
				return string.Format("{0}, Version={1}", asmRef.Name.ToLowerInvariant(), asmRef.Version);
			return string.Format("{0}, Version=", scope.ToString().ToLowerInvariant());
		}

		public static bool compareScope(IMetadataScope a, IMetadataScope b) {
			if (ReferenceEquals(a, b))
				return true;
			if (a == null || b == null)
				return false;
			return getCanonicalizedScopeName(a) == getCanonicalizedScopeName(b);
		}

		public static int scopeHashCode(IMetadataScope a) {
			if (a == null)
				return 0;
			return getCanonicalizedScopeName(a).GetHashCode();
		}

		public static bool compareScopeSameVersion(IMetadataScope a, IMetadataScope b) {
			if (ReferenceEquals(a, b))
				return true;
			if (a == null || b == null)
				return false;
			return getCanonicalizedScopeAndVersion(a) == getCanonicalizedScopeAndVersion(b);
		}

		public static int scopeHashCodeSameVersion(IMetadataScope a) {
			if (a == null)
				return 0;
			return getCanonicalizedScopeAndVersion(a).GetHashCode();
		}

		public static bool compareMethodReference(MethodReference a, MethodReference b) {
			throw new NotImplementedException();
		}

		public static bool compareMethodReferenceAndDeclaringType(MethodReference a, MethodReference b) {
			throw new NotImplementedException();
		}

		public static bool compareFieldReference(FieldReference a, FieldReference b) {
			throw new NotImplementedException();
		}

		public static bool compareTypes(TypeReference a, TypeReference b) {
			throw new NotImplementedException();
		}
	}
}
#endif
