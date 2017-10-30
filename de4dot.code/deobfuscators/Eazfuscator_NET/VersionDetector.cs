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
using System.Collections.Generic;
using dnlib.DotNet.Emit;
using dnlib.DotNet;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Eazfuscator_NET {
	class VersionDetector {
		StringDecrypter stringDecrypter;
		FrameworkType frameworkType;

		public VersionDetector(ModuleDefMD module, StringDecrypter stringDecrypter) {
			this.stringDecrypter = stringDecrypter;
			this.frameworkType = DotNetUtils.GetFrameworkType(module);
		}

		public string Detect() {
			var decryptStringType = stringDecrypter.Type;
			var decryptStringMethod = stringDecrypter.Method;
			if (decryptStringType == null || decryptStringMethod == null)
				return null;

			var otherMethods = new List<MethodDef>();
			MethodDef cctor = null;
			foreach (var method in decryptStringType.Methods) {
				if (method == decryptStringMethod)
					continue;
				if (method.Name == ".cctor")
					cctor = method;
				else
					otherMethods.Add(method);
			}
			if (cctor == null)
				return null;

			bool hasConstantM2 = DeobUtils.HasInteger(decryptStringMethod, -2);

			/////////////////////////////////////////////////////////////////
			/////////////////////////////////////////////////////////////////
			/////////////////////////////////////////////////////////////////

			var fields11 = new string[] {
				"System.Collections.Generic.Dictionary`2<System.Int32,System.String>",
				"System.IO.BinaryReader",
				"System.Byte[]",
				"System.Int16",
				"System.Boolean",
			};
			var locals11 = CreateLocalsArray(
				"System.Boolean",
				"System.Byte[]",
				"System.Char[]",
				"System.Int16",
				"System.Int32",
				"System.Reflection.Assembly",
				"System.String"
			);
			if (otherMethods.Count == 0 &&
				decryptStringType.NestedTypes.Count == 0 &&
				!hasConstantM2 &&
				!decryptStringMethod.IsNoInlining &&
				decryptStringMethod.IsPublic &&
				decryptStringMethod.IsSynchronized &&
				decryptStringMethod.Body.MaxStack >= 35 &&
				decryptStringMethod.Body.MaxStack <= 50 &&
				decryptStringMethod.Body.ExceptionHandlers.Count == 0 &&
				new LocalTypes(decryptStringMethod).Exactly(locals11) &&
				CheckTypeFields(fields11)) {
				return "1.1 - 1.2";
			}

			/////////////////////////////////////////////////////////////////
			/////////////////////////////////////////////////////////////////
			/////////////////////////////////////////////////////////////////

			var fields13 = new string[] {
				"System.Collections.Generic.Dictionary`2<System.Int32,System.String>",
				"System.IO.BinaryReader",
				"System.Byte[]",
				"System.Int16",
				"System.Boolean",
				"System.Byte[]",
			};
			var locals13 = CreateLocalsArray(
				"System.Boolean",
				"System.Byte",
				"System.Byte[]",
				"System.Char[]",
				"System.Int16",
				"System.Int32",
				"System.Reflection.Assembly",
				"System.String"
			);
			if (otherMethods.Count == 0 &&
				decryptStringType.NestedTypes.Count == 0 &&
				!hasConstantM2 &&
				!decryptStringMethod.IsNoInlining &&
				decryptStringMethod.IsPublic &&
				decryptStringMethod.IsSynchronized &&
				decryptStringMethod.Body.MaxStack >= 35 &&
				decryptStringMethod.Body.MaxStack <= 50 &&
				decryptStringMethod.Body.ExceptionHandlers.Count == 0 &&
				new LocalTypes(decryptStringMethod).Exactly(locals13) &&
				CheckTypeFields(fields13)) {
				return "1.3";
			}

			/////////////////////////////////////////////////////////////////
			/////////////////////////////////////////////////////////////////
			/////////////////////////////////////////////////////////////////

			var fields14 = new string[] {
				"System.Collections.Generic.Dictionary`2<System.Int32,System.String>",
				"System.IO.BinaryReader",
				"System.Byte[]",
				"System.Int16",
				"System.Boolean",
				"System.Byte[]",
			};
			var locals14 = CreateLocalsArray(
				"System.Boolean",
				"System.Byte",
				"System.Byte[]",
				"System.Char[]",
				"System.Int16",
				"System.Int32",
				"System.Reflection.Assembly",
				"System.String"
			);
			if (otherMethods.Count == 0 &&
				decryptStringType.NestedTypes.Count == 0 &&
				!hasConstantM2 &&
				!decryptStringMethod.IsNoInlining &&
				decryptStringMethod.IsPublic &&
				decryptStringMethod.IsSynchronized &&
				decryptStringMethod.Body.MaxStack >= 150 &&
				decryptStringMethod.Body.MaxStack <= 200 &&
				decryptStringMethod.Body.ExceptionHandlers.Count == 0 &&
				new LocalTypes(decryptStringMethod).Exactly(locals14) &&
				CheckTypeFields(fields14)) {
				return "1.4 - 2.3";
			}

			/////////////////////////////////////////////////////////////////
			/////////////////////////////////////////////////////////////////
			/////////////////////////////////////////////////////////////////

			var fields24 = new string[] {
				"System.Collections.Generic.Dictionary`2<System.Int32,System.String>",
				"System.IO.BinaryReader",
				"System.Byte[]",
				"System.Int16",
				"System.Boolean",
				"System.Byte[]",
			};
			var locals24 = CreateLocalsArray(
				"System.Boolean",
				"System.Byte",
				"System.Byte[]",
				"System.Char[]",
				"System.Int16",
				"System.Int32",
				"System.Reflection.Assembly",
				"System.Reflection.AssemblyName",
				"System.String"
			);
			if (otherMethods.Count == 0 &&
				decryptStringType.NestedTypes.Count == 0 &&
				!hasConstantM2 &&
				!decryptStringMethod.IsNoInlining &&
				decryptStringMethod.IsPublic &&
				decryptStringMethod.IsSynchronized &&
				decryptStringMethod.Body.MaxStack >= 1 &&
				decryptStringMethod.Body.MaxStack <= 8 &&
				decryptStringMethod.Body.ExceptionHandlers.Count == 0 &&
				new LocalTypes(decryptStringMethod).Exactly(locals24) &&
				CheckTypeFields(fields24)) {
				return "2.4 - 2.5";
			}

			/////////////////////////////////////////////////////////////////
			/////////////////////////////////////////////////////////////////
			/////////////////////////////////////////////////////////////////

			var fields26 = new string[] {
				"System.Collections.Generic.Dictionary`2<System.Int32,System.String>",
				"System.IO.BinaryReader",
				"System.Byte[]",
				"System.Int16",
				"System.Boolean",
				"System.Byte[]",
			};
			var locals26 = CreateLocalsArray(
				"System.Boolean",
				"System.Byte",
				"System.Byte[]",
				"System.Char[]",
				"System.Collections.Generic.Dictionary`2<System.Int32,System.String>",
				"System.Int16",
				"System.Int32",
				"System.Reflection.Assembly",
				"System.Reflection.AssemblyName",
				"System.String"
			);
			if (otherMethods.Count == 0 &&
				decryptStringType.NestedTypes.Count == 0 &&
				!hasConstantM2 &&
				!decryptStringMethod.IsNoInlining &&
				decryptStringMethod.IsPublic &&
				!decryptStringMethod.IsSynchronized &&
				decryptStringMethod.Body.MaxStack >= 1 &&
				decryptStringMethod.Body.MaxStack <= 8 &&
				decryptStringMethod.Body.ExceptionHandlers.Count == 1 &&
				new LocalTypes(decryptStringMethod).Exactly(locals26) &&
				CheckTypeFields(fields26)) {
				return "2.6";
			}

			/////////////////////////////////////////////////////////////////
			/////////////////////////////////////////////////////////////////
			/////////////////////////////////////////////////////////////////

			var fields27 = new string[] {
				"System.Collections.Generic.Dictionary`2<System.Int32,System.String>",
				"System.IO.BinaryReader",
				"System.Byte[]",
				"System.Int16",
				"System.Boolean",
				"System.Byte[]",
			};
			var locals27 = CreateLocalsArray(
				"System.Boolean",
				"System.Byte",
				"System.Byte[]",
				"System.Char[]",
				"System.Collections.Generic.Dictionary`2<System.Int32,System.String>",
				"System.Int16",
				"System.Int32",
				"System.Reflection.Assembly",
				"System.Reflection.AssemblyName",
				"System.String"
			);
			if (otherMethods.Count == 0 &&
				decryptStringType.NestedTypes.Count == 0 &&
				!hasConstantM2 &&
				decryptStringMethod.IsNoInlining &&
				decryptStringMethod.IsPublic &&
				!decryptStringMethod.IsSynchronized &&
				decryptStringMethod.Body.MaxStack >= 1 &&
				decryptStringMethod.Body.MaxStack <= 8 &&
				decryptStringMethod.Body.ExceptionHandlers.Count == 1 &&
				new LocalTypes(decryptStringMethod).Exactly(locals27) &&
				CheckTypeFields(fields27)) {
				return "2.7";
			}

			/////////////////////////////////////////////////////////////////
			/////////////////////////////////////////////////////////////////
			/////////////////////////////////////////////////////////////////

			var fields28 = new string[] {
				"System.Collections.Generic.Dictionary`2<System.Int32,System.String>",
				"System.IO.BinaryReader",
				"System.Byte[]",
				"System.Int16",
				"System.Boolean",
				"System.Byte[]",
				"System.Boolean",
			};
			var locals28 = CreateLocalsArray(
				"System.Boolean",
				"System.Byte",
				"System.Byte[]",
				"System.Char[]",
				"System.Collections.Generic.Dictionary`2<System.Int32,System.String>",
				"System.Int16",
				"System.Int32",
				"System.Reflection.Assembly",
				"System.Reflection.AssemblyName",
				"System.String"
			);
			if (otherMethods.Count == 0 &&
				decryptStringType.NestedTypes.Count == 0 &&
				!hasConstantM2 &&
				decryptStringMethod.IsNoInlining &&
				decryptStringMethod.IsAssembly &&
				!decryptStringMethod.IsSynchronized &&
				decryptStringMethod.Body.MaxStack >= 1 &&
				decryptStringMethod.Body.MaxStack <= 8 &&
				decryptStringMethod.Body.ExceptionHandlers.Count == 1 &&
				new LocalTypes(decryptStringMethod).Exactly(locals28) &&
				CheckTypeFields(fields28)) {
				return "2.8";
			}

			/////////////////////////////////////////////////////////////////
			/////////////////////////////////////////////////////////////////
			/////////////////////////////////////////////////////////////////

			var fields29 = new string[] {
				"System.Collections.Generic.Dictionary`2<System.Int32,System.String>",
				"System.IO.BinaryReader",
				"System.Byte[]",
				"System.Int16",
				"System.Int32",
				"System.Byte[]",
			};
			var locals29 = CreateLocalsArray(
				"System.Boolean",
				"System.Byte",
				"System.Byte[]",
				"System.Char[]",
				"System.Collections.Generic.Dictionary`2<System.Int32,System.String>",
				"System.Diagnostics.StackFrame",
				"System.Diagnostics.StackTrace",
				"System.Int16",
				"System.Int32",
				"System.IO.Stream",
				"System.Reflection.Assembly",
				"System.Reflection.AssemblyName",
				"System.Reflection.MethodBase",
				"System.String",
				"System.Type"
			);
			if (otherMethods.Count == 0 &&
				decryptStringType.NestedTypes.Count == 0 &&
				!hasConstantM2 &&
				decryptStringMethod.IsNoInlining &&
				decryptStringMethod.IsAssembly &&
				!decryptStringMethod.IsSynchronized &&
				decryptStringMethod.Body.MaxStack >= 1 &&
				decryptStringMethod.Body.MaxStack <= 8 &&
				(decryptStringMethod.Body.ExceptionHandlers.Count == 1 || decryptStringMethod.Body.ExceptionHandlers.Count == 2) &&
				new LocalTypes(decryptStringMethod).Exactly(locals29) &&
				CheckTypeFields(fields29)) {
				return "2.9";
			}

			/////////////////////////////////////////////////////////////////
			/////////////////////////////////////////////////////////////////
			/////////////////////////////////////////////////////////////////

			var fields30 = new string[] {
				"System.Collections.Generic.Dictionary`2<System.Int32,System.String>",
				"System.IO.BinaryReader",
				"System.Byte[]",
				"System.Int16",
				"System.Int32",
				"System.Byte[]",
			};
			var locals30 = CreateLocalsArray(
				"System.Boolean",
				"System.Byte",
				"System.Byte[]",
				"System.Char[]",
				"System.Collections.Generic.Dictionary`2<System.Int32,System.String>",
				"System.Diagnostics.StackFrame",
				"System.Diagnostics.StackTrace",
				"System.Int16",
				"System.Int32",
				"System.IO.Stream",
				"System.Reflection.Assembly",
				"System.Reflection.AssemblyName",
				"System.Reflection.MethodBase",
				"System.String",
				"System.Type"
			);
			var olocals30 = CreateLocalsArray(
				"System.Int32"
			);
			if (otherMethods.Count == 1 &&
				decryptStringType.NestedTypes.Count == 0 &&
				DotNetUtils.IsMethod(otherMethods[0], "System.Int32", "(System.Byte[],System.Int32,System.Byte[])") &&
				otherMethods[0].IsPrivate &&
				otherMethods[0].IsStatic &&
				new LocalTypes(otherMethods[0]).Exactly(olocals30) &&
				!hasConstantM2 &&
				decryptStringMethod.IsNoInlining &&
				decryptStringMethod.IsAssembly &&
				!decryptStringMethod.IsSynchronized &&
				decryptStringMethod.Body.MaxStack >= 1 &&
				decryptStringMethod.Body.MaxStack <= 8 &&
				(decryptStringMethod.Body.ExceptionHandlers.Count == 1 || decryptStringMethod.Body.ExceptionHandlers.Count == 2) &&
				new LocalTypes(decryptStringMethod).Exactly(locals30) &&
				CheckTypeFields(fields30)) {
				return "3.0";
			}

			/////////////////////////////////////////////////////////////////
			/////////////////////////////////////////////////////////////////
			/////////////////////////////////////////////////////////////////

			var fields31 = new string[] {
				"System.Collections.Generic.Dictionary`2<System.Int32,System.String>",
				"System.IO.BinaryReader",
				"System.Byte[]",
				"System.Int16",
				"System.Int32",
				"System.Byte[]",
			};
			var locals31 = CreateLocalsArray(
				"System.Boolean",
				"System.Byte",
				"System.Byte[]",
				"System.Char[]",
				"System.Collections.Generic.Dictionary`2<System.Int32,System.String>",
				"System.Diagnostics.StackFrame",
				"System.Diagnostics.StackTrace",
				"System.Int16",
				"System.Int32",
				"System.IO.Stream",
				"System.Reflection.Assembly",
				"System.Reflection.AssemblyName",
				"System.Reflection.MethodBase",
				"System.String",
				"System.Type"
			);
			var olocals31 = CreateLocalsArray(
				"System.Int32"
			);
			if (otherMethods.Count == 1 &&
				decryptStringType.NestedTypes.Count == 0 &&
				DotNetUtils.IsMethod(otherMethods[0], "System.Int32", "(System.Byte[],System.Int32,System.Byte[])") &&
				otherMethods[0].IsPrivate &&
				otherMethods[0].IsStatic &&
				new LocalTypes(otherMethods[0]).Exactly(olocals31) &&
				hasConstantM2 &&
				decryptStringMethod.IsNoInlining &&
				decryptStringMethod.IsAssembly &&
				!decryptStringMethod.IsSynchronized &&
				decryptStringMethod.Body.MaxStack >= 1 &&
				decryptStringMethod.Body.MaxStack <= 8 &&
				(decryptStringMethod.Body.ExceptionHandlers.Count == 1 || decryptStringMethod.Body.ExceptionHandlers.Count == 2) &&
				new LocalTypes(decryptStringMethod).Exactly(locals31) &&
				CheckTypeFields(fields31)) {
				return "3.1";
			}

			/////////////////////////////////////////////////////////////////
			/////////////////////////////////////////////////////////////////
			/////////////////////////////////////////////////////////////////

			var fields32 = new string[] {
				"System.Collections.Generic.Dictionary`2<System.Int32,System.String>",
				"System.IO.BinaryReader",
				"System.Byte[]",
				"System.Int16",
				"System.Int32",
				"System.Byte[]",
				"System.Int32",
			};
			var locals32 = CreateLocalsArray(
				"System.Boolean",
				"System.Byte",
				"System.Byte[]",
				"System.Char[]",
				"System.Collections.Generic.Dictionary`2<System.Int32,System.String>",
				"System.Diagnostics.StackFrame",
				"System.Diagnostics.StackTrace",
				"System.Int16",
				"System.Int32",
				"System.Int64",
				"System.IO.Stream",
				"System.Reflection.Assembly",
				"System.Reflection.AssemblyName",
				"System.Reflection.MethodBase",
				"System.String",
				"System.Type"
			);
			var olocals32 = CreateLocalsArray(
				"System.Int32"
			);
			if (otherMethods.Count == 1 &&
				decryptStringType.NestedTypes.Count == 0 &&
				DotNetUtils.IsMethod(otherMethods[0], "System.Void", "(System.Byte[],System.Int32,System.Byte[])") &&
				otherMethods[0].IsPrivate &&
				otherMethods[0].IsStatic &&
				new LocalTypes(otherMethods[0]).Exactly(olocals32) &&
				hasConstantM2 &&
				decryptStringMethod.IsNoInlining &&
				decryptStringMethod.IsAssembly &&
				!decryptStringMethod.IsSynchronized &&
				decryptStringMethod.Body.MaxStack >= 1 &&
				decryptStringMethod.Body.MaxStack <= 8 &&
				(decryptStringMethod.Body.ExceptionHandlers.Count == 1 || decryptStringMethod.Body.ExceptionHandlers.Count == 2) &&
				new LocalTypes(decryptStringMethod).Exactly(locals32) &&
				CheckTypeFields(fields32)) {
				return "3.2";
			}

			/////////////////////////////////////////////////////////////////
			/////////////////////////////////////////////////////////////////
			/////////////////////////////////////////////////////////////////

			if (decryptStringType.NestedTypes.Count == 1) {
				var fields33 = new string[] {
					"System.Collections.Generic.Dictionary`2<System.Int32,System.String>",
					"System.IO.BinaryReader",
					"System.Byte[]",
					"System.Int16",
					"System.Int32",
					"System.Byte[]",
					"System.Int32",
					"System.Int32",
					decryptStringType.NestedTypes[0].FullName,
				};
				var locals33 = CreateLocalsArray(
					"System.Boolean",
					"System.Byte",
					"System.Byte[]",
					"System.Char[]",
					"System.Collections.Generic.Dictionary`2<System.Int32,System.String>",
					"System.Diagnostics.StackFrame",
					"System.Diagnostics.StackTrace",
					"System.Int16",
					"System.Int32",
					"System.Int64",
					"System.IO.Stream",
					"System.Reflection.Assembly",
					"System.Reflection.AssemblyName",
					"System.Reflection.MethodBase",
					"System.String",
					"System.Type"
				);
				var olocals33 = CreateLocalsArray(
					"System.Int32"
				);
				if (otherMethods.Count == 1 &&
					decryptStringType.NestedTypes.Count == 1 &&
					DotNetUtils.IsMethod(otherMethods[0], "System.Void", "(System.Byte[],System.Int32,System.Byte[])") &&
					otherMethods[0].IsPrivate &&
					otherMethods[0].IsStatic &&
					new LocalTypes(otherMethods[0]).Exactly(olocals33) &&
					hasConstantM2 &&
					decryptStringMethod.IsNoInlining &&
					decryptStringMethod.IsAssembly &&
					!decryptStringMethod.IsSynchronized &&
					decryptStringMethod.Body.MaxStack >= 1 &&
					decryptStringMethod.Body.MaxStack <= 8 &&
					(decryptStringMethod.Body.ExceptionHandlers.Count == 1 || decryptStringMethod.Body.ExceptionHandlers.Count == 2) &&
					new LocalTypes(decryptStringMethod).Exactly(locals33) &&
					CheckTypeFields(fields33)) {
					return "3.3.29 - 3.3.57 (BETA)";
				}
			}

			/////////////////////////////////////////////////////////////////
			/////////////////////////////////////////////////////////////////
			/////////////////////////////////////////////////////////////////

			if (decryptStringType.NestedTypes.Count == 3) {
				var fields33 = new string[] {
					GetNestedTypeName(0),
					GetNestedTypeName(1),
					"System.Byte[]",
					"System.Int16",
					"System.Int32",
					"System.Byte[]",
					"System.Int32",
					"System.Int32",
					GetNestedTypeName(2),
				};
				var locals33 = CreateLocalsArray(
					"System.Boolean",
					"System.Byte",
					"System.Byte[]",
					"System.Char[]",
					GetNestedTypeName(0),
					"System.Diagnostics.StackFrame",
					"System.Diagnostics.StackTrace",
					"System.Int16",
					"System.Int32",
					"System.Int64",
					"System.IO.Stream",
					"System.Reflection.Assembly",
					"System.Reflection.AssemblyName",
					"System.Reflection.MethodBase",
					"System.String",
					"System.Type"
				);
				var olocals33 = CreateLocalsArray(
					"System.Int32"
				);
				if (otherMethods.Count == 1 &&
					decryptStringType.NestedTypes.Count == 3 &&
					DotNetUtils.IsMethod(otherMethods[0], "System.Void", "(System.Byte[],System.Int32,System.Byte[])") &&
					otherMethods[0].IsPrivate &&
					otherMethods[0].IsStatic &&
					new LocalTypes(otherMethods[0]).Exactly(olocals33) &&
					decryptStringMethod.IsNoInlining &&
					decryptStringMethod.IsAssembly &&
					!decryptStringMethod.IsSynchronized &&
					decryptStringMethod.Body.MaxStack >= 1 &&
					decryptStringMethod.Body.MaxStack <= 8 &&
					(decryptStringMethod.Body.ExceptionHandlers.Count == 1 || decryptStringMethod.Body.ExceptionHandlers.Count == 2) &&
					new LocalTypes(decryptStringMethod).Exactly(locals33) &&
					CheckTypeFields(fields33)) {
					return "3.3";
				}

				/////////////////////////////////////////////////////////////////
				/////////////////////////////////////////////////////////////////
				/////////////////////////////////////////////////////////////////

				var fields33_149 = new string[] {
					GetNestedTypeName(0),
					GetNestedTypeName(1),
					"System.Byte[]",
					"System.Int16",
					"System.Int32",
					"System.Byte[]",
					"System.Int32",
					"System.Int32",
					GetNestedTypeName(2),
				};
				var locals33_149 = CreateLocalsArray(
					"System.Boolean",
					"System.Byte",
					"System.Byte[]",
					"System.Char[]",
					GetNestedTypeName(0),
					"System.Diagnostics.StackFrame",
					"System.Diagnostics.StackTrace",
					"System.Int16",
					"System.Int32",
					"System.Int64",
					"System.IO.Stream",
					"System.Reflection.Assembly",
					"System.Reflection.AssemblyName",
					"System.Reflection.MethodBase",
					"System.String",
					"System.Text.StringBuilder",
					"System.Type"
				);
				var olocals33_149 = CreateLocalsArray(
					"System.Int32"
				);
				if (otherMethods.Count == 1 &&
					decryptStringType.NestedTypes.Count == 3 &&
					DotNetUtils.IsMethod(otherMethods[0], "System.Void", "(System.Byte[],System.Int32,System.Byte[])") &&
					otherMethods[0].IsPrivate &&
					otherMethods[0].IsStatic &&
					new LocalTypes(otherMethods[0]).Exactly(olocals33_149) &&
					decryptStringMethod.IsNoInlining &&
					decryptStringMethod.IsAssembly &&
					!decryptStringMethod.IsSynchronized &&
					decryptStringMethod.Body.MaxStack >= 1 &&
					decryptStringMethod.Body.MaxStack <= 8 &&
					(decryptStringMethod.Body.ExceptionHandlers.Count == 1 || decryptStringMethod.Body.ExceptionHandlers.Count == 2) &&
					new LocalTypes(decryptStringMethod).Exactly(locals33_149) &&
					CheckTypeFields2(fields33_149)) {
					return "3.3.149 - 3.4"; // 3.3.149+ (but not SL or CF)
				}

				/////////////////////////////////////////////////////////////////
				/////////////////////////////////////////////////////////////////
				/////////////////////////////////////////////////////////////////

				var fields35 = new string[] {
					GetNestedTypeName(0),
					GetNestedTypeName(1),
					"System.Byte[]",
					"System.Int16",
					"System.Int32",
					"System.Byte[]",
					"System.Int32",
					"System.Int32",
					GetNestedTypeName(2),
				};
				var locals35 = CreateLocalsArray(
					"System.Boolean",
					"System.Byte",
					"System.Byte[]",
					"System.Char[]",
					"System.Collections.Generic.IEnumerator`1<System.Int32>",
					GetNestedTypeName(0),
					"System.Diagnostics.StackFrame",
					"System.Diagnostics.StackTrace",
					"System.Int16",
					"System.Int32",
					"System.Int64",
					"System.IO.Stream",
					"System.Reflection.Assembly",
					"System.Reflection.AssemblyName",
					"System.Reflection.MethodBase",
					"System.String",
					"System.Text.StringBuilder",
					"System.Type"
				);
				var olocals35 = CreateLocalsArray(
					"System.Int32"
				);
				if (otherMethods.Count == 1 &&
					decryptStringType.NestedTypes.Count == 3 &&
					DotNetUtils.IsMethod(otherMethods[0], "System.Void", "(System.Byte[],System.Int32,System.Byte[])") &&
					otherMethods[0].IsPrivate &&
					otherMethods[0].IsStatic &&
					new LocalTypes(otherMethods[0]).Exactly(olocals35) &&
					decryptStringMethod.IsNoInlining &&
					decryptStringMethod.IsAssembly &&
					!decryptStringMethod.IsSynchronized &&
					decryptStringMethod.Body.MaxStack >= 1 &&
					decryptStringMethod.Body.MaxStack <= 8 &&
					decryptStringMethod.Body.ExceptionHandlers.Count >= 2 &&
					new LocalTypes(decryptStringMethod).All(locals35) &&
					CheckTypeFields2(fields35)) {
					return "3.5 - 4.2";
				}

				/////////////////////////////////////////////////////////////////
				/////////////////////////////////////////////////////////////////
				/////////////////////////////////////////////////////////////////

				var fields43 = new string[] {
					GetNestedTypeName(0),
					GetNestedTypeName(1),
					"System.Byte[]",
					"System.Int16",
					"System.Int32",
					"System.Byte[]",
					"System.Int32",
					"System.Int32",
					GetNestedTypeName(2),
				};
				var locals43 = CreateLocalsArray(
					"System.Boolean",
					"System.Byte",
					"System.Byte[]",
					"System.Char[]",
					FindEnumeratorName(decryptStringMethod),
					GetNestedTypeName(0),
					"System.Diagnostics.StackFrame",
					"System.Diagnostics.StackTrace",
					"System.Int16",
					"System.Int32",
					"System.Int64",
					"System.IO.Stream",
					"System.Reflection.Assembly",
					"System.Reflection.AssemblyName",
					"System.Reflection.MethodBase",
					"System.String",
					"System.Text.StringBuilder",
					"System.Type"
				);
				var olocals43 = CreateLocalsArray(
					"System.Int32"
				);
				if (otherMethods.Count == 1 &&
					decryptStringType.NestedTypes.Count == 3 &&
					DotNetUtils.IsMethod(otherMethods[0], "System.Void", "(System.Byte[],System.Int32,System.Byte[])") &&
					otherMethods[0].IsPrivate &&
					otherMethods[0].IsStatic &&
					new LocalTypes(otherMethods[0]).Exactly(olocals43) &&
					decryptStringMethod.IsNoInlining &&
					decryptStringMethod.IsAssembly &&
					!decryptStringMethod.IsSynchronized &&
					decryptStringMethod.Body.MaxStack >= 1 &&
					decryptStringMethod.Body.MaxStack <= 8 &&
					decryptStringMethod.Body.ExceptionHandlers.Count >= 2 &&
					new LocalTypes(decryptStringMethod).All(locals43) &&
					CheckTypeFields2(fields43)) {
					return "4.3 - 4.9";
				}

				/////////////////////////////////////////////////////////////////
				/////////////////////////////////////////////////////////////////
				/////////////////////////////////////////////////////////////////

				var fields50 = new string[] {
					GetNestedTypeName(0),
					GetNestedTypeName(1),
					"System.Byte[]",
					"System.Int16",
					"System.Int32",
					"System.Byte[]",
					"System.Int32",
					"System.Int32",
					GetNestedTypeName(2),
				};
				var locals50 = CreateLocalsArray(
					// GetNestedTypeName(2) // One of the nested types is the first local (non-enum type)
					"System.String",
					"System.String"
				);
				var otherMethod50 = otherMethods.Find((m) => {
					return DotNetUtils.IsMethod(m, "System.Void", "(System.Byte[],System.Int32,System.Byte[])");
				});
				decryptStringMethod = stringDecrypter.RealMethod;
				if (stringDecrypter.HasRealMethod &&
					otherMethods.Count == 2 &&
					otherMethod50 != null &&
					decryptStringType.NestedTypes.Count == 3 &&
					otherMethod50.IsPrivate &&
					otherMethod50.IsStatic &&
					decryptStringMethod.IsNoInlining &&
					decryptStringMethod.IsAssembly &&
					!decryptStringMethod.IsSynchronized &&
					decryptStringMethod.Body.MaxStack >= 1 &&
					decryptStringMethod.Body.MaxStack <= 8 &&
					decryptStringMethod.Body.ExceptionHandlers.Count == 1 &&
					new LocalTypes(decryptStringMethod).All(locals50) &&
					CheckTypeFields2(fields50)) {
					foreach (var inst in stringDecrypter.Method.Body.Instructions) {
						if (inst.OpCode.Code == Code.Cgt_Un)
							return "5.1";
					}
					return "5.0";
				}

				if (stringDecrypter.HasRealMethod &&
				    otherMethods.Count == 5 &&
				    otherMethod50 != null &&
				    decryptStringType.NestedTypes.Count == 3 &&
				    otherMethod50.IsPrivate &&
				    otherMethod50.IsStatic &&
				    decryptStringMethod.IsNoInlining &&
				    decryptStringMethod.IsAssembly &&
				    !decryptStringMethod.IsSynchronized &&
				    decryptStringMethod.Body.MaxStack >= 1 &&
				    decryptStringMethod.Body.MaxStack <= 8 &&
				    decryptStringMethod.Body.ExceptionHandlers.Count == 1) {
					return "5.2-5.8";
				}
			}

			return null;
		}

		static string FindEnumeratorName(MethodDef method) {
			foreach (var local in method.Body.Variables) {
				var gis = local.Type as GenericInstSig;
				if (gis == null)
					continue;
				if (gis.FullName == "System.Collections.Generic.IEnumerator`1<System.Int32>")
					continue;
				if (gis.GenericArguments.Count != 1)
					continue;
				if (gis.GenericArguments[0].GetFullName() != "System.Int32")
					continue;

				return gis.FullName;
			}
			return null;
		}

		TypeDef GetNestedType(int n) {
			var type = stringDecrypter.Type;

			if (n == 0) {
				foreach (var nested in type.NestedTypes) {
					if (nested.NestedTypes.Count == 1)
						return nested;
				}
			}
			else if (n == 1) {
				foreach (var nested in type.NestedTypes) {
					if (nested.IsEnum)
						continue;
					if (nested.NestedTypes.Count != 0)
						continue;
					return nested;
				}
			}
			else if (n == 2) {
				foreach (var nested in type.NestedTypes) {
					if (nested.IsEnum)
						return nested;
				}
			}
			return null;
		}

		string GetNestedTypeName(int n) {
			var nestedType = GetNestedType(n);
			return nestedType == null ? null : nestedType.FullName;
		}

		bool CheckTypeFields(string[] fieldTypes) {
			if (fieldTypes.Length != stringDecrypter.Type.Fields.Count)
				return false;
			for (int i = 0; i < fieldTypes.Length; i++) {
				if (fieldTypes[i] != stringDecrypter.Type.Fields[i].FieldType.FullName)
					return false;
			}
			return true;
		}

		bool CheckTypeFields2(string[] fieldTypes) {
			if (fieldTypes.Length != stringDecrypter.Type.Fields.Count)
				return false;

			var fieldTypes1 = new List<string>(fieldTypes);
			fieldTypes1.Sort();

			var fieldTypes2 = new List<string>();
			foreach (var f in stringDecrypter.Type.Fields)
				fieldTypes2.Add(f.FieldType.FullName);
			fieldTypes2.Sort();

			for (int i = 0; i < fieldTypes1.Count; i++) {
				if (fieldTypes1[i] != fieldTypes2[i])
					return false;
			}
			return true;
		}

		static Dictionary<string, bool> removeLocals_cf = new Dictionary<string, bool>(StringComparer.Ordinal) {
			{ "System.Diagnostics.StackFrame", true },
			{ "System.Diagnostics.StackTrace", true },
		};
		string[] CreateLocalsArray(params string[] locals) {
			Dictionary<string, bool> removeLocals = null;
			switch (frameworkType) {
			case FrameworkType.CompactFramework:
				removeLocals = removeLocals_cf;
				break;
			}
			if (removeLocals == null)
				return locals;

			var list = new List<string>(locals.Length);
			foreach (var s in locals) {
				if (!removeLocals.ContainsKey(s))
					list.Add(s);
			}
			return list.ToArray();
		}
	}
}
