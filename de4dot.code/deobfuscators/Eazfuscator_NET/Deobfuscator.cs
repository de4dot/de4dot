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
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Eazfuscator_NET {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "Eazfuscator.NET";
		public const string THE_TYPE = "ef";
		const string DEFAULT_REGEX = @"!^#=&!^dje_.+_ejd$&" + DeobfuscatorBase.DEFAULT_VALID_NAME_REGEX;
		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
		}

		public override string Name {
			get { return THE_NAME; }
		}

		public override string Type {
			get { return THE_TYPE; }
		}

		public override IDeobfuscator createDeobfuscator() {
			return new Deobfuscator(new Deobfuscator.Options {
				ValidNameRegex = validNameRegex.get(),
			});
		}
	}

	class Deobfuscator : DeobfuscatorBase {
		Options options;
		TypeDefinition decryptStringType;
		MethodDefinition decryptStringMethod;
		string obfuscatorName = DeobfuscatorInfo.THE_NAME;
		bool detectedVersion = false;

		internal class Options : OptionsBase {
		}

		public override string Type {
			get { return DeobfuscatorInfo.THE_TYPE; }
		}

		public override string TypeLong {
			get { return DeobfuscatorInfo.THE_NAME; }
		}

		public override string Name {
			get { return obfuscatorName; }
		}

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;
			DefaultDecrypterType = DecrypterType.Emulate;
		}

		protected override int detectInternal() {
			int val = 0;

			if (decryptStringMethod != null)
				val += 100;
			if (detectedVersion)
				val += 10;

			return val;
		}

		protected override void scanForObfuscator() {
			findStringDecrypterMethod();
			if (decryptStringType != null)
				detectVersion();
		}

		void findStringDecrypterMethod() {
			foreach (var type in module.Types) {
				if (DotNetUtils.findFieldType(type, "System.IO.BinaryReader", true) == null)
					continue;
				if (DotNetUtils.findFieldType(type, "System.Collections.Generic.Dictionary`2<System.Int32,System.String>", true) == null)
					continue;

				foreach (var method in type.Methods) {
					if (method.IsStatic && method.HasBody && method.MethodReturnType.ReturnType.FullName == "System.String" &&
						method.Parameters.Count == 1 && method.Parameters[0].ParameterType.FullName == "System.Int32") {
						foreach (var instr in method.Body.Instructions) {
							if (instr.OpCode != OpCodes.Callvirt)
								continue;
							var calledMethod = instr.Operand as MethodReference;
							if (calledMethod != null && calledMethod.FullName == "System.IO.Stream System.Reflection.Assembly::GetManifestResourceStream(System.String)") {
								decryptStringType = type;
								decryptStringMethod = method;
								return;
							}
						}
					}
				}
			}
		}

		void detectVersion() {
			var name = detectVersion2();
			if (name == null)
				return;

			detectedVersion = true;
			obfuscatorName = DeobfuscatorInfo.THE_NAME + " " +  name;
		}

		string detectVersion2() {
			var otherMethods = new List<MethodDefinition>();
			MethodDefinition cctor = null;
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
			var locals11 = new string[] {
				"System.Boolean",
				"System.Byte[]",
				"System.Char[]",
				"System.Int16",
				"System.Int32",
				"System.Reflection.Assembly",
				"System.String",
			};
			if (otherMethods.Count == 0 &&
				!decryptStringMethod.NoInlining &&
				decryptStringMethod.IsPublic &&
				decryptStringMethod.IsSynchronized &&
				decryptStringMethod.Body.MaxStackSize >= 35 &&
				decryptStringMethod.Body.MaxStackSize <= 50 &&
				decryptStringMethod.Body.ExceptionHandlers.Count == 0 &&
				new LocalTypes(decryptStringMethod).exactly(locals11) &&
				checkTypeFields(fields11)) {
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
			var locals13 = new string[] {
				"System.Boolean",
				"System.Byte",
				"System.Byte[]",
				"System.Char[]",
				"System.Int16",
				"System.Int32",
				"System.Reflection.Assembly",
				"System.String",
			};
			if (otherMethods.Count == 0 &&
				!decryptStringMethod.NoInlining &&
				decryptStringMethod.IsPublic &&
				decryptStringMethod.IsSynchronized &&
				decryptStringMethod.Body.MaxStackSize >= 35 &&
				decryptStringMethod.Body.MaxStackSize <= 50 &&
				decryptStringMethod.Body.ExceptionHandlers.Count == 0 &&
				new LocalTypes(decryptStringMethod).exactly(locals13) &&
				checkTypeFields(fields13)) {
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
			var locals14 = new string[] {
				"System.Boolean",
				"System.Byte",
				"System.Byte[]",
				"System.Char[]",
				"System.Int16",
				"System.Int32",
				"System.Reflection.Assembly",
				"System.String",
			};
			if (otherMethods.Count == 0 &&
				!decryptStringMethod.NoInlining &&
				decryptStringMethod.IsPublic &&
				decryptStringMethod.IsSynchronized &&
				decryptStringMethod.Body.MaxStackSize >= 150 &&
				decryptStringMethod.Body.MaxStackSize <= 200 &&
				decryptStringMethod.Body.ExceptionHandlers.Count == 0 &&
				new LocalTypes(decryptStringMethod).exactly(locals14) &&
				checkTypeFields(fields14)) {
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
			var locals24 = new string[] {
				"System.Boolean",
				"System.Byte",
				"System.Byte[]",
				"System.Char[]",
				"System.Int16",
				"System.Int32",
				"System.Reflection.Assembly",
				"System.Reflection.AssemblyName",
				"System.String",
			};
			if (otherMethods.Count == 0 &&
				!decryptStringMethod.NoInlining &&
				decryptStringMethod.IsPublic &&
				decryptStringMethod.IsSynchronized &&
				decryptStringMethod.Body.MaxStackSize >= 1 &&
				decryptStringMethod.Body.MaxStackSize <= 8 &&
				decryptStringMethod.Body.ExceptionHandlers.Count == 0 &&
				new LocalTypes(decryptStringMethod).exactly(locals24) &&
				checkTypeFields(fields24)) {
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
			var locals26 = new string[] {
				"System.Boolean",
				"System.Byte",
				"System.Byte[]",
				"System.Char[]",
				"System.Collections.Generic.Dictionary`2<System.Int32,System.String>",
				"System.Int16",
				"System.Int32",
				"System.Reflection.Assembly",
				"System.Reflection.AssemblyName",
				"System.String",
			};
			if (otherMethods.Count == 0 &&
				!decryptStringMethod.NoInlining &&
				decryptStringMethod.IsPublic &&
				!decryptStringMethod.IsSynchronized &&
				decryptStringMethod.Body.MaxStackSize >= 1 &&
				decryptStringMethod.Body.MaxStackSize <= 8 &&
				decryptStringMethod.Body.ExceptionHandlers.Count == 1 &&
				new LocalTypes(decryptStringMethod).exactly(locals26) &&
				checkTypeFields(fields26)) {
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
			var locals27 = new string[] {
				"System.Boolean",
				"System.Byte",
				"System.Byte[]",
				"System.Char[]",
				"System.Collections.Generic.Dictionary`2<System.Int32,System.String>",
				"System.Int16",
				"System.Int32",
				"System.Reflection.Assembly",
				"System.Reflection.AssemblyName",
				"System.String",
			};
			if (otherMethods.Count == 0 &&
				decryptStringMethod.NoInlining &&
				decryptStringMethod.IsPublic &&
				!decryptStringMethod.IsSynchronized &&
				decryptStringMethod.Body.MaxStackSize >= 1 &&
				decryptStringMethod.Body.MaxStackSize <= 8 &&
				decryptStringMethod.Body.ExceptionHandlers.Count == 1 &&
				new LocalTypes(decryptStringMethod).exactly(locals27) &&
				checkTypeFields(fields27)) {
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
			var locals28 = new string[] {
				"System.Boolean",
				"System.Byte",
				"System.Byte[]",
				"System.Char[]",
				"System.Collections.Generic.Dictionary`2<System.Int32,System.String>",
				"System.Int16",
				"System.Int32",
				"System.Reflection.Assembly",
				"System.Reflection.AssemblyName",
				"System.String",
			};
			if (otherMethods.Count == 0 &&
				decryptStringMethod.NoInlining &&
				decryptStringMethod.IsAssembly &&
				!decryptStringMethod.IsSynchronized &&
				decryptStringMethod.Body.MaxStackSize >= 1 &&
				decryptStringMethod.Body.MaxStackSize <= 8 &&
				decryptStringMethod.Body.ExceptionHandlers.Count == 1 &&
				new LocalTypes(decryptStringMethod).exactly(locals28) &&
				checkTypeFields(fields28)) {
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
			var locals29 = new string[] {
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
				"System.Type",
			};
			if (otherMethods.Count == 0 &&
				decryptStringMethod.NoInlining &&
				decryptStringMethod.IsAssembly &&
				!decryptStringMethod.IsSynchronized &&
				decryptStringMethod.Body.MaxStackSize >= 1 &&
				decryptStringMethod.Body.MaxStackSize <= 8 &&
				decryptStringMethod.Body.ExceptionHandlers.Count == 2 &&
				new LocalTypes(decryptStringMethod).exactly(locals29) &&
				checkTypeFields(fields29)) {
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
			var locals30 = new string[] {
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
				"System.Type",
			};
			var olocals30 = new string[] {
				"System.Int32",
			};
			if (otherMethods.Count == 1 &&
				DotNetUtils.isMethod(otherMethods[0], "System.Int32", "(System.Byte[],System.Int32,System.Byte[])") &&
				otherMethods[0].IsPrivate &&
				otherMethods[0].IsStatic &&
				new LocalTypes(otherMethods[0]).exactly(olocals30) &&
				decryptStringMethod.NoInlining &&
				decryptStringMethod.IsAssembly &&
				!decryptStringMethod.IsSynchronized &&
				decryptStringMethod.Body.MaxStackSize >= 1 &&
				decryptStringMethod.Body.MaxStackSize <= 8 &&
				decryptStringMethod.Body.ExceptionHandlers.Count == 2 &&
				new LocalTypes(decryptStringMethod).exactly(locals30) &&
				checkTypeFields(fields30)) {
				return "3.0 - 3.1";
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
			var locals32 = new string[] {
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
				"System.Type",
			};
			var olocals32 = new string[] {
				"System.Int32",
			};
			if (otherMethods.Count == 1 &&
				DotNetUtils.isMethod(otherMethods[0], "System.Void", "(System.Byte[],System.Int32,System.Byte[])") &&
				otherMethods[0].IsPrivate &&
				otherMethods[0].IsStatic &&
				new LocalTypes(otherMethods[0]).exactly(olocals32) &&
				decryptStringMethod.NoInlining &&
				decryptStringMethod.IsAssembly &&
				!decryptStringMethod.IsSynchronized &&
				decryptStringMethod.Body.MaxStackSize >= 1 &&
				decryptStringMethod.Body.MaxStackSize <= 8 &&
				decryptStringMethod.Body.ExceptionHandlers.Count == 2 &&
				new LocalTypes(decryptStringMethod).exactly(locals32) &&
				checkTypeFields(fields32)) {
				return "3.2";
			}

			return null;
		}

		bool checkTypeFields(string[] fieldTypes) {
			if (fieldTypes.Length != decryptStringType.Fields.Count)
				return false;
			for (int i = 0; i < fieldTypes.Length; i++) {
				if (fieldTypes[i] != decryptStringType.Fields[i].FieldType.FullName)
					return false;
			}
			return true;
		}

		public override void deobfuscateEnd() {
			if (Operations.DecryptStrings == OpDecryptString.Dynamic) {
				addTypeToBeRemoved(decryptStringType, "String decrypter type");
				findPossibleNamesToRemove(decryptStringMethod);
				addResources("Encrypted strings");
			}

			base.deobfuscateEnd();
		}

		public override IEnumerable<string> getStringDecrypterMethods() {
			var list = new List<string>();
			if (decryptStringMethod != null)
				list.Add(decryptStringMethod.MetadataToken.ToInt32().ToString("X8"));
			return list;
		}
	}
}
