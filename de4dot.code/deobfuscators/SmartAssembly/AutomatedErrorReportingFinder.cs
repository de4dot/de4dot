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
	class AutomatedErrorReportingFinder {
		ModuleDefinition module;
		ExceptionLoggerRemover exceptionLoggerRemover = new ExceptionLoggerRemover();
		TypeDefinition automatedErrorReportingType;
		int constantArgs;
		AerVersion aerVersion;

		enum AerVersion {
			V0,
			V1,
			V2,
			V3,
		}

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

			constantArgs = 0;
			var methods = new List<MethodDefinition>();
			MethodDefinition mainMethod = null;
			foreach (var method in type.Methods) {
				if (isAutomatedErrorReportingMethodHelper(method))
					methods.Add(method);
				else if (isAutomatedErrorReportingMethod(method))
					mainMethod = method;
				else
					continue;
				initializeConstantArgs(method);
			}
			if (mainMethod == null)
				return false;

			if (isV0(type)) {
				aerVersion = AerVersion.V0;
				foreach (var method in type.Methods) {
					if (method.IsStatic)
						exceptionLoggerRemover.add(method);
				}
			}
			else {
				if (methods.Count < MIN_HELPER_METHODS)
					return false;

				if (isV1(mainMethod))
					aerVersion = AerVersion.V1;
				else if (isV2(mainMethod))
					aerVersion = AerVersion.V2;
				else
					aerVersion = AerVersion.V3;

				methods.Sort((a, b) => Utils.compareInt32(a.Parameters.Count, b.Parameters.Count));
				for (int i = 0; i < methods.Count; i++) {
					var method = methods[i];
					if (method.Parameters.Count != i + constantArgs)
						return false;
					var methodCalls = DotNetUtils.getMethodCallCounts(method);
					if (methodCalls.count(mainMethod.FullName) != 1)
						return false;
				}
			}

			exceptionLoggerRemover.add(mainMethod);
			foreach (var method in methods)
				exceptionLoggerRemover.add(method);
			automatedErrorReportingType = type;

			if (aerVersion == AerVersion.V1) {
				foreach (var method in type.Methods) {
					if (DotNetUtils.isMethod(method, "System.Exception", "(System.Int32,System.Object[])"))
						exceptionLoggerRemover.add(method);
				}
			}

			initUnhandledExceptionFilterMethods();

			return true;
		}

		void initializeConstantArgs(MethodDefinition method) {
			if (constantArgs > 0)
				return;
			constantArgs = getConstantArgs(method);
		}

		static int getConstantArgs(MethodDefinition method) {
			if (method.Parameters.Count >= 2) {
				if (isV1(method) || isV2(method))
					return 2;
			}
			return 1;
		}

		static string[] v0Fields = new string[] {
			"System.Int32",
			"System.Object[]",
		};
		static bool isV0(TypeDefinition type) {
			if (!new FieldTypes(type).exactly(v0Fields))
				return false;
			if (type.Methods.Count != 3)
				return false;
			if (type.HasEvents || type.HasProperties)
				return false;
			MethodDefinition ctor = null, meth1 = null, meth2 = null;
			foreach (var method in type.Methods) {
				if (method.Name == ".ctor") {
					ctor = method;
					continue;
				}
				if (!method.IsStatic)
					return false;
				if (DotNetUtils.isMethod(method, "System.Exception", "(System.Int32,System.Object[])"))
					meth1 = method;
				else if (DotNetUtils.isMethod(method, "System.Exception", "(System.Int32,System.Exception,System.Object[])"))
					meth2 = method;
				else
					return false;
			}

			return ctor != null && meth1 != null && meth2 != null;
		}

		static bool isV1(MethodDefinition method) {
			if (method.Parameters.Count < 2)
				return false;
			var p0 = method.Parameters[0].ParameterType.FullName;
			var p1 = method.Parameters[1].ParameterType.FullName;
			return p0 == "System.Int32" && p1 == "System.Exception";
		}

		static bool isV2(MethodDefinition method) {
			if (method.Parameters.Count < 2)
				return false;
			var p0 = method.Parameters[0].ParameterType.FullName;
			var p1 = method.Parameters[1].ParameterType.FullName;
			return p0 == "System.Exception" && p1 == "System.Int32";
		}

		bool isAutomatedErrorReportingMethodHelper(MethodDefinition method) {
			if (!method.HasBody || !method.IsStatic || method.Name == ".ctor")
				return false;
			if (DotNetUtils.hasReturnValue(method) && method.MethodReturnType.ReturnType.FullName != "System.Exception")
				return false;
			if (method.Parameters.Count == 0)
				return false;
			if (!isV1(method) && !isV2(method) && method.Parameters[0].ParameterType.FullName != "System.Exception")
				return false;
			for (int i = getConstantArgs(method); i < method.Parameters.Count; i++) {
				if (method.Parameters[i].ParameterType.FullName != "System.Object")
					return false;
			}
			return true;
		}

		bool isAutomatedErrorReportingMethod(MethodDefinition method) {
			if (!method.HasBody || !method.IsStatic || method.Name == ".ctor")
				return false;
			return
				// 5.x-6.x
				DotNetUtils.isMethod(method, "System.Void", "(System.Exception,System.Object[])") ||
				// 5.x-6.x
				DotNetUtils.isMethod(method, "System.Void", "(System.Exception,System.Int32,System.Object[])") ||
				// 3.x-4.x
				DotNetUtils.isMethod(method, "System.Exception", "(System.Exception,System.Object[])") ||
				// 2.x-4.x
				DotNetUtils.isMethod(method, "System.Exception", "(System.Exception,System.Int32,System.Object[])") ||
				// 1.x
				DotNetUtils.isMethod(method, "System.Exception", "(System.Int32,System.Exception,System.Object[])");
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

			if (DotNetUtils.hasReturnValue(method))
				return null;

			switch (aerVersion) {
			case AerVersion.V0:
			case AerVersion.V1:
				if (!DotNetUtils.isMethod(method, "System.Void", "(System.Int32,System.Object[])"))
					return null;
				break;

			case AerVersion.V2:
				if (method.Parameters.Count < 2)
					return null;
				if (method.Parameters[0].ParameterType.ToString() != "System.Exception")
					return null;
				if (method.Parameters[1].ParameterType.ToString() != "System.Int32")
					return null;
				if (method.Parameters[method.Parameters.Count - 1].ParameterType.ToString() != "System.Object[]")
					return null;
				break;

			case AerVersion.V3:
				if (method.Parameters.Count < 1)
					return null;
				if (method.Parameters[0].ParameterType.ToString() != "System.Exception")
					return null;
				if (method.Parameters[method.Parameters.Count - 1].ParameterType.ToString() != "System.Object[]")
					return null;
				break;

			default:
				throw new ApplicationException("Invalid AER version");
			}

			return method;
		}
	}
}
