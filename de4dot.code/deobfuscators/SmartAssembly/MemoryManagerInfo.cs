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
	class MemoryManagerInfo {
		ModuleDefMD module;
		TypeDef memoryManagerType;
		MethodDef attachAppMethod;

		public bool Detected => memoryManagerType != null;
		public TypeDef Type => memoryManagerType;
		public MethodDef CctorInitMethod => attachAppMethod;
		public MemoryManagerInfo(ModuleDefMD module) => this.module = module;

		public bool Find() {
			if (CheckCalledMethods(DotNetUtils.GetModuleTypeCctor(module)))
				return true;
			if (CheckCalledMethods(module.EntryPoint))
				return true;
			return false;
		}

		bool CheckCalledMethods(MethodDef checkMethod) {
			if (checkMethod == null)
				return false;
			foreach (var method in DotNetUtils.GetCalledMethods(module, checkMethod)) {
				if (method.Name == ".cctor" || method.Name == ".ctor")
					continue;
				if (!method.IsStatic || !DotNetUtils.IsMethod(method, "System.Void", "()"))
					continue;
				if (CheckMemoryManagerType(method.DeclaringType, method)) {
					memoryManagerType = method.DeclaringType;
					attachAppMethod = method;
					return true;
				}
			}

			return false;
		}

		bool CheckMemoryManagerType(TypeDef type, MethodDef method) {
			// Only two fields: itself and a long
			int fields = 0;
			foreach (var field in type.Fields) {
				if (new SigComparer().Equals(field.FieldType, type) ||
					field.FieldType.FullName == "System.Int64") {
					fields++;
					continue;
				}
				if (DotNetUtils.DerivesFromDelegate(DotNetUtils.GetType(module, field.FieldType)))
					continue;

				return false;
			}
			if (fields != 2)
				return false;

			if (DotNetUtils.GetPInvokeMethod(type, "kernel32", "SetProcessWorkingSetSize") == null)
				return false;

			return true;
		}
	}
}
