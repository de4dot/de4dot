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

using System.Collections.Generic;
using Mono.Cecil;
using de4dot.blocks;

namespace de4dot.code.renamer.asmmodules {
	static class DictHelper {
		public static IEnumerable<T> getSorted<T>(IEnumerable<T> values) where T : Ref {
			var list = new List<T>(values);
			list.Sort((a, b) => Utils.compareInt32(a.Index, b.Index));
			return list;
		}
	}

	class TypeDefDict : TypeDefinitionDict<TypeDef> {
		public IEnumerable<TypeDef> getSorted() {
			return DictHelper.getSorted(getValues());
		}

		public void add(TypeDef typeDef) {
			add(typeDef.TypeDefinition, typeDef);
		}
	}

	class FieldDefDict : FieldDefinitionDict<FieldDef> {
		public IEnumerable<FieldDef> getSorted() {
			return DictHelper.getSorted(getValues());
		}

		public void add(FieldDef fieldDef) {
			add(fieldDef.FieldDefinition, fieldDef);
		}
	}

	class MethodDefDict : MethodDefinitionDict<MethodDef> {
		public IEnumerable<MethodDef> getSorted() {
			return DictHelper.getSorted(getValues());
		}

		public void add(MethodDef methodDef) {
			add(methodDef.MethodDefinition, methodDef);
		}
	}

	class PropertyDefDict : PropertyDefinitionDict<PropertyDef> {
		public IEnumerable<PropertyDef> getSorted() {
			return DictHelper.getSorted(getValues());
		}

		public void add(PropertyDef propDef) {
			add(propDef.PropertyDefinition, propDef);
		}
	}

	class EventDefDict : EventDefinitionDict<EventDef> {
		public IEnumerable<EventDef> getSorted() {
			return DictHelper.getSorted(getValues());
		}

		public void add(EventDef eventDef) {
			add(eventDef.EventDefinition, eventDef);
		}
	}
}
