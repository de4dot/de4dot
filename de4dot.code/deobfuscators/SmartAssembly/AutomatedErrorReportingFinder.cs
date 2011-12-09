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

using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.SmartAssembly {
	class AutomatedErrorReportingFinder {
		ModuleDefinition module;
		ExceptionLoggerRemover exceptionLoggerRemover = new ExceptionLoggerRemover();
		TypeDefinition automatedErrorReportingType;

		public ExceptionLoggerRemover ExceptionLoggerRemover {
			get { return exceptionLoggerRemover; }
		}

		public TypeDefinition Type {
			get { return automatedErrorReportingType; }
		}

		public bool Detected {
			get { return automatedErrorReportingType != null; }
		}

		public AutomatedErrorReportingFinder(ModuleDefinition module) {
			this.module = module;
		}

		public void find() {
			foreach (var type in module.Types) {
				if (detectAutomatedErrorReportingType(type))
					break;
			}
		}

		bool detectAutomatedErrorReportingType(TypeDefinition type) {
			if (automatedErrorReportingType != null)
				return false;

			const int MIN_HELPER_METHODS = 6;

			var methods = new List<MethodDefinition>();
			MethodDefinition mainMethod = null;
			foreach (var method in type.Methods) {
				if (isAutomatedErrorReportingMethodHelper(method))
					methods.Add(method);
				else if (isAutomatedErrorReportingMethod(method))
					mainMethod = method;
			}
			if (mainMethod == null || methods.Count < MIN_HELPER_METHODS)
				return false;

			methods.Sort((a, b) => Utils.compareInt32(a.Parameters.Count, b.Parameters.Count));
			for (int i = 0; i < methods.Count; i++) {
				var method = methods[i];
				if (method.Parameters.Count != i + 1)
					return false;
				var methodCalls = DotNetUtils.getMethodCallCounts(method);
				if (methodCalls.count(mainMethod.FullName) != 1)
					return false;
			}

			exceptionLoggerRemover.add(mainMethod);
			foreach (var method in methods)
				exceptionLoggerRemover.add(method);
			automatedErrorReportingType = type;

			initUnhandledExceptionFilterMethods();

			return true;
		}

		bool isAutomatedErrorReportingMethodHelper(MethodDefinition method) {
			if (!method.HasBody || !method.IsStatic || method.Name == ".ctor")
				return false;
			if (DotNetUtils.hasReturnValue(method) && method.MethodReturnType.ReturnType.FullName != "System.Exception")
				return false;
			if (method.Parameters.Count == 0)
				return false;
			if (method.Parameters[0].ParameterType.FullName != "System.Exception")
				return false;
			for (int i = 1; i < method.Parameters.Count; i++) {
				if (method.Parameters[i].ParameterType.FullName != "System.Object")
					return false;
			}
			return true;
		}

		bool isAutomatedErrorReportingMethod(MethodDefinition method) {
			if (!method.HasBody || !method.IsStatic || method.Name == ".ctor")
				return false;
			return DotNetUtils.isMethod(method, "System.Void", "(System.Exception,System.Object[])") ||
				DotNetUtils.isMethod(method, "System.Exception", "(System.Exception,System.Object[])");
		}

		void initUnhandledExceptionFilterMethods() {
			var main = module.EntryPoint;
			if (main == null || !main.HasBody)
				return;
			if (!main.Body.HasExceptionHandlers)
				return;

			MethodDefinition mainExceptionHandlerMethod = null;
			var instructions = main.Body.Instructions;
			for (int i = 0; i < instructions.Count; i++) {
				var call = instructions[i];
				if (call.OpCode != OpCodes.Call)
					continue;
				var method = getMainExceptionHandlerMethod(call.Operand as MethodReference);
				if (method == null)
					continue;

				mainExceptionHandlerMethod = method;	// Use the last one we find
			}

			if (mainExceptionHandlerMethod != null)
				exceptionLoggerRemover.add(mainExceptionHandlerMethod);
		}

		MethodDefinition getMainExceptionHandlerMethod(MethodReference methodReference) {
			var type = DotNetUtils.getType(module, methodReference.DeclaringType);
			var method = DotNetUtils.getMethod(type, methodReference);
			if (method == null || !method.IsStatic)
				return null;

			if (method.Parameters.Count < 2)
				return null;
			if (DotNetUtils.hasReturnValue(method))
				return null;
			if (method.Parameters[0].ParameterType.ToString() != "System.Exception")
				return null;
			if (method.Parameters[method.Parameters.Count - 1].ParameterType.ToString() != "System.Object[]")
				return null;

			return method;
		}
	}
}
