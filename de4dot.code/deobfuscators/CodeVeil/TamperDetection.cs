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
using Mono.Cecil.Metadata;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CodeVeil {
	class TamperDetection {
		ModuleDefinition module;
		MainType mainType;
		TypeDefinition tamperDetectionType;
		List<MethodDefinition> tamperDetectionMethods = new List<MethodDefinition>();

		public TypeDefinition Type {
			get { return tamperDetectionType; }
		}

		public List<MethodDefinition> Methods {
			get { return tamperDetectionMethods; }
		}

		public TamperDetection(ModuleDefinition module, MainType mainType) {
			this.module = module;
			this.mainType = mainType;
		}

		public void initialize() {
			if (!mainType.Detected)
				return;

			if (mainType.TamperCheckMethod == null)
				return;

			findTamperDetectionTypes();
		}

		void findTamperDetectionTypes() {
			foreach (var type in module.Types) {
				if (!type.HasNestedTypes)
					continue;
				if ((type.Attributes & ~TypeAttributes.Sealed) != 0)
					continue;

				if (!checkTamperDetectionClasses(type.NestedTypes))
					continue;

				tamperDetectionType = type;
				findTamperDetectionMethods();
				return;
			}
		}

		void findTamperDetectionMethods() {
			foreach (var type in tamperDetectionType.NestedTypes) {
				foreach (var method in type.Methods) {
					if (!method.IsStatic || method.Body == null)
						continue;
					if (method.Name == ".cctor")
						continue;
					if (DotNetUtils.isMethod(method, "System.Void", "()"))
						tamperDetectionMethods.Add(method);
				}
			}
		}

		bool checkTamperDetectionClasses(IEnumerable<TypeDefinition> types) {
			foreach (var type in types) {
				if (!isTamperDetectionClass(type))
					return false;
			}
			return true;
		}

		bool isTamperDetectionClass(TypeDefinition type) {
			if (type.BaseType == null || type.BaseType.EType != ElementType.Object)
				return false;
			if ((type.Attributes & ~TypeAttributes.Sealed) != TypeAttributes.NestedAssembly)
				return false;

			MethodDefinition cctor = null, initMethod = null;
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					return false;
				if (method.Name == ".cctor")
					cctor = method;
				else if (DotNetUtils.isMethod(method, "System.Void", "()"))
					initMethod = method;
			}
			if (cctor == null || initMethod == null)
				return false;

			if (!callsMainTypeTamperCheckMethod(cctor))
				return false;

			return true;
		}

		bool callsMainTypeTamperCheckMethod(MethodDefinition method) {
			foreach (var info in DotNetUtils.getCalledMethods(module, method)) {
				if (info.Item2 == mainType.TamperCheckMethod)
					return true;
			}
			return false;
		}
	}
}
