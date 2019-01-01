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

using dnlib.DotNet;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.SmartAssembly {
	class SimpleZipInfo {

		public static bool IsSimpleZipDecryptMethod_QuickCheck(ModuleDefMD module, IMethod method, out MethodDef simpleZipTypeMethod) {
			simpleZipTypeMethod = null;

			if (!DotNetUtils.IsMethod(method, "System.Byte[]", "(System.Byte[])"))
				return false;

			var methodDef = DotNetUtils.GetMethod(DotNetUtils.GetType(module, method.DeclaringType), method);
			if (methodDef == null)
				return false;

			simpleZipTypeMethod = methodDef;
			return true;
		}
	}
}
