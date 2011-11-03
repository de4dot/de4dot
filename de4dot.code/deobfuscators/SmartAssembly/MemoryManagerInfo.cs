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

namespace de4dot.deobfuscators.SmartAssembly {
	class MemoryManagerInfo {
		ModuleDefinition module;
		TypeDefinition memoryManagerType;
		MethodDefinition attachAppMethod;

		public bool Detected {
			get { return memoryManagerType != null; }
		}

		public TypeDefinition MemoryManagerType {
			get { return memoryManagerType; }
		}

		public MethodDefinition CctorInitMethod {
			get { return attachAppMethod; }
		}

		public MemoryManagerInfo(ModuleDefinition module) {
			this.module = module;
			find();
		}

		bool find() {
			var cctor = DotNetUtils.getMethod(DotNetUtils.getModuleType(module), ".cctor");
			if (cctor == null)
				return false;

			foreach (var tuple in DotNetUtils.getCalledMethods(module, cctor)) {
				var method = tuple.Item2;
				if (method.Name == ".cctor" || method.Name == ".ctor")
					continue;
				if (!method.IsStatic || !DotNetUtils.isMethod(method, "System.Void", "()"))
					continue;
				if (checkMemoryManagerType(tuple.Item1, method)) {
					memoryManagerType = tuple.Item1;
					attachAppMethod = method;
					return true;
				}
			}

			return false;
		}

		bool checkMemoryManagerType(TypeDefinition type, MethodDefinition method) {
			// Only two fields: itself and a long
			int fields = 0;
			foreach (var field in type.Fields) {
				if (MemberReferenceHelper.compareTypes(field.FieldType, type) ||
					field.FieldType.FullName == "System.Int64") {
					fields++;
					continue;
				}
				if (DotNetUtils.derivesFromDelegate(DotNetUtils.getType(module, field.FieldType)))
					continue;

				return false;
			}
			if (fields != 2)
				return false;

			if (DotNetUtils.getPInvokeMethod(type, "kernel32", "SetProcessWorkingSetSize") == null)
				return false;

			return true;
		}
	}
}
