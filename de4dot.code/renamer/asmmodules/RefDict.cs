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

namespace de4dot.code.renamer.asmmodules {
	interface RefDict<TRef, TMRef> where TRef : Ref where TMRef : MemberReference {
		int Count { get; }
		IEnumerable<TRef> getAll();
		IEnumerable<TRef> getSorted();
		TRef find(TMRef tmref);
		void add(TRef tref);
		void onTypesRenamed();
	}

	class TypeDefDict : RefDict<TypeDef, TypeReference> {
		TypeDefinitionDict<TypeDef> typeToDef = new TypeDefinitionDict<TypeDef>();

		public int Count {
			get { return typeToDef.Count; }
		}

		public IEnumerable<TypeDef> getAll() {
			return typeToDef.getAll();
		}

		public IEnumerable<TypeDef> getSorted() {
			var list = new List<TypeDef>(getAll());
			list.Sort((a, b) => Utils.compareInt32(a.Index, b.Index));
			return list;
		}

		public TypeDef find(TypeReference typeReference) {
			return typeToDef.find(typeReference);
		}

		public void add(TypeDef typeDef) {
			typeToDef.add(typeDef.TypeDefinition, typeDef);
		}

		public void onTypesRenamed() {
			typeToDef.onTypesRenamed();
		}
	}

	class FieldDefDict : RefDict<FieldDef, FieldReference> {
		FieldDefinitionDict<FieldDef> fieldToDef = new FieldDefinitionDict<FieldDef>();

		public int Count {
			get { return fieldToDef.Count; }
		}

		public IEnumerable<FieldDef> getAll() {
			return fieldToDef.getAll();
		}

		public IEnumerable<FieldDef> getSorted() {
			var list = new List<FieldDef>(getAll());
			list.Sort((a, b) => Utils.compareInt32(a.Index, b.Index));
			return list;
		}

		public FieldDef find(FieldReference fieldReference) {
			return fieldToDef.find(fieldReference);
		}

		public void add(FieldDef fieldDef) {
			fieldToDef.add(fieldDef.FieldDefinition, fieldDef);
		}

		public void onTypesRenamed() {
			fieldToDef.onTypesRenamed();
		}
	}

	class MethodDefDict : RefDict<MethodDef, MethodReference> {
		MethodDefinitionDict<MethodDef> methodToDef = new MethodDefinitionDict<MethodDef>();

		public int Count {
			get { return methodToDef.Count; }
		}

		public IEnumerable<MethodDef> getAll() {
			return methodToDef.getAll();
		}

		public IEnumerable<MethodDef> getSorted() {
			var list = new List<MethodDef>(getAll());
			list.Sort((a, b) => Utils.compareInt32(a.Index, b.Index));
			return list;
		}

		public MethodDef find(MethodReference methodReference) {
			return methodToDef.find(methodReference);
		}

		public void add(MethodDef methodDef) {
			methodToDef.add(methodDef.MethodDefinition, methodDef);
		}

		public void onTypesRenamed() {
			methodToDef.onTypesRenamed();
		}
	}

	class PropertyDefDict : RefDict<PropertyDef, PropertyReference> {
		PropertyDefinitionDict<PropertyDef> propToDef = new PropertyDefinitionDict<PropertyDef>();

		public int Count {
			get { return propToDef.Count; }
		}

		public IEnumerable<PropertyDef> getAll() {
			return propToDef.getAll();
		}

		public IEnumerable<PropertyDef> getSorted() {
			var list = new List<PropertyDef>(getAll());
			list.Sort((a, b) => Utils.compareInt32(a.Index, b.Index));
			return list;
		}

		public PropertyDef find(PropertyReference propertyReference) {
			return propToDef.find(propertyReference);
		}

		public void add(PropertyDef propDef) {
			propToDef.add(propDef.PropertyDefinition, propDef);
		}

		public void onTypesRenamed() {
			propToDef.onTypesRenamed();
		}
	}

	class EventDefDict : RefDict<EventDef, EventReference> {
		EventDefinitionDict<EventDef> eventToDef = new EventDefinitionDict<EventDef>();

		public int Count {
			get { return eventToDef.Count; }
		}

		public IEnumerable<EventDef> getAll() {
			return eventToDef.getAll();
		}

		public IEnumerable<EventDef> getSorted() {
			var list = new List<EventDef>(getAll());
			list.Sort((a, b) => Utils.compareInt32(a.Index, b.Index));
			return list;
		}

		public EventDef find(EventReference eventReference) {
			return eventToDef.find(eventReference);
		}

		public void add(EventDef eventDef) {
			eventToDef.add(eventDef.EventDefinition, eventDef);
		}

		public void onTypesRenamed() {
			eventToDef.onTypesRenamed();
		}
	}
}
