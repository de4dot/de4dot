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

using Mono.Cecil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.SmartAssembly {
	class SimpleZipInfo {

		public static bool isSimpleZipDecryptMethod_QuickCheck(ModuleDefinition module, MethodReference method, out TypeDefinition simpleZipType) {
			simpleZipType = null;

			if (!DotNetUtils.isMethod(method, "System.Byte[]", "(System.Byte[])"))
				return false;

			var type = DotNetUtils.getType(module, method.DeclaringType);
			var methodDef = DotNetUtils.getMethod(type, method);
			if (methodDef == null)
				return false;

			simpleZipType = type;
			return true;
		}
	}
}
