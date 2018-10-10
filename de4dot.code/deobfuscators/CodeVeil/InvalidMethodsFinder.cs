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

namespace de4dot.code.deobfuscators.CodeVeil {
	class InvalidMethodsFinder {
		public static List<MethodDef> FindAll(ModuleDefMD module) {
			var list = new List<MethodDef>();
			foreach (var type in module.GetTypes()) {
				foreach (var method in type.Methods) {
					if (IsInvalidMethod(method))
						list.Add(method);
				}
			}
			return list;
		}

		public static bool IsInvalidMethod(MethodDef method) {
			if (method == null || method.IsStatic)
				return false;
			var sig = method.MethodSig;
			if (sig == null || sig.Params.Count != 0)
				return false;
			if (sig.RetType == null)
				return true;
			var retType = sig.RetType as GenericSig;
			if (retType == null)
				return false;

			if (retType.IsMethodVar)
				return retType.Number >= sig.GenParamCount;
			var dt = method.DeclaringType;
			return dt == null || retType.Number >= dt.GenericParameters.Count;
		}
	}
}
