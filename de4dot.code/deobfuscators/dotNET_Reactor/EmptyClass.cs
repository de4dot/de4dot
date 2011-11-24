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

namespace de4dot.deobfuscators.dotNET_Reactor {
	// Detect some empty class that is called from most .ctor's
	class EmptyClass {
		ModuleDefinition module;
		MethodDefinition emptyMethod;

		public MethodDefinition Method {
			get { return emptyMethod; }
		}

		public TypeDefinition Type {
			get { return emptyMethod != null ? emptyMethod.DeclaringType : null; }
		}

		public EmptyClass(ModuleDefinition module) {
			this.module = module;
			init();
		}

		void init() {
			var callCounter = new CallCounter();
			int count = 0;
			foreach (var type in module.GetTypes()) {
				if (count >= 40)
					break;
				foreach (var method in type.Methods) {
					if (method.Name != ".ctor")
						continue;
					foreach (var tuple in DotNetUtils.getCalledMethods(module, method)) {
						var calledMethod = tuple.Item2;
						if (!calledMethod.IsStatic || calledMethod.Body == null)
							continue;
						if (!DotNetUtils.isMethod(calledMethod, "System.Void", "()"))
							continue;
						if (isEmptyClass(calledMethod)) {
							callCounter.add(calledMethod);
							count++;
						}
					}
				}
			}

			emptyMethod = (MethodDefinition)callCounter.most();
		}

		bool isEmptyClass(MethodDefinition emptyMethod) {
			if (!DotNetUtils.isEmptyObfuscated(emptyMethod))
				return false;

			var type = emptyMethod.DeclaringType;
			if (type.HasEvents || type.HasProperties)
				return false;
			if (type.Fields.Count != 1)
				return false;
			if (type.Fields[0].FieldType.FullName != "System.Boolean")
				return false;

			foreach (var method in type.Methods) {
				if (method.Name == ".ctor" || method.Name == ".cctor")
					continue;
				if (method == emptyMethod)
					continue;
				return false;
			}

			return true;
		}
	}
}
