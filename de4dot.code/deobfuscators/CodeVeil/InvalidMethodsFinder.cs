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

namespace de4dot.code.deobfuscators.CodeVeil {
	class InvalidMethodsFinder {
		public static List<MethodDefinition> findAll(ModuleDefinition module) {
			var list = new List<MethodDefinition>();
			foreach (var type in module.GetTypes()) {
				foreach (var method in type.Methods) {
					if (isInvalidMethod(method))
						list.Add(method);
				}
			}
			return list;
		}

		public static bool isInvalidMethod(MethodDefinition method) {
			if (method == null)
				return false;
			if (method.IsStatic)
				return false;
			if (method.Parameters.Count != 0)
				return false;
			var retType = method.MethodReturnType.ReturnType as GenericParameter;
			if (retType == null)
				return false;

			return retType.Owner == null;
		}
	}
}
