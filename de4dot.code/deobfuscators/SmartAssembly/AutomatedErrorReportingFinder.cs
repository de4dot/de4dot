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

using System.Collections.Generic;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.SmartAssembly {
	class AutomatedErrorReportingFinder : ExceptionLoggerRemover {
		ModuleDefMD module;
		bool enabled;

		protected override bool HasExceptionLoggers => enabled;
		public AutomatedErrorReportingFinder(ModuleDefMD module) => this.module = module;
		protected override bool IsExceptionLogger(IMethod method) => IsExceptionLoggerMethod(method);

		public void Find() {
			var entryPoint = module.EntryPoint;
			if (entryPoint == null)
				enabled = true;
			else
				enabled = CheckMethod(entryPoint, out var exceptionMethod);
		}

		bool CheckMethod(MethodDef method, out MethodDef exceptionMethod) {
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
			exceptionMethod = DotNetUtils.GetMethod(module, CheckHandler(instrs, handlerStart, handlerEnd));
			if (exceptionMethod == null || !exceptionMethod.IsStatic || exceptionMethod.Body == null)
				return false;

			return IsExceptionLoggerMethod(exceptionMethod);
		}

		IMethod CheckHandler(IList<Instruction> instrs, int start, int end) {
			IMethod calledMethod = null;
			for (int i = start; i < end; i++) {
				var instr = instrs[i];
				if (instr.OpCode.Code == Code.Call || instr.OpCode.Code == Code.Callvirt) {
					if (calledMethod != null)
						return null;
					var method = instr.Operand as IMethod;
					if (method == null)
						return null;
					calledMethod = method;
				}
			}

			return calledMethod;
		}

		static bool IsExceptionLoggerMethod(IMethod method) {
			if (method.Name == ".ctor" || method.Name == ".cctor")
				return false;

			var sig = method.MethodSig;
			if (sig == null || sig.Params.Count < 1)
				return false;

			var rtype = sig.RetType.GetFullName();
			var type0 = sig.Params[0].GetFullName();
			var type1 = sig.Params.Count < 2 ? "" : sig.Params[1].GetFullName();
			int index;
			if (rtype == "System.Void") {
				if (type0 == "System.Exception" && type1 == "System.Int32")
					index = 2;
				else if (type0 == "System.Object[]" && type1 == "System.Exception")
					return true;
				else if (sig.Params.Count == 2 && type0 == "System.Int32" && type1 == "System.Object[]")
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
				else if (sig.Params.Count == 2 && type0 == "System.Int32" && type1 == "System.Object[]")
					return true;
				else if (type0 == "System.Exception")
					index = 1;
				else
					return false;
			}
			else
				return false;

			if (index + 1 == sig.Params.Count && sig.Params[index].GetFullName() == "System.Object[]")
				return true;

			for (int i = index; i < sig.Params.Count; i++) {
				if (sig.Params[i].GetElementType() != ElementType.Object)
					return false;
			}

			return true;
		}
	}
}
