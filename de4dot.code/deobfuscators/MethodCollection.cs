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
using dnlib.DotNet;
using de4dot.blocks;

namespace de4dot.code.deobfuscators {
	class MethodCollection {
		TypeDefDict<bool> types = new TypeDefDict<bool>();
		MethodDefAndDeclaringTypeDict<bool> methods = new MethodDefAndDeclaringTypeDict<bool>();

		public bool exists(IMethod method) {
			if (method == null)
				return false;
			if (method.DeclaringType != null && types.find(method.DeclaringType))
				return true;
			return methods.find(method);
		}

		public void add(MethodDef method) {
			methods.add(method, true);
		}

		public void add(IEnumerable<MethodDef> methods) {
			foreach (var method in methods)
				add(method);
		}

		public void add(TypeDef type) {
			types.add(type, true);
		}

		public void addAndNested(TypeDef type) {
			add(type);
			foreach (var t in type.GetTypes())
				add(t);
		}

		public void addAndNested(IList<TypeDef> types) {
			foreach (var type in AllTypesHelper.Types(types))
				add(type);
		}
	}
}
