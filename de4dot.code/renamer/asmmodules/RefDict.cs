/*
    Copyright (C) 2011-2013 de4dot@gmail.com

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
using de4dot.blocks;

namespace de4dot.code.renamer.asmmodules {
	static class DictHelper {
		public static IEnumerable<T> getSorted<T>(IEnumerable<T> values) where T : Ref {
			var list = new List<T>(values);
			list.Sort((a, b) => a.Index.CompareTo(b.Index));
			return list;
		}
	}

	class TypeDefDict : TypeDefDict<MTypeDef> {
		public IEnumerable<MTypeDef> getSorted() {
			return DictHelper.getSorted(getValues());
		}

		public void add(MTypeDef typeDef) {
			add(typeDef.TypeDef, typeDef);
		}
	}

	class FieldDefDict : FieldDefDict<MFieldDef> {
		public IEnumerable<MFieldDef> getSorted() {
			return DictHelper.getSorted(getValues());
		}

		public void add(MFieldDef fieldDef) {
			add(fieldDef.FieldDef, fieldDef);
		}
	}

	class MethodDefDict : MethodDefDict<MMethodDef> {
		public IEnumerable<MMethodDef> getSorted() {
			return DictHelper.getSorted(getValues());
		}

		public void add(MMethodDef methodDef) {
			add(methodDef.MethodDef, methodDef);
		}
	}

	class PropertyDefDict : PropertyDefDict<MPropertyDef> {
		public IEnumerable<MPropertyDef> getSorted() {
			return DictHelper.getSorted(getValues());
		}

		public void add(MPropertyDef propDef) {
			add(propDef.PropertyDef, propDef);
		}
	}

	class EventDefDict : EventDefDict<MEventDef> {
		public IEnumerable<MEventDef> getSorted() {
			return DictHelper.getSorted(getValues());
		}

		public void add(MEventDef eventDef) {
			add(eventDef.EventDef, eventDef);
		}
	}
}
