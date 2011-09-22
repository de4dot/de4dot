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

using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.deobfuscators.SmartAssembly {
	class StringDecrypterInfo {
		ModuleDefinition module;
		ResourceDecrypter resourceDecrypter;
		TypeDefinition stringsEncodingClass;
		EmbeddedResource stringsResource;
		int stringOffset;
		TypeDefinition simpleZipType;
		MethodDefinition stringDecrypterMethod;

		public TypeDefinition GetStringDelegate { get; set; }
		public TypeDefinition StringsType { get; set; }
		public MethodDefinition CreateStringDelegateMethod { get; set; }

		public TypeDefinition StringsEncodingClass {
			get { return stringsEncodingClass; }
		}

		public bool CanDecrypt {
			get { return resourceDecrypter == null || resourceDecrypter.CanDecrypt; }
		}

		public TypeDefinition SimpleZipType {
			get { return simpleZipType; }
		}

		public EmbeddedResource StringsResource {
			get { return stringsResource; }
		}

		public int StringOffset {
			get { return stringOffset; }
		}

		public bool StringsEncrypted {
			get { return simpleZipType != null; }
		}

		public MethodDefinition StringDecrypterMethod {
			get { return stringDecrypterMethod; }
		}

		public StringDecrypterInfo(ModuleDefinition module, TypeDefinition stringsEncodingClass) {
			this.module = module;
			this.stringsEncodingClass = stringsEncodingClass;
		}

		public bool init(IDeobfuscator deob, ISimpleDeobfuscator simpleDeobfuscator) {
			var cctor = DotNetUtils.getMethod(stringsEncodingClass, ".cctor");
			if (cctor == null)
				throw new ApplicationException("Could not find .cctor");
			simpleDeobfuscator.deobfuscate(cctor);

			stringsResource = findStringResource(cctor);
			if (stringsResource == null) {
				simpleDeobfuscator.decryptStrings(cctor, deob);
				stringsResource = findStringResource(cctor);
				if (stringsResource == null)
					return false;
			}

			var offsetVal = findOffsetValue(cctor);
			if (offsetVal == null)
				throw new ApplicationException("Could not find string offset");
			stringOffset = offsetVal.Value;

			if (!findDecrypterMethod())
				throw new ApplicationException("Could not find string decrypter method");

			simpleZipType = findSimpleZipType(cctor);
			if (simpleZipType != null)
				resourceDecrypter = new ResourceDecrypter(new ResourceDecrypterInfo(module, simpleZipType, simpleDeobfuscator));

			return true;
		}

		public byte[] decrypt() {
			if (!CanDecrypt)
				throw new ApplicationException("Can't decrypt strings");
			return resourceDecrypter.decrypt(stringsResource);
		}

		// Find the embedded resource where all the strings are encrypted
		EmbeddedResource findStringResource(MethodDefinition method) {
			foreach (var ldstr in method.Body.Instructions) {
				if (ldstr.OpCode.Code != Code.Ldstr)
					continue;
				var s = ldstr.Operand as string;
				if (s == null)
					continue;
				var resource = DotNetUtils.getResource(module, s) as EmbeddedResource;
				if (resource != null)
					return resource;
			}
			return null;
		}

		// Find the string decrypter string offset value or null if none found
		int? findOffsetValue(MethodDefinition method) {
			var offsetField = findOffsetField(method);
			if (offsetField == null)
				return null;
			return findOffsetValue(method, offsetField);
		}

		FieldReference findOffsetField(MethodDefinition method) {
			var instructions = method.Body.Instructions;
			for (int i = 0; i <= instructions.Count - 2; i++) {
				var ldsfld = instructions[i];
				if (ldsfld.OpCode.Code != Code.Ldsfld)
					continue;
				var field = ldsfld.Operand as FieldReference;
				if (field == null || field.FieldType.FullName != "System.String")
					continue;
				if (!MemberReferenceHelper.compareTypes(stringsEncodingClass, field.DeclaringType))
					continue;

				var call = instructions[i + 1];
				if (call.OpCode.Code != Code.Call)
					continue;
				var calledMethod = call.Operand as MethodReference;
				if (!DotNetUtils.isMethod(calledMethod, "System.Int32", "(System.String)"))
					continue;

				return field;
			}

			return null;
		}

		int? findOffsetValue(MethodDefinition method, FieldReference offsetField) {
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
				var field = stsfld.Operand as FieldReference;
				if (field == null || !MemberReferenceHelper.compareFieldReference(offsetField, field))
					continue;

				int value;
				if (!int.TryParse(stringVal, System.Globalization.NumberStyles.Integer, null, out value))
					continue;

				return value;
			}

			return null;
		}

		bool findDecrypterMethod() {
			var methods = new List<MethodDefinition>(DotNetUtils.findMethods(stringsEncodingClass.Methods, "System.String", new string[] { "System.Int32" }));
			if (methods.Count != 1)
				return false;

			stringDecrypterMethod = methods[0];
			return true;
		}

		// Find SmartAssembly.Zip.SimpleZip, which is the class that decrypts and inflates
		// data in the resources.
		TypeDefinition findSimpleZipType(MethodDefinition method) {
			var instructions = method.Body.Instructions;
			for (int i = 0; i <= instructions.Count - 2; i++) {
				var call = instructions[i];
				if (call.OpCode.Code != Code.Call)
					continue;
				var calledMethod = call.Operand as MethodReference;
				if (!DotNetUtils.isMethod(calledMethod, "System.Byte[]", "(System.Byte[])"))
					continue;

				var stsfld = instructions[i + 1];
				if (stsfld.OpCode.Code != Code.Stsfld)
					continue;
				var field = stsfld.Operand as FieldReference;
				if (field == null || field.FieldType.FullName != "System.Byte[]")
					continue;
				if (!MemberReferenceHelper.compareTypes(stringsEncodingClass, field.DeclaringType))
					continue;

				var type = DotNetUtils.getType(module, calledMethod.DeclaringType);
				if (type == null)
					continue;

				return type;
			}

			return null;
		}

		public IEnumerable<FieldDefinition> getAllStringDelegateFields() {
			foreach (var type in module.GetTypes()) {
				foreach (var field in type.Fields) {
					if (field.FieldType == GetStringDelegate)
						yield return field;
				}
			}
		}

		public void removeInitCode(Blocks blocks) {
			if (CreateStringDelegateMethod == null)
				return;

			if (CreateStringDelegateMethod.Parameters.Count != 0)
				removeInitCode_v2(blocks);
			else
				removeInitCode_v1(blocks);
		}

		void removeInitCode_v1(Blocks blocks) {
			foreach (var block in blocks.MethodBlocks.getAllBlocks()) {
				var instructions = block.Instructions;
				for (int i = 0; i < instructions.Count; i++) {
					var call = instructions[i];
					if (call.OpCode != OpCodes.Call)
						continue;
					var method = call.Operand as MethodReference;
					if (!MemberReferenceHelper.compareMethodReferenceAndDeclaringType(method, CreateStringDelegateMethod))
						continue;

					block.remove(i, 1);
					break;
				}
			}
		}

		void removeInitCode_v2(Blocks blocks) {
			foreach (var block in blocks.MethodBlocks.getAllBlocks()) {
				var instructions = block.Instructions;
				for (int i = 0; i <= instructions.Count - 3; i++) {
					var ldtoken = instructions[i];
					if (ldtoken.OpCode != OpCodes.Ldtoken)
						continue;
					if (!MemberReferenceHelper.compareTypes(blocks.Method.DeclaringType, ldtoken.Operand as TypeReference))
						continue;

					var call1 = instructions[i + 1];
					if (call1.OpCode != OpCodes.Call)
						continue;
					var method1 = call1.Operand as MethodReference;
					if (method1 == null || method1.ToString() != "System.Type System.Type::GetTypeFromHandle(System.RuntimeTypeHandle)")
						continue;

					var call2 = instructions[i + 2];
					if (call2.OpCode != OpCodes.Call)
						continue;
					var method2 = call2.Operand as MethodReference;
					if (!MemberReferenceHelper.compareMethodReferenceAndDeclaringType(method2, CreateStringDelegateMethod))
						continue;

					block.remove(i, 3);
					break;
				}
			}
		}
	}
}
