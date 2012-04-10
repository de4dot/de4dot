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
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CliSecure.vm {
	class UnknownHandlerInfo {
		TypeDefinition type;
		FieldsInfo fieldsInfo;
		MethodDefinition readMethod, executeMethod;
		int numStaticMethods, numInstanceMethods, numVirtualMethods, numCtors;
		int executeMethodThrows;

		public MethodDefinition ReadMethod {
			get { return readMethod; }
		}

		public MethodDefinition ExecuteMethod {
			get { return executeMethod; }
		}

		public int NumStaticMethods {
			get { return numStaticMethods; }
		}

		public int NumInstanceMethods {
			get { return numInstanceMethods; }
		}

		public int NumVirtualMethods {
			get { return numVirtualMethods; }
		}

		public int ExecuteMethodThrows {
			get { return executeMethodThrows; }
		}

		public int NumCtors {
			get { return numCtors; }
		}

		public UnknownHandlerInfo(TypeDefinition type) {
			this.type = type;
			fieldsInfo = new FieldsInfo(type);
			countMethods();
			findOverrideMethods();
			executeMethodThrows = countThrows(executeMethod);
		}

		void countMethods() {
			foreach (var method in type.Methods) {
				if (method.Name == ".cctor") {
				}
				else if (method.Name == ".ctor")
					numCtors++;
				else if (method.IsStatic)
					numStaticMethods++;
				else if (method.IsVirtual)
					numVirtualMethods++;
				else
					numInstanceMethods++;
			}
		}

		void findOverrideMethods() {
			foreach (var method in type.Methods) {
				if (!method.IsVirtual)
					continue;
				if (DotNetUtils.isMethod(method, "System.Void", "(System.IO.BinaryReader)")) {
					if (readMethod != null)
						throw new ApplicationException("Found another read method");
					readMethod = method;
				}
				else if (!DotNetUtils.hasReturnValue(method) && method.Parameters.Count == 1) {
					if (executeMethod != null)
						throw new ApplicationException("Found another execute method");
					executeMethod = method;
				}
			}

			if (readMethod == null)
				throw new ApplicationException("Could not find read method");
			if (executeMethod == null)
				throw new ApplicationException("Could not find execute method");
		}

		static int countThrows(MethodDefinition method) {
			int count = 0;
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code == Code.Throw)
					count++;
			}
			return count;
		}

		public bool hasSameFieldTypes(object[] fieldTypes) {
			return new FieldsInfo(fieldTypes).isSame(fieldsInfo);
		}
	}
}
