/*
    Copyright (C) 2011 de4dot@gmail.com

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

using System.Collections.Generic;
using Mono.Cecil;
using de4dot.blocks;

namespace de4dot.renamer.asmmodules {
	interface RefDict<TRef, TMRef> where TRef : Ref where TMRef : MemberReference {
		IEnumerable<TRef> getAll();
		IEnumerable<TRef> getSorted();
		TRef find(TMRef tmref);
		void add(TRef tref);
		void onTypesRenamed();
	}

	class TypeDefDict : RefDict<TypeDef, TypeReference> {
		Dictionary<ScopeAndTokenKey, TypeDef> tokenToTypeDef = new Dictionary<ScopeAndTokenKey, TypeDef>();
		Dictionary<TypeReferenceKey, TypeDef> typeRefToDef = new Dictionary<TypeReferenceKey, TypeDef>();

		public IEnumerable<TypeDef> getAll() {
			return tokenToTypeDef.Values;
		}

		public IEnumerable<TypeDef> getSorted() {
			var list = new List<TypeDef>(getAll());
			list.Sort((a, b) => {
				if (a.Index < b.Index) return -1;
				if (a.Index > b.Index) return 1;
				return 0;
			});
			return list;
		}

		public TypeDef find(TypeReference typeReference) {
			TypeDef typeDef;
			if (tokenToTypeDef.TryGetValue(new ScopeAndTokenKey(typeReference), out typeDef))
				return typeDef;

			typeRefToDef.TryGetValue(new TypeReferenceKey(typeReference), out typeDef);
			return typeDef;
		}

		public void add(TypeDef typeDef) {
			tokenToTypeDef[new ScopeAndTokenKey(typeDef.TypeDefinition)] = typeDef;
			typeRefToDef[new TypeReferenceKey(typeDef.TypeDefinition)] = typeDef;
		}

		public void onTypesRenamed() {
			var all = new List<TypeDef>(typeRefToDef.Values);
			typeRefToDef.Clear();
			foreach (var typeDef in all)
				typeRefToDef[new TypeReferenceKey(typeDef.TypeDefinition)] = typeDef;
		}
	}

	class FieldDefDict : RefDict<FieldDef, FieldReference> {
		Dictionary<ScopeAndTokenKey, FieldDef> tokenToFieldDef = new Dictionary<ScopeAndTokenKey, FieldDef>();
		Dictionary<FieldReferenceKey, FieldDef> fieldRefToDef = new Dictionary<FieldReferenceKey, FieldDef>();

		public IEnumerable<FieldDef> getAll() {
			return tokenToFieldDef.Values;
		}

		public IEnumerable<FieldDef> getSorted() {
			var list = new List<FieldDef>(getAll());
			list.Sort((a, b) => {
				if (a.Index < b.Index) return -1;
				if (a.Index > b.Index) return 1;
				return 0;
			});
			return list;
		}

		public FieldDef find(FieldReference fieldReference) {
			FieldDef fieldDef;
			if (tokenToFieldDef.TryGetValue(new ScopeAndTokenKey(fieldReference), out fieldDef))
				return fieldDef;

			fieldRefToDef.TryGetValue(new FieldReferenceKey(fieldReference), out fieldDef);
			return fieldDef;
		}

		public void add(FieldDef fieldDef) {
			tokenToFieldDef[new ScopeAndTokenKey(fieldDef.FieldDefinition)] = fieldDef;
			fieldRefToDef[new FieldReferenceKey(fieldDef.FieldDefinition)] = fieldDef;
		}

		public void onTypesRenamed() {
			var all = new List<FieldDef>(fieldRefToDef.Values);
			fieldRefToDef.Clear();
			foreach (var fieldDef in all)
				fieldRefToDef[new FieldReferenceKey(fieldDef.FieldDefinition)] = fieldDef;
		}
	}

	class MethodDefDict : RefDict<MethodDef, MethodReference> {
		Dictionary<ScopeAndTokenKey, MethodDef> tokenToMethodDef = new Dictionary<ScopeAndTokenKey, MethodDef>();
		Dictionary<MethodReferenceKey, MethodDef> methodRefToDef = new Dictionary<MethodReferenceKey, MethodDef>();

		public IEnumerable<MethodDef> getAll() {
			return tokenToMethodDef.Values;
		}

		public IEnumerable<MethodDef> getSorted() {
			var list = new List<MethodDef>(getAll());
			list.Sort((a, b) => {
				if (a.Index < b.Index) return -1;
				if (a.Index > b.Index) return 1;
				return 0;
			});
			return list;
		}

		public MethodDef find(MethodReference methodReference) {
			MethodDef methodDef;
			if (tokenToMethodDef.TryGetValue(new ScopeAndTokenKey(methodReference), out methodDef))
				return methodDef;

			methodRefToDef.TryGetValue(new MethodReferenceKey(methodReference), out methodDef);
			return methodDef;
		}

		public void add(MethodDef methodDef) {
			tokenToMethodDef[new ScopeAndTokenKey(methodDef.MethodDefinition)] = methodDef;
			methodRefToDef[new MethodReferenceKey(methodDef.MethodDefinition)] = methodDef;
		}

		public void onTypesRenamed() {
			var all = new List<MethodDef>(methodRefToDef.Values);
			methodRefToDef.Clear();
			foreach (var methodDef in all)
				methodRefToDef[new MethodReferenceKey(methodDef.MethodDefinition)] = methodDef;
		}
	}

	class PropertyDefDict : RefDict<PropertyDef, PropertyReference> {
		Dictionary<ScopeAndTokenKey, PropertyDef> tokenToPropDef = new Dictionary<ScopeAndTokenKey, PropertyDef>();

		public IEnumerable<PropertyDef> getAll() {
			return tokenToPropDef.Values;
		}

		public IEnumerable<PropertyDef> getSorted() {
			var list = new List<PropertyDef>(getAll());
			list.Sort((a, b) => {
				if (a.Index < b.Index) return -1;
				if (a.Index > b.Index) return 1;
				return 0;
			});
			return list;
		}

		public PropertyDef find(PropertyReference propertyReference) {
			PropertyDef propDef;
			tokenToPropDef.TryGetValue(new ScopeAndTokenKey(propertyReference), out propDef);
			return propDef;
		}

		public void add(PropertyDef propDef) {
			tokenToPropDef[new ScopeAndTokenKey(propDef.PropertyDefinition)] = propDef;
		}

		public void onTypesRenamed() {
		}
	}

	class EventDefDict : RefDict<EventDef, EventReference> {
		Dictionary<ScopeAndTokenKey, EventDef> tokenToEventDef = new Dictionary<ScopeAndTokenKey, EventDef>();

		public IEnumerable<EventDef> getAll() {
			return tokenToEventDef.Values;
		}

		public IEnumerable<EventDef> getSorted() {
			var list = new List<EventDef>(getAll());
			list.Sort((a, b) => {
				if (a.Index < b.Index) return -1;
				if (a.Index > b.Index) return 1;
				return 0;
			});
			return list;
		}

		public EventDef find(EventReference eventReference) {
			EventDef eventDef;
			tokenToEventDef.TryGetValue(new ScopeAndTokenKey(eventReference), out eventDef);
			return eventDef;
		}

		public void add(EventDef eventDef) {
			tokenToEventDef[new ScopeAndTokenKey(eventDef.EventDefinition)] = eventDef;
		}

		public void onTypesRenamed() {
		}
	}
}
