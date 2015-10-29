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

using System;
using dnlib.DotNet;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Agile_NET {
	class StackFrameHelper {
		ModuleDefMD module;
		TypeDef stackFrameHelperType;
		ExceptionLoggerRemover exceptionLoggerRemover = new ExceptionLoggerRemover();

		public TypeDef Type {
			get { return stackFrameHelperType; }
		}

		public ExceptionLoggerRemover ExceptionLoggerRemover {
			get { return exceptionLoggerRemover; }
		}

		public StackFrameHelper(ModuleDefMD module) {
			this.module = module;
		}

		public void Find() {
			foreach (var type in module.Types) {
				if (!type.HasMethods)
					continue;
				if (type.Methods.Count > 3)
					continue;

				MethodDef errorMethod = null;
				foreach (var method in type.Methods) {
					if (method.Name == ".ctor")
						continue;	// .ctor is allowed
					if (method.Name == ".cctor")
						continue;	// .cctor is allowed
					var sig = method.MethodSig;
					if (sig != null && method.IsStatic && method.HasBody &&
						sig.Params.Count == 2 && !method.HasGenericParameters &&
						!DotNetUtils.HasReturnValue(method) &&
						sig.Params[0].GetFullName() == "System.Exception" &&
						sig.Params[1].GetFullName() == "System.Object[]") {
						errorMethod = method;
					}
					else
						break;
				}
				if (errorMethod != null) {
					stackFrameHelperType = type;
					exceptionLoggerRemover.Add(errorMethod);
					return;
				}
			}
		}
	}
}
