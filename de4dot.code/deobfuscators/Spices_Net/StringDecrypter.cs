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
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Metadata;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Spices_Net {
	class StringDecrypter {
		ModuleDefinition module;
		TypeDefinition decrypterType;
		FieldDefinition encryptedDataField;
		StringDataFlags stringDataFlags;
		MethodDefinitionAndDeclaringTypeDict<DecrypterInfo> methodToInfo = new MethodDefinitionAndDeclaringTypeDict<DecrypterInfo>();
		byte[] decryptedData;
		byte[] key;
		byte[] iv;

		[Flags]
		enum StringDataFlags {
			Compressed = 0x1,
			Encrypted1 = 0x2,
			Encrypted2 = 0x4,
			Encrypted3DES = 0x8,
		}

		public class DecrypterInfo {
			public MethodDefinition method;
			public int offset;
			public int length;

			public DecrypterInfo(MethodDefinition method, int offset, int length) {
				this.method = method;
				this.offset = offset;
				this.length = length;
			}
		}

		public TypeDefinition EncryptedStringsType {
			get {
				if (encryptedDataField == null)
					return null;
				var type = encryptedDataField.FieldType as TypeDefinition;
				if (type == null || type.Fields.Count != 1 || type.Fields[0] != encryptedDataField)
					return null;
				if (type.HasMethods || type.HasEvents || type.HasProperties || type.HasNestedTypes)
					return null;
				if (type.Interfaces.Count > 0)
					return null;

				return type;
			}
		}

		public TypeDefinition Type {
			get { return decrypterType; }
		}

		public bool Detected {
			get { return decrypterType != null; }
		}

		public IEnumerable<DecrypterInfo> DecrypterInfos {
			get { return methodToInfo.getValues(); }
		}

		public StringDecrypter(ModuleDefinition module) {
			this.module = module;
		}

		public void find() {
			foreach (var type in module.Types) {
				if (type.HasNestedTypes || type.HasInterfaces)
					continue;
				if (type.HasEvents || type.HasProperties)
					continue;
				if (type.Fields.Count != 2)
					continue;
				if ((type.Attributes & ~TypeAttributes.Sealed) != 0)
					continue;
				if (type.BaseType == null || type.BaseType.FullName != "System.Object")
					continue;
				if (hasInstanceMethods(type))
					continue;
				var cctor = DotNetUtils.getMethod(type, ".cctor");
				if (cctor == null)
					continue;

				FieldDefinition encryptedDataFieldTmp;
				StringDataFlags stringDataFlagsTmp;
				if (!checkCctor(cctor, out encryptedDataFieldTmp, out stringDataFlagsTmp))
					continue;

				if (!initializeDecrypterInfos(type))
					continue;

				encryptedDataField = encryptedDataFieldTmp;
				stringDataFlags = stringDataFlagsTmp;
				decrypterType = type;
				return;
			}
		}

		static bool hasInstanceMethods(TypeDefinition type) {
			foreach (var method in type.Methods) {
				if (!method.IsStatic)
					return true;
				if (method.PInvokeInfo != null)
					return true;
			}
			return false;
		}

		bool checkCctor(MethodDefinition cctor, out FieldDefinition compressedDataField, out StringDataFlags flags) {
			flags = 0;
			var instructions = cctor.Body.Instructions;
			for (int i = 0; i < instructions.Count; i++) {
				var ldci4 = instructions[i];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;

				var instrs = DotNetUtils.getInstructions(instructions, i + 1, OpCodes.Newarr, OpCodes.Dup, OpCodes.Ldtoken, OpCodes.Call);
				if (instrs == null)
					continue;

				var newarr = instrs[0];
				if (newarr.Operand.ToString() != "System.Byte")
					continue;

				var field = instrs[2].Operand as FieldDefinition;
				if (field == null || field.InitialValue == null || field.InitialValue.Length == 0)
					continue;

				int index = i + 1 + instrs.Count;
				if (index < instructions.Count && instructions[index].OpCode.Code == Code.Call)
					flags = getStringDataFlags(instructions[index].Operand as MethodDefinition);

				compressedDataField = field;
				return true;
			}

			compressedDataField = null;
			return false;
		}

		StringDataFlags getStringDataFlags(MethodDefinition method) {
			if (method == null || method.Body == null)
				return 0;
			if (method.Parameters.Count != 1)
				return 0;
			if (!checkClass(method.Parameters[0].ParameterType, "System.Byte[]"))
				return 0;
			if (!checkClass(method.MethodReturnType.ReturnType, "System.Byte[]"))
				return 0;

			StringDataFlags flags = 0;

			if (hasInstruction(method, Code.Not))
				flags |= StringDataFlags.Encrypted2;
			else if (hasInstruction(method, Code.Xor))
				flags |= StringDataFlags.Encrypted1;
			else if (check3DesCreator(method))
				flags |= StringDataFlags.Encrypted3DES;
			if (callsDecompressor(method))
				flags |= StringDataFlags.Compressed;

			return flags;
		}

		bool check3DesCreator(MethodDefinition method) {
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call)
					continue;
				var calledMethod = instr.Operand as MethodDefinition;
				if (calledMethod == null)
					continue;
				if (calledMethod.MethodReturnType.ReturnType.EType == ElementType.Void)
					continue;
				if (calledMethod.Parameters.Count != 0)
					continue;
				if (!get3DesKeyIv(calledMethod, ref key, ref iv))
					continue;

				return true;
			}
			return false;
		}

		bool get3DesKeyIv(MethodDefinition method, ref byte[] key, ref byte[] iv) {
			if (!new LocalTypes(method).exists("System.Security.Cryptography.TripleDESCryptoServiceProvider"))
				return false;

			var instrs = method.Body.Instructions;
			var arrays = ArrayFinder.getArrays(method, module.TypeSystem.Byte);
			if (arrays.Count != 1 && arrays.Count != 2)
				return false;

			key = arrays[0];
			if (arrays.Count == 1)
				iv = module.Assembly.Name.PublicKeyToken;
			else
				iv = arrays[1];
			return true;
		}

		static bool callsDecompressor(MethodDefinition method) {
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call)
					continue;
				var called = instr.Operand as MethodDefinition;
				if (called == null)
					continue;
				if (called.MethodReturnType.ReturnType.EType != ElementType.I4)
					continue;
				var parameters = called.Parameters;
				if (parameters.Count != 4)
					continue;
				if (!checkClass(parameters[0].ParameterType, "System.Byte[]"))
					continue;
				if (parameters[1].ParameterType.EType != ElementType.I4)
					continue;
				if (!checkClass(parameters[2].ParameterType, "System.Byte[]"))
					continue;
				if (parameters[3].ParameterType.EType != ElementType.I4)
					continue;

				return true;
			}
			return false;
		}

		static bool hasInstruction(MethodDefinition method, Code code) {
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code == code)
					return true;
			}
			return false;
		}

		static bool checkClass(TypeReference type, string fullName) {
			return type != null && (type.EType == ElementType.Object || type.FullName == fullName);
		}

		static bool isStringType(TypeReference type) {
			return type != null && (type.EType == ElementType.Object || type.EType == ElementType.String);
		}

		bool initializeDecrypterInfos(TypeDefinition type) {
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (method.Parameters.Count != 0)
					continue;
				if (!isStringType(method.MethodReturnType.ReturnType))
					continue;

				var info = createInfo(method);
				if (info == null)
					continue;

				methodToInfo.add(method, info);
			}

			return methodToInfo.Count != 0;
		}

		DecrypterInfo createInfo(MethodDefinition method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var ldci4_1 = instrs[i];
				var ldci4_2 = instrs[i + 1];
				if (!DotNetUtils.isLdcI4(ldci4_1) || !DotNetUtils.isLdcI4(ldci4_2))
					continue;

				int offset = DotNetUtils.getLdcI4Value(ldci4_1);
				int length = DotNetUtils.getLdcI4Value(ldci4_2);
				return new DecrypterInfo(method, offset, length);
			}

			return null;
		}

		public void initialize() {
			if (decrypterType == null)
				return;

			decryptedData = new byte[encryptedDataField.InitialValue.Length];
			Array.Copy(encryptedDataField.InitialValue, 0, decryptedData, 0, decryptedData.Length);

			if ((stringDataFlags & StringDataFlags.Encrypted1) != 0) {
				for (int i = 0; i < decryptedData.Length; i++)
					decryptedData[i] ^= (byte)i;
			}

			if ((stringDataFlags & StringDataFlags.Encrypted2) != 0) {
				var k = module.Assembly.Name.PublicKey;
				int mask = (byte)(~k.Length);
				for (int i = 0; i < decryptedData.Length; i++)
					decryptedData[i] ^= k[i & mask];
			}

			if ((stringDataFlags & StringDataFlags.Encrypted3DES) != 0)
				decryptedData = DeobUtils.des3Decrypt(decryptedData, key, iv);

			if ((stringDataFlags & StringDataFlags.Compressed) != 0)
				decryptedData = QclzDecompressor.decompress(decryptedData);
		}

		public void cleanUp() {
			if (decrypterType == null)
				return;

			encryptedDataField.InitialValue = new byte[1];
			encryptedDataField.FieldType = module.TypeSystem.Byte;
		}

		public string decrypt(MethodDefinition method) {
			var info = methodToInfo.find(method);
			return Encoding.Unicode.GetString(decryptedData, info.offset, info.length);
		}
	}
}
