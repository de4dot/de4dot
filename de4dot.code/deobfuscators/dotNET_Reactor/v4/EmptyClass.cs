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

namespace de4dot.code.deobfuscators.dotNET_Reactor.v4 {
	// Detect some empty class that is called from most .ctor's
	class EmptyClass {
		ModuleDefMD module;
		MethodDef emptyMethod;

		public MethodDef Method {
			get { return emptyMethod; }
		}

		public TypeDef Type {
			get { return emptyMethod != null ? emptyMethod.DeclaringType : null; }
		}

		public EmptyClass(ModuleDefMD module) {
			this.module = module;
			Initialize();
		}

		void Initialize() {
			var callCounter = new CallCounter();
			int count = 0;
			foreach (var type in module.GetTypes()) {
				if (count >= 40)
					break;
				foreach (var method in type.Methods) {
					if (method.Name != ".ctor" && method.Name != ".cctor" && module.EntryPoint != method)
						continue;
					foreach (var calledMethod in DotNetUtils.GetCalledMethods(module, method)) {
						if (!calledMethod.IsStatic || calledMethod.Body == null)
							continue;
						if (!DotNetUtils.IsMethod(calledMethod, "System.Void", "()"))
							continue;
						if (IsEmptyClass(calledMethod)) {
							callCounter.Add(calledMethod);
							count++;
						}
					}
				}
			}

			int numCalls;
			var theMethod = (MethodDef)callCounter.Most(out numCalls);
			if (numCalls >= 10)
				emptyMethod = theMethod;
		}

		bool IsEmptyClass(MethodDef emptyMethod) {
			if (!DotNetUtils.IsEmptyObfuscated(emptyMethod))
				return false;

			var type = emptyMethod.DeclaringType;
			if (type.HasEvents || type.HasProperties)
				return false;
			if (type.Fields.Count != 1)
				return false;
			if (type.Fields[0].FieldType.FullName != "System.Boolean")
				return false;
			if (type.IsPublic)
				return false;

			int otherMethods = 0;
			foreach (var method in type.Methods) {
				if (method.Name == ".ctor" || method.Name == ".cctor")
					continue;
				if (method == emptyMethod)
					continue;
				otherMethods++;
				if (method.Body == null)
					return false;
				if (method.Body.Instructions.Count > 20)
					return false;
			}
			if (otherMethods > 8)
				return false;

			return true;
		}
	}
}
