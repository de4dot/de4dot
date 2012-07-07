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
using Mono.Cecil;
using Mono.Cecil.Metadata;

namespace de4dot.blocks {
	public enum CecilType {
		ArrayType,
		ByReferenceType,
		EventDefinition,
		FieldDefinition,
		FieldReference,
		FunctionPointerType,
		GenericInstanceMethod,
		GenericInstanceType,
		GenericParameter,
		MethodDefinition,
		MethodReference,
		OptionalModifierType,
		PinnedType,
		PointerType,
		PropertyDefinition,
		RequiredModifierType,
		SentinelType,
		TypeDefinition,
		TypeReference,
	}

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
			return MemberReferenceHelper.typeHashCode(typeRef);
		}

		public override bool Equals(object obj) {
			var other = obj as TypeReferenceKey;
			if (other == null)
				return false;
			return MemberReferenceHelper.compareTypes(typeRef, other.typeRef);
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
			return MemberReferenceHelper.typeReferenceHashCodeSameVersion(typeRef);
		}

		public override bool Equals(object obj) {
			var other = obj as TypeReferenceSameVersionKey;
			if (other == null)
				return false;
			return MemberReferenceHelper.compareTypeReferenceSameVersion(typeRef, other.typeRef);
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
			return MemberReferenceHelper.fieldReferenceHashCode(fieldRef);
		}

		public override bool Equals(object obj) {
			var other = obj as FieldReferenceKey;
			if (other == null)
				return false;
			return MemberReferenceHelper.compareFieldReference(fieldRef, other.fieldRef);
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
			return MemberReferenceHelper.propertyReferenceHashCode(propRef);
		}

		public override bool Equals(object obj) {
			var other = obj as PropertyReferenceKey;
			if (other == null)
				return false;
			return MemberReferenceHelper.comparePropertyReference(propRef, other.propRef);
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
			return MemberReferenceHelper.eventReferenceHashCode(eventRef);
		}

		public override bool Equals(object obj) {
			var other = obj as EventReferenceKey;
			if (other == null)
				return false;
			return MemberReferenceHelper.compareEventReference(eventRef, other.eventRef);
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
			return MemberReferenceHelper.methodReferenceHashCode(methodRef);
		}

		public override bool Equals(object obj) {
			var other = obj as MethodReferenceKey;
			if (other == null)
				return false;
			return MemberReferenceHelper.compareMethodReference(methodRef, other.methodRef);
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
			return MemberReferenceHelper.fieldReferenceHashCode(fieldRef) +
					MemberReferenceHelper.typeHashCode(fieldRef.DeclaringType);
		}

		public override bool Equals(object obj) {
			var other = obj as FieldReferenceAndDeclaringTypeKey;
			if (other == null)
				return false;
			return MemberReferenceHelper.compareFieldReference(fieldRef, other.fieldRef) &&
					MemberReferenceHelper.compareTypes(fieldRef.DeclaringType, other.fieldRef.DeclaringType);
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
			return MemberReferenceHelper.propertyReferenceHashCode(propRef) +
					MemberReferenceHelper.typeHashCode(propRef.DeclaringType);
		}

		public override bool Equals(object obj) {
			var other = obj as PropertyReferenceAndDeclaringTypeKey;
			if (other == null)
				return false;
			return MemberReferenceHelper.comparePropertyReference(propRef, other.propRef) &&
					MemberReferenceHelper.compareTypes(propRef.DeclaringType, other.propRef.DeclaringType);
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
			return MemberReferenceHelper.eventReferenceHashCode(eventRef) +
					MemberReferenceHelper.typeHashCode(eventRef.DeclaringType);
		}

		public override bool Equals(object obj) {
			var other = obj as EventReferenceAndDeclaringTypeKey;
			if (other == null)
				return false;
			return MemberReferenceHelper.compareEventReference(eventRef, other.eventRef) &&
					MemberReferenceHelper.compareTypes(eventRef.DeclaringType, other.eventRef.DeclaringType);
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
			return MemberReferenceHelper.methodReferenceHashCode(methodRef) +
					MemberReferenceHelper.typeHashCode(methodRef.DeclaringType);
		}

		public override bool Equals(object obj) {
			var other = obj as MethodReferenceAndDeclaringTypeKey;
			if (other == null)
				return false;
			return MemberReferenceHelper.compareMethodReference(methodRef, other.methodRef) &&
					MemberReferenceHelper.compareTypes(methodRef.DeclaringType, other.methodRef.DeclaringType);
		}

		public override string ToString() {
			return methodRef.ToString();
		}
	}

	public static class MemberReferenceHelper {
		static Dictionary<Type, CecilType> typeToCecilTypeDict = new Dictionary<Type, CecilType>();
		static MemberReferenceHelper() {
			typeToCecilTypeDict[typeof(ArrayType)] = CecilType.ArrayType;
			typeToCecilTypeDict[typeof(ByReferenceType)] = CecilType.ByReferenceType;
			typeToCecilTypeDict[typeof(EventDefinition)] = CecilType.EventDefinition;
			typeToCecilTypeDict[typeof(FieldDefinition)] = CecilType.FieldDefinition;
			typeToCecilTypeDict[typeof(FieldReference)] = CecilType.FieldReference;
			typeToCecilTypeDict[typeof(FunctionPointerType)] = CecilType.FunctionPointerType;
			typeToCecilTypeDict[typeof(GenericInstanceMethod)] = CecilType.GenericInstanceMethod;
			typeToCecilTypeDict[typeof(GenericInstanceType)] = CecilType.GenericInstanceType;
			typeToCecilTypeDict[typeof(GenericParameter)] = CecilType.GenericParameter;
			typeToCecilTypeDict[typeof(MethodDefinition)] = CecilType.MethodDefinition;
			typeToCecilTypeDict[typeof(MethodReference)] = CecilType.MethodReference;
			typeToCecilTypeDict[typeof(OptionalModifierType)] = CecilType.OptionalModifierType;
			typeToCecilTypeDict[typeof(PinnedType)] = CecilType.PinnedType;
			typeToCecilTypeDict[typeof(PointerType)] = CecilType.PointerType;
			typeToCecilTypeDict[typeof(PropertyDefinition)] = CecilType.PropertyDefinition;
			typeToCecilTypeDict[typeof(RequiredModifierType)] = CecilType.RequiredModifierType;
			typeToCecilTypeDict[typeof(SentinelType)] = CecilType.SentinelType;
			typeToCecilTypeDict[typeof(TypeDefinition)] = CecilType.TypeDefinition;
			typeToCecilTypeDict[typeof(TypeReference)] = CecilType.TypeReference;
		}

		public static CecilType getMemberReferenceType(MemberReference memberReference) {
			CecilType cecilType;
			var type = memberReference.GetType();
			if (typeToCecilTypeDict.TryGetValue(type, out cecilType))
				return cecilType;
			throw new ApplicationException(string.Format("Unknown MemberReference type: {0}", type));
		}

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

		public static bool compareEventReference(EventReference a, EventReference b) {
			if (ReferenceEquals(a, b))
				return true;
			if (a == null || b == null)
				return false;
			return a.Name == b.Name &&
					compareTypes(a.EventType, b.EventType);
		}

		public static int eventReferenceHashCode(EventReference a) {
			if (a == null)
				return 0;
			int res = 0;
			res += a.Name.GetHashCode();
			res += typeHashCode(a.EventType);
			return res;
		}

		public static bool compareFieldReferenceAndDeclaringType(FieldReference a, FieldReference b) {
			if (!compareFieldReference(a, b))
				return false;
			if (a == null)
				return true;
			return compareTypes(a.DeclaringType, b.DeclaringType);
		}

		public static bool compareFieldReference(FieldReference a, FieldReference b) {
			if (ReferenceEquals(a, b))
				return true;
			if (a == null || b == null)
				return false;
			return a.Name == b.Name &&
				compareTypes(a.FieldType, b.FieldType);
		}

		public static int fieldReferenceHashCode(FieldReference a) {
			if (a == null)
				return 0;
			int res = 0;
			res += a.Name.GetHashCode();
			res += typeHashCode(a.FieldType);
			return res;
		}

		public static bool compareMethodReferenceAndDeclaringType(MethodReference a, MethodReference b) {
			if (!compareMethodReference(a, b))
				return false;
			if (a == null)
				return true;
			return compareTypes(a.DeclaringType, b.DeclaringType);
		}

		public static bool compareMethodReferenceSignature(MethodReference a, MethodReference b) {
			if (ReferenceEquals(a, b))
				return true;
			if (a == null || b == null)
				return false;
			if (a.HasThis != b.HasThis || a.ExplicitThis != b.ExplicitThis)
				return false;
			if (a.CallingConvention != b.CallingConvention)
				return false;
			if (!compareTypes(a.MethodReturnType.ReturnType, b.MethodReturnType.ReturnType))
				return false;
			if (a.HasParameters != b.HasParameters)
				return false;
			if (a.HasParameters) {
				if (a.Parameters.Count != b.Parameters.Count)
					return false;
				for (int i = 0; i < a.Parameters.Count; i++) {
					if (!compareTypes(a.Parameters[i].ParameterType, b.Parameters[i].ParameterType))
						return false;
				}
			}
			if (a.HasGenericParameters != b.HasGenericParameters)
				return false;
			if (a.HasGenericParameters && a.GenericParameters.Count != b.GenericParameters.Count)
				return false;

			return true;
		}

		public static int methodReferenceSignatureHashCode(MethodReference a) {
			if (a == null)
				return 0;
			int res = 0;

			res += a.HasThis.GetHashCode();
			res += a.ExplicitThis.GetHashCode();
			res += a.CallingConvention.GetHashCode();
			res += typeHashCode(a.MethodReturnType.ReturnType);
			res += a.HasParameters.GetHashCode();
			if (a.HasParameters) {
				res += a.Parameters.Count.GetHashCode();
				foreach (var param in a.Parameters)
					res += typeHashCode(param.ParameterType);
			}
			res += a.HasGenericParameters.GetHashCode();
			if (a.HasGenericParameters)
				res += a.GenericParameters.Count.GetHashCode();

			return res;
		}

		public static bool compareMethodReference(MethodReference a, MethodReference b) {
			if (ReferenceEquals(a, b))
				return true;
			if (a == null || b == null)
				return false;

			if (a.Name != b.Name)
				return false;
			return compareMethodReferenceSignature(a, b);
		}

		public static int methodReferenceAndDeclaringTypeHashCode(MethodReference a) {
			if (a == null)
				return 0;
			return methodReferenceHashCode(a) + typeReferenceHashCode(a.DeclaringType);
		}

		public static int methodReferenceHashCode(MethodReference a) {
			if (a == null)
				return 0;
			int res = 0;

			res += a.Name.GetHashCode();
			res += methodReferenceSignatureHashCode(a);

			return res;
		}

		public static bool comparePropertyReference(PropertyReference a, PropertyReference b) {
			if (ReferenceEquals(a, b))
				return true;
			if (a == null || b == null)
				return false;
			if ((a.Parameters == null && b.Parameters != null) || (a.Parameters != null && b.Parameters == null))
				return false;
			if (a.Parameters != null) {
				if (a.Parameters.Count != b.Parameters.Count)
					return false;
				for (int i = 0; i < a.Parameters.Count; i++) {
					if (!compareTypes(a.Parameters[i].ParameterType, b.Parameters[i].ParameterType))
						return false;
				}
			}
			return a.Name == b.Name &&
				compareTypes(a.PropertyType, b.PropertyType);
		}

		public static int propertyReferenceHashCode(PropertyReference a) {
			if (a == null)
				return 0;
			int res = 0;

			if (a.Parameters != null) {
				res += a.Parameters.Count.GetHashCode();
				foreach (var param in a.Parameters)
					res += typeHashCode(param.ParameterType);
			}

			res += a.Name.GetHashCode();
			res += typeHashCode(a.PropertyType);
			return res;
		}

		public static bool compareTypes(TypeReference a, TypeReference b) {
			if (ReferenceEquals(a, b))
				return true;
			if (a == null || b == null)
				return false;

			var atype = a.GetType();
			var btype = b.GetType();
			if (atype != btype) {
				if ((atype == typeof(TypeReference) || atype == typeof(TypeDefinition)) &&
					(btype == typeof(TypeReference) || btype == typeof(TypeDefinition)))
					return compareTypeReferences(a, b);
				return false;
			}

			var type = getMemberReferenceType(a);
			switch (type) {
			case CecilType.ArrayType:
				return compareArrayTypes((ArrayType)a, (ArrayType)b);
			case CecilType.ByReferenceType:
				return compareByReferenceTypes((ByReferenceType)a, (ByReferenceType)b);
			case CecilType.FunctionPointerType:
				return compareFunctionPointerTypes((FunctionPointerType)a, (FunctionPointerType)b);
			case CecilType.GenericInstanceType:
				return compareGenericInstanceTypes((GenericInstanceType)a, (GenericInstanceType)b);
			case CecilType.GenericParameter:
				return compareGenericParameters((GenericParameter)a, (GenericParameter)b);
			case CecilType.OptionalModifierType:
				return compareOptionalModifierTypes((OptionalModifierType)a, (OptionalModifierType)b);
			case CecilType.PinnedType:
				return comparePinnedTypes((PinnedType)a, (PinnedType)b);
			case CecilType.PointerType:
				return comparePointerTypes((PointerType)a, (PointerType)b);
			case CecilType.RequiredModifierType:
				return compareRequiredModifierTypes((RequiredModifierType)a, (RequiredModifierType)b);
			case CecilType.SentinelType:
				return compareSentinelTypes((SentinelType)a, (SentinelType)b);
			case CecilType.TypeDefinition:
				return compareTypeDefinitions((TypeDefinition)a, (TypeDefinition)b);
			case CecilType.TypeReference:
				return compareTypeReferences((TypeReference)a, (TypeReference)b);
			default:
				throw new ApplicationException(string.Format("Unknown cecil type {0}", type));
			}
		}

		public static int typeHashCode(TypeReference a) {
			if (a == null)
				return 0;

			var type = getMemberReferenceType(a);
			switch (type) {
			case CecilType.ArrayType:
				return arrayTypeHashCode((ArrayType)a);
			case CecilType.ByReferenceType:
				return byReferenceTypeHashCode((ByReferenceType)a);
			case CecilType.FunctionPointerType:
				return functionPointerTypeHashCode((FunctionPointerType)a);
			case CecilType.GenericInstanceType:
				return genericInstanceTypeHashCode((GenericInstanceType)a);
			case CecilType.GenericParameter:
				return genericParameterHashCode((GenericParameter)a);
			case CecilType.OptionalModifierType:
				return optionalModifierTypeHashCode((OptionalModifierType)a);
			case CecilType.PinnedType:
				return pinnedTypeHashCode((PinnedType)a);
			case CecilType.PointerType:
				return pointerTypeHashCode((PointerType)a);
			case CecilType.RequiredModifierType:
				return requiredModifierTypeHashCode((RequiredModifierType)a);
			case CecilType.SentinelType:
				return sentinelTypeHashCode((SentinelType)a);
			case CecilType.TypeDefinition:
				return typeDefinitionHashCode((TypeDefinition)a);
			case CecilType.TypeReference:
				return typeReferenceHashCode((TypeReference)a);
			default:
				throw new ApplicationException(string.Format("Unknown cecil type {0}", type));
			}
		}

		static bool compareArrayTypes(ArrayType a, ArrayType b) {
			if (a.Rank != b.Rank || a.IsVector != b.IsVector)
				return false;
			if (!a.IsVector) {
				for (int i = 0; i < a.Dimensions.Count; i++) {
					if (!compareArrayDimensions(a.Dimensions[i], b.Dimensions[i]))
						return false;
				}
			}
			return compareTypeSpecifications(a, b);
		}

		static int arrayTypeHashCode(ArrayType a) {
			if (a == null)
				return 0;
			int res = 0;

			res += a.Rank.GetHashCode();
			res += a.IsVector.GetHashCode();
			if (!a.IsVector) {
				foreach (var dim in a.Dimensions)
					res += arrayDimensionHashCode(dim);
			}
			res += typeSpecificationHashCode(a);

			return res;
		}

		static bool compareArrayDimensions(ArrayDimension a, ArrayDimension b) {
			return a.LowerBound == b.LowerBound && a.UpperBound == b.UpperBound;
		}

		static int arrayDimensionHashCode(ArrayDimension a) {
			return a.LowerBound.GetHashCode() + a.UpperBound.GetHashCode();
		}

		static bool compareGenericInstanceTypes(GenericInstanceType a, GenericInstanceType b) {
			if (a.HasGenericArguments != b.HasGenericArguments)
				return false;
			if (a.HasGenericArguments) {
				if (a.GenericArguments.Count != b.GenericArguments.Count)
					return false;
				for (int i = 0; i < a.GenericArguments.Count; i++) {
					if (!compareTypes(a.GenericArguments[i], b.GenericArguments[i]))
						return false;
				}
			}
			return compareTypeSpecifications(a, b);
		}

		static int genericInstanceTypeHashCode(GenericInstanceType a) {
			if (a == null)
				return 0;
			int res = 0;

			res += a.HasGenericArguments.GetHashCode();
			if (a.HasGenericArguments) {
				res += a.GenericArguments.Count.GetHashCode();
				foreach (var arg in a.GenericArguments)
					res += typeHashCode(arg);
			}
			res += typeSpecificationHashCode(a);

			return res;
		}

		static bool comparePointerTypes(PointerType a, PointerType b) {
			return compareTypeSpecifications(a, b);
		}

		static int pointerTypeHashCode(PointerType a) {
			if (a == null)
				return 0;
			return typeSpecificationHashCode(a);
		}

		static bool compareByReferenceTypes(ByReferenceType a, ByReferenceType b) {
			return compareTypeSpecifications(a, b);
		}

		static int byReferenceTypeHashCode(ByReferenceType a) {
			if (a == null)
				return 0;
			return typeSpecificationHashCode(a);
		}

		static bool comparePinnedTypes(PinnedType a, PinnedType b) {
			return compareTypeSpecifications(a, b);
		}

		static int pinnedTypeHashCode(PinnedType a) {
			if (a == null)
				return 0;
			return typeSpecificationHashCode(a);
		}

		static bool compareFunctionPointerTypes(FunctionPointerType a, FunctionPointerType b) {
			return compareMethodReference(a.function, b.function) &&
				compareTypeSpecifications(a, b);
		}

		static int functionPointerTypeHashCode(FunctionPointerType a) {
			if (a == null)
				return 0;
			return methodReferenceHashCode(a.function) + typeSpecificationHashCode(a);
		}

		static bool compareOptionalModifierTypes(OptionalModifierType a, OptionalModifierType b) {
			return compareTypes(a.ModifierType, b.ModifierType) &&
				compareTypeSpecifications(a, b);
		}

		static int optionalModifierTypeHashCode(OptionalModifierType a) {
			if (a == null)
				return 0;
			return typeHashCode(a.ModifierType) + typeSpecificationHashCode(a);
		}

		static bool compareRequiredModifierTypes(RequiredModifierType a, RequiredModifierType b) {
			return compareTypes(a.ModifierType, b.ModifierType) &&
				compareTypeSpecifications(a, b);
		}

		static int requiredModifierTypeHashCode(RequiredModifierType a) {
			if (a == null)
				return 0;
			return typeHashCode(a.ModifierType) + typeSpecificationHashCode(a);
		}

		static bool compareSentinelTypes(SentinelType a, SentinelType b) {
			return compareTypeSpecifications(a, b);
		}

		static int sentinelTypeHashCode(SentinelType a) {
			if (a == null)
				return 0;
			return typeSpecificationHashCode(a);
		}

		static bool compareTypeSpecifications(TypeSpecification a, TypeSpecification b) {
			// It overrides everything of importance in TypeReference. The only thing to check
			// is the ElementType.
			return compareTypes(a.ElementType, b.ElementType);
		}

		static int typeSpecificationHashCode(TypeSpecification a) {
			if (a == null)
				return 0;
			return typeHashCode(a.ElementType);
		}

		static bool compareTypeDefinitions(TypeDefinition a, TypeDefinition b) {
			return compareTypeReferences(a, b);
		}

		static int typeDefinitionHashCode(TypeDefinition a) {
			if (a == null)
				return 0;
			return typeReferenceHashCode(a);
		}

		static bool compareGenericParameters(GenericParameter a, GenericParameter b) {
			return a.Position == b.Position &&
				compareGenericParameterProvider(a.Owner, b.Owner);
		}

		static int genericParameterHashCode(GenericParameter a) {
			if (a == null)
				return 0;
			return a.Position.GetHashCode() + genericParameterProviderHashCode(a.Owner);
		}

		// a and b must be either exactly a TypeReference or exactly a TypeDefinition
		static bool compareTypeReferences(TypeReference a, TypeReference b) {
			if ((a.GetType() != typeof(TypeReference) && a.GetType() != typeof(TypeDefinition)) ||
				(b.GetType() != typeof(TypeReference) && b.GetType() != typeof(TypeDefinition)))
				throw new ApplicationException("arg must be exactly of type TypeReference or TypeDefinition");

			if (ReferenceEquals(a, b))
				return true;

			return a.Name == b.Name &&
				a.Namespace == b.Namespace &&
				compareTypes(a.DeclaringType, b.DeclaringType) &&
				compareScope(a.Scope, b.Scope);
		}

		// a must be exactly a TypeReference or a TypeDefinition
		static int typeReferenceHashCode(TypeReference a) {
			if (a == null)
				return 0;

			if (a.GetType() != typeof(TypeReference) && a.GetType() != typeof(TypeDefinition))
				throw new ApplicationException("arg must be exactly of type TypeReference or TypeDefinition");

			int res = 0;

			res += a.Name.GetHashCode();
			res += a.Namespace.GetHashCode();
			res += typeHashCode(a.DeclaringType);
			res += scopeHashCode(a.Scope);

			return res;
		}

		public static bool compareTypeReferenceSameVersion(TypeReference a, TypeReference b) {
			if (ReferenceEquals(a, b))
				return true;
			if (a == null || b == null)
				return false;

			if ((a.GetType() != typeof(TypeReference) && a.GetType() != typeof(TypeDefinition)) ||
				(b.GetType() != typeof(TypeReference) && b.GetType() != typeof(TypeDefinition)))
				throw new ApplicationException("arg must be exactly of type TypeReference or TypeDefinition");

			return a.Name == b.Name &&
				a.Namespace == b.Namespace &&
				compareTypeReferenceSameVersion(a.DeclaringType, b.DeclaringType) &&
				compareScopeSameVersion(a.Scope, b.Scope);
		}

		public static int typeReferenceHashCodeSameVersion(TypeReference a) {
			if (a == null)
				return 0;

			if (a.GetType() != typeof(TypeReference) && a.GetType() != typeof(TypeDefinition))
				throw new ApplicationException("arg must be exactly of type TypeReference or TypeDefinition");

			int res = 0;

			res += a.Name.GetHashCode();
			res += a.Namespace.GetHashCode();
			res += typeReferenceHashCodeSameVersion(a.DeclaringType);
			res += scopeHashCodeSameVersion(a.Scope);

			return res;
		}

		static bool compareGenericParameterProvider(IGenericParameterProvider a, IGenericParameterProvider b) {
			if (ReferenceEquals(a, b))
				return true;
			if (a == null || b == null)
				return false;
			if (a is TypeReference && b is TypeReference)
				return compareTypes((TypeReference)a, (TypeReference)b);
			if (a is MethodReference && b is MethodReference)
				return true;
			return false;
		}

		static int genericParameterProviderHashCode(IGenericParameterProvider a) {
			if (a == null)
				return 0;
			if (a is TypeReference)
				return typeHashCode((TypeReference)a);
			if (a is MethodReference)
				return 0;
			throw new ApplicationException(string.Format("Unknown IGenericParameterProvider type {0}", a.GetType()));
		}
	}
}
