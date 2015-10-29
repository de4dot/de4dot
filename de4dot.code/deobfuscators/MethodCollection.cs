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
using dnlib.DotNet;
using de4dot.blocks;

namespace de4dot.code.deobfuscators {
	public class MethodCollection {
		TypeDefDict<bool> types = new TypeDefDict<bool>();
		MethodDefAndDeclaringTypeDict<bool> methods = new MethodDefAndDeclaringTypeDict<bool>();

		public bool Exists(IMethod method) {
			if (method == null)
				return false;
			if (method.DeclaringType != null && types.Find(method.DeclaringType))
				return true;
			return methods.Find(method);
		}

		public void Add(MethodDef method) {
			methods.Add(method, true);
		}

		public void Add(IEnumerable<MethodDef> methods) {
			foreach (var method in methods)
				Add(method);
		}

		public void Add(TypeDef type) {
			types.Add(type, true);
		}

		public void AddAndNested(TypeDef type) {
			Add(type);
			foreach (var t in type.GetTypes())
				Add(t);
		}

		public void AddAndNested(IList<TypeDef> types) {
			foreach (var type in AllTypesHelper.Types(types))
				Add(type);
		}
	}
}
