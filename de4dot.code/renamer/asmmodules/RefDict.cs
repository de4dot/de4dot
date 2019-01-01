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

using System.Collections.Generic;
using de4dot.blocks;

namespace de4dot.code.renamer.asmmodules {
	public static class DictHelper {
		public static IEnumerable<T> GetSorted<T>(IEnumerable<T> values) where T : Ref {
			var list = new List<T>(values);
			list.Sort((a, b) => a.Index.CompareTo(b.Index));
			return list;
		}
	}

	public class TypeDefDict : TypeDefDict<MTypeDef> {
		public IEnumerable<MTypeDef> GetSorted() => DictHelper.GetSorted(GetValues());
		public void Add(MTypeDef typeDef) => Add(typeDef.TypeDef, typeDef);
	}

	public class FieldDefDict : FieldDefDict<MFieldDef> {
		public IEnumerable<MFieldDef> GetSorted() => DictHelper.GetSorted(GetValues());
		public void Add(MFieldDef fieldDef) => Add(fieldDef.FieldDef, fieldDef);
	}

	public class MethodDefDict : MethodDefDict<MMethodDef> {
		public IEnumerable<MMethodDef> GetSorted() => DictHelper.GetSorted(GetValues());
		public void Add(MMethodDef methodDef) => Add(methodDef.MethodDef, methodDef);
	}

	public class PropertyDefDict : PropertyDefDict<MPropertyDef> {
		public IEnumerable<MPropertyDef> GetSorted() => DictHelper.GetSorted(GetValues());
		public void Add(MPropertyDef propDef) => Add(propDef.PropertyDef, propDef);
	}

	public class EventDefDict : EventDefDict<MEventDef> {
		public IEnumerable<MEventDef> GetSorted() => DictHelper.GetSorted(GetValues());
		public void Add(MEventDef eventDef) => Add(eventDef.EventDef, eventDef);
	}
}
