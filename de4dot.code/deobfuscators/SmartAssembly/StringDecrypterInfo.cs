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
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.SmartAssembly {
	enum StringDecrypterVersion {
		V1,
		V2,
		V3,
		V4,
		Unknown,
	}

	class StringDecrypterInfo {
		ModuleDefMD module;
		ResourceDecrypter resourceDecrypter;
		TypeDef stringsEncodingClass;
		EmbeddedResource stringsResource;
		int stringOffset;
		MethodDef simpleZipTypeMethod;
		MethodDef stringDecrypterMethod;
		StringDecrypterVersion decrypterVersion;

		public StringDecrypterVersion DecrypterVersion {
			get { return decrypterVersion; }
		}

		public TypeDef GetStringDelegate { get; set; }
		public TypeDef StringsType { get; set; }
		public MethodDef CreateStringDelegateMethod { get; set; }

		public TypeDef StringsEncodingClass {
			get { return stringsEncodingClass; }
		}

		public bool CanDecrypt {
			get { return resourceDecrypter == null || resourceDecrypter.CanDecrypt; }
		}

		public MethodDef SimpleZipTypeMethod {
			get { return simpleZipTypeMethod; }
		}

		public EmbeddedResource StringsResource {
			get { return stringsResource; }
		}

		public int StringOffset {
			get { return stringOffset; }
		}

		public bool StringsEncrypted {
			get { return simpleZipTypeMethod != null; }
		}

		public MethodDef StringDecrypterMethod {
			get { return stringDecrypterMethod; }
		}

		public StringDecrypterInfo(ModuleDefMD module, TypeDef stringsEncodingClass) {
			this.module = module;
			this.stringsEncodingClass = stringsEncodingClass;
		}

		static string[] fields2x = new string[] {
			"System.IO.Stream",
			"System.Int32",
		};
		static string[] fields3x = new string[] {
			"System.Byte[]",
			"System.Int32",
		};
		StringDecrypterVersion GuessVersion(MethodDef cctor) {
			var fieldTypes = new FieldTypes(stringsEncodingClass);
			if (fieldTypes.Exactly(fields2x))
				return StringDecrypterVersion.V2;
			if (cctor == null)
				return StringDecrypterVersion.V1;
			if (fieldTypes.Exactly(fields3x))
				return StringDecrypterVersion.V3;
			return StringDecrypterVersion.Unknown;
		}

		public bool Initialize(IDeobfuscator deob, ISimpleDeobfuscator simpleDeobfuscator) {
			var cctor = stringsEncodingClass.FindStaticConstructor();
			if (cctor != null)
				simpleDeobfuscator.Deobfuscate(cctor);

			decrypterVersion = GuessVersion(cctor);

			if (!FindDecrypterMethod())
				throw new ApplicationException("Could not find string decrypter method");

			if (!FindStringsResource(deob, simpleDeobfuscator, cctor))
				return false;

			if (decrypterVersion <= StringDecrypterVersion.V3) {
				MethodDef initMethod;
				if (decrypterVersion == StringDecrypterVersion.V3)
					initMethod = cctor;
				else if (decrypterVersion == StringDecrypterVersion.V2)
					initMethod = stringDecrypterMethod;
				else
					initMethod = stringDecrypterMethod;

				stringOffset = 0;
				if (decrypterVersion != StringDecrypterVersion.V1) {
					if (CallsGetPublicKeyToken(initMethod)) {
						var pkt = PublicKeyBase.ToPublicKeyToken(module.Assembly.PublicKeyToken);
						if (!PublicKeyBase.IsNullOrEmpty2(pkt)) {
							for (int i = 0; i < pkt.Data.Length - 1; i += 2)
								stringOffset ^= ((int)pkt.Data[i] << 8) + pkt.Data[i + 1];
						}
					}

					if (DeobUtils.HasInteger(initMethod, 0xFFFFFF) &&
						DeobUtils.HasInteger(initMethod, 0xFFFF)) {
						stringOffset ^= ((stringDecrypterMethod.MDToken.ToInt32() & 0xFFFFFF) - 1) % 0xFFFF;
					}
				}
			}
			else {
				var offsetVal = FindOffsetValue(cctor);
				if (offsetVal == null)
					throw new ApplicationException("Could not find string offset");
				stringOffset = offsetVal.Value;
				decrypterVersion = StringDecrypterVersion.V4;
			}

			simpleZipTypeMethod = FindSimpleZipTypeMethod(cctor) ?? FindSimpleZipTypeMethod(stringDecrypterMethod);
			if (simpleZipTypeMethod != null)
				resourceDecrypter = new ResourceDecrypter(new ResourceDecrypterInfo(module, simpleZipTypeMethod, simpleDeobfuscator));

			return true;
		}

		bool CallsGetPublicKeyToken(MethodDef method) {
			foreach (var calledMethod in DotNetUtils.GetMethodCalls(method)) {
				if (calledMethod.ToString() == "System.Byte[] System.Reflection.AssemblyName::GetPublicKeyToken()")
					return true;
			}
			return false;
		}

		bool FindStringsResource(IDeobfuscator deob, ISimpleDeobfuscator simpleDeobfuscator, MethodDef cctor) {
			if (stringsResource != null)
				return true;

			if (decrypterVersion <= StringDecrypterVersion.V3) {
				stringsResource = DotNetUtils.GetResource(module, (module.Mvid ?? Guid.NewGuid()).ToString("B")) as EmbeddedResource;
				if (stringsResource != null)
					return true;
			}

			if (FindStringsResource2(deob, simpleDeobfuscator, cctor))
				return true;
			if (FindStringsResource2(deob, simpleDeobfuscator, stringDecrypterMethod))
				return true;

			return false;
		}

		bool FindStringsResource2(IDeobfuscator deob, ISimpleDeobfuscator simpleDeobfuscator, MethodDef initMethod) {
			if (initMethod == null)
				return false;

			stringsResource = FindStringResource(initMethod);
			if (stringsResource != null)
				return true;

			simpleDeobfuscator.DecryptStrings(initMethod, deob);
			stringsResource = FindStringResource(initMethod);
			if (stringsResource != null)
				return true;

			return false;
		}

		public byte[] Decrypt() {
			if (!CanDecrypt)
				throw new ApplicationException("Can't decrypt strings");
			return resourceDecrypter.Decrypt(stringsResource);
		}

		// Find the embedded resource where all the strings are encrypted
		EmbeddedResource FindStringResource(MethodDef method) {
			foreach (var s in DotNetUtils.GetCodeStrings(method)) {
				if (s == null)
					continue;
				var resource = DotNetUtils.GetResource(module, s) as EmbeddedResource;
				if (resource != null)
					return resource;
			}
			return null;
		}

		// Find the string decrypter string offset value or null if none found
		int? FindOffsetValue(MethodDef method) {
			var fieldDict = new FieldDefAndDeclaringTypeDict<IField>();
			foreach (var field in method.DeclaringType.Fields)
				fieldDict.Add(field, field);

			var offsetField = FindOffsetField(method);
			if (offsetField == null)
				return null;

			return FindOffsetValue(method, (FieldDef)fieldDict.Find(offsetField), fieldDict);
		}

		IField FindOffsetField(MethodDef method) {
			var instructions = method.Body.Instructions;
			for (int i = 0; i <= instructions.Count - 2; i++) {
				var ldsfld = instructions[i];
				if (ldsfld.OpCode.Code != Code.Ldsfld)
					continue;
				var field = ldsfld.Operand as IField;
				if (field == null || field.FieldSig.GetFieldType().GetElementType() != ElementType.String)
					continue;
				if (!new SigComparer().Equals(stringsEncodingClass, field.DeclaringType))
					continue;

				var call = instructions[i + 1];
				if (call.OpCode.Code != Code.Call)
					continue;
				var calledMethod = call.Operand as IMethod;
				if (!DotNetUtils.IsMethod(calledMethod, "System.Int32", "(System.String)"))
					continue;

				return field;
			}

			return null;
		}

		int? FindOffsetValue(MethodDef method, FieldDef offsetField, FieldDefAndDeclaringTypeDict<IField> fields) {
			var instructions = method.Body.Instructions;
			for (int i = 0; i <= instructions.Count - 2; i++) {
				var ldstr = instructions[i];
				if (ldstr.OpCode.Code != Code.Ldstr)
					continue;
				var stringVal = ldstr.Operand as string;
				if (stringVal == null)
					continue;

				var stsfld = instructions[i + 1];
				if (stsfld.OpCode.Code != Code.Stsfld)
					continue;
				var field = stsfld.Operand as IField;
				if (field == null || fields.Find(field) != offsetField)
					continue;

				int value;
				if (!int.TryParse(stringVal, System.Globalization.NumberStyles.Integer, null, out value))
					continue;

				return value;
			}

			return null;
		}

		bool FindDecrypterMethod() {
			if (stringDecrypterMethod != null)
				return true;

			var methods = new List<MethodDef>(DotNetUtils.FindMethods(stringsEncodingClass.Methods, "System.String", new string[] { "System.Int32" }));
			if (methods.Count != 1)
				return false;

			stringDecrypterMethod = methods[0];
			return true;
		}

		MethodDef FindSimpleZipTypeMethod(MethodDef method) {
			if (method == null || method.Body == null)
				return null;
			var instructions = method.Body.Instructions;
			for (int i = 0; i <= instructions.Count - 2; i++) {
				var call = instructions[i];
				if (call.OpCode.Code != Code.Call)
					continue;
				var calledMethod = call.Operand as MethodDef;
				if (calledMethod == null)
					continue;
				if (!DotNetUtils.IsMethod(calledMethod, "System.Byte[]", "(System.Byte[])"))
					continue;

				var stsfld = instructions[i + 1];
				if (stsfld.OpCode.Code != Code.Stsfld)
					continue;
				var field = stsfld.Operand as IField;
				if (field == null || field.FieldSig.GetFieldType().GetFullName() != "System.Byte[]")
					continue;
				if (!new SigComparer().Equals(stringsEncodingClass, field.DeclaringType))
					continue;

				return calledMethod;
			}

			return null;
		}

		public IEnumerable<FieldDef> GetAllStringDelegateFields() {
			if (GetStringDelegate == null)
				yield break;
			foreach (var type in module.GetTypes()) {
				foreach (var field in type.Fields) {
					if (field.FieldType.TryGetTypeDef() == GetStringDelegate)
						yield return field;
				}
			}
		}

		public void RemoveInitCode(Blocks blocks) {
			if (CreateStringDelegateMethod == null)
				return;

			if (CreateStringDelegateMethod.Parameters.Count != 0)
				RemoveInitCode_v2(blocks);
			else
				RemoveInitCode_v1(blocks);
		}

		void RemoveInitCode_v1(Blocks blocks) {
			foreach (var block in blocks.MethodBlocks.GetAllBlocks()) {
				var instructions = block.Instructions;
				for (int i = 0; i < instructions.Count; i++) {
					var call = instructions[i];
					if (call.OpCode != OpCodes.Call)
						continue;
					var method = call.Operand as IMethod;
					if (!MethodEqualityComparer.CompareDeclaringTypes.Equals(method, CreateStringDelegateMethod))
						continue;

					block.Remove(i, 1);
					break;
				}
			}
		}

		void RemoveInitCode_v2(Blocks blocks) {
			foreach (var block in blocks.MethodBlocks.GetAllBlocks()) {
				var instructions = block.Instructions;
				for (int i = 0; i <= instructions.Count - 3; i++) {
					var ldtoken = instructions[i];
					if (ldtoken.OpCode != OpCodes.Ldtoken)
						continue;
					if (!new SigComparer().Equals(blocks.Method.DeclaringType, ldtoken.Operand as ITypeDefOrRef))
						continue;

					var call1 = instructions[i + 1];
					if (call1.OpCode != OpCodes.Call)
						continue;
					var method1 = call1.Operand as IMethod;
					if (method1 == null || method1.ToString() != "System.Type System.Type::GetTypeFromHandle(System.RuntimeTypeHandle)")
						continue;

					var call2 = instructions[i + 2];
					if (call2.OpCode != OpCodes.Call)
						continue;
					var method2 = call2.Operand as IMethod;
					if (!MethodEqualityComparer.CompareDeclaringTypes.Equals(method2, CreateStringDelegateMethod))
						continue;

					block.Remove(i, 3);
					break;
				}
			}
		}
	}
}
