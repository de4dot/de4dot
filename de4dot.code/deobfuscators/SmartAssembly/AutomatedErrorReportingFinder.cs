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
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.SmartAssembly {
	class AutomatedErrorReportingFinder : ExceptionLoggerRemover {
		ModuleDefinition module;
		bool enabled;

		protected override bool HasExceptionLoggers {
			get { return enabled; }
		}

		public AutomatedErrorReportingFinder(ModuleDefinition module) {
			this.module = module;
		}

		protected override bool isExceptionLogger(MethodReference method) {
			return isExceptionLoggerMethod(method);
		}

		public void find() {
			var entryPoint = module.EntryPoint;
			if (entryPoint == null)
				enabled = true;
			else {
				MethodDefinition exceptionMethod;
				enabled = checkMethod(entryPoint, out exceptionMethod);
			}
		}

		bool checkMethod(MethodDefinition method, out MethodDefinition exceptionMethod) {
			exceptionMethod = null;

			var body = method.Body;
			if (body == null)
				return false;
			var instrs = body.Instructions;
			if (instrs.Count < 1)
				return false;
			if (body.ExceptionHandlers.Count == 0)
				return false;
			var eh = body.ExceptionHandlers[body.ExceptionHandlers.Count - 1];
			if (eh.HandlerType != ExceptionHandlerType.Catch)
				return false;
			if (eh.FilterStart != null)
				return false;
			if (eh.CatchType == null || eh.CatchType.FullName != "System.Exception")
				return false;
			if (eh.HandlerStart == null)
				return false;

			int handlerStart = instrs.IndexOf(eh.HandlerStart);
			int handlerEnd = eh.HandlerEnd == null ? instrs.Count : instrs.IndexOf(eh.HandlerEnd);
			exceptionMethod = DotNetUtils.getMethod(module, checkHandler(instrs, handlerStart, handlerEnd));
			if (exceptionMethod == null || !exceptionMethod.IsStatic || exceptionMethod.Body == null)
				return false;

			return isExceptionLoggerMethod(exceptionMethod);
		}

		MethodReference checkHandler(IList<Instruction> instrs, int start, int end) {
			MethodReference calledMethod = null;
			for (int i = start; i < end; i++) {
				var instr = instrs[i];
				if (instr.OpCode.Code == Code.Call || instr.OpCode.Code == Code.Callvirt) {
					if (calledMethod != null)
						return null;
					var method = instr.Operand as MethodReference;
					if (method == null)
						return null;
					calledMethod = method;
				}
			}

			return calledMethod;
		}

		static bool isExceptionLoggerMethod(MethodReference method) {
			if (method.Name == ".ctor" || method.Name == ".cctor")
				return false;

			var parameters = method.Parameters;
			if (parameters.Count < 1)
				return false;

			var rtype = method.MethodReturnType.ReturnType.FullName;
			var type0 = parameters[0].ParameterType.FullName;
			var type1 = parameters.Count < 2 ? "" : parameters[1].ParameterType.FullName;
			int index;
			if (rtype == "System.Void") {
				if (type0 == "System.Exception" && type1 == "System.Int32")
					index = 2;
				else if (type0 == "System.Object[]" && type1 == "System.Exception")
					return true;
				else if (parameters.Count == 2 && type0 == "System.Int32" && type1 == "System.Object[]")
					return true;
				else if (type0 == "System.Exception")
					index = 1;
				else
					return false;
			}
			else if (rtype == "System.Exception") {
				if (type0 == "System.Exception" && type1 == "System.Int32")
					index = 2;
				else if (type0 == "System.Int32" && type1 == "System.Exception")
					index = 2;
				else if (parameters.Count == 2 && type0 == "System.Int32" && type1 == "System.Object[]")
					return true;
				else if (type0 == "System.Exception")
					index = 1;
				else
					return false;
			}
			else
				return false;

			if (index + 1 == parameters.Count && parameters[index].ParameterType.FullName == "System.Object[]")
				return true;

			for (int i = index; i < parameters.Count; i++) {
				if (parameters[i].ParameterType.FullName != "System.Object")
					return false;
			}

			return true;
		}
	}
}
