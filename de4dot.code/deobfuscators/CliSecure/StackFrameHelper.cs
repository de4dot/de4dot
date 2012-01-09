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

using System;
using Mono.Cecil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CliSecure {
	class StackFrameHelper {
		ModuleDefinition module;
		TypeDefinition stackFrameHelperType;
		ExceptionLoggerRemover exceptionLoggerRemover = new ExceptionLoggerRemover();

		public TypeDefinition Type {
			get { return stackFrameHelperType; }
		}

		public ExceptionLoggerRemover ExceptionLoggerRemover {
			get { return exceptionLoggerRemover; }
		}

		public StackFrameHelper(ModuleDefinition module) {
			this.module = module;
		}

		public void find() {
			foreach (var type in module.Types) {
				if (!type.HasMethods)
					continue;
				if (type.Methods.Count > 3)
					continue;

				MethodDefinition errorMethod = null;
				foreach (var method in type.Methods) {
					if (method.IsRuntimeSpecialName && method.Name == ".ctor" && !method.HasParameters)
						continue;	// .ctor is allowed
					if (method.IsRuntimeSpecialName && method.Name == ".cctor" && !method.HasParameters)
						continue;	// .cctor is allowed
					if (method.IsStatic && method.CallingConvention == MethodCallingConvention.Default &&
						method.ExplicitThis == false && method.HasThis == false &&
						method.HasBody && method.IsManaged && method.IsIL && method.HasParameters &&
						method.Parameters.Count == 2 && !method.HasGenericParameters &&
						!DotNetUtils.hasReturnValue(method) &&
						method.Parameters[0].ParameterType.FullName == "System.Exception" &&
						method.Parameters[1].ParameterType.FullName == "System.Object[]") {
						errorMethod = method;
					}
					else
						break;
				}
				if (errorMethod != null) {
					stackFrameHelperType = type;
					exceptionLoggerRemover.add(errorMethod);
					return;
				}
			}
		}
	}
}
