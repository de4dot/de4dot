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
using System.Text;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Spices_Net {
	class StringDecrypter {
		ModuleDefMD module;
		TypeDef decrypterType;
		FieldDef encryptedDataField;
		StringDataFlags stringDataFlags;
		MethodDefAndDeclaringTypeDict<DecrypterInfo> methodToInfo = new MethodDefAndDeclaringTypeDict<DecrypterInfo>();
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
			public MethodDef method;
			public int offset;
			public int length;

			public DecrypterInfo(MethodDef method, int offset, int length) {
				this.method = method;
				this.offset = offset;
				this.length = length;
			}
		}

		public TypeDef EncryptedStringsType {
			get {
				if (encryptedDataField == null)
					return null;
				var type = encryptedDataField.FieldSig.GetFieldType().TryGetTypeDef();
				if (type == null || type.Fields.Count != 1 || type.Fields[0] != encryptedDataField)
					return null;
				if (type.HasMethods || type.HasEvents || type.HasProperties || type.HasNestedTypes)
					return null;
				if (type.Interfaces.Count > 0)
					return null;

				return type;
			}
		}

		public TypeDef Type {
			get { return decrypterType; }
		}

		public bool Detected {
			get { return decrypterType != null; }
		}

		public IEnumerable<DecrypterInfo> DecrypterInfos {
			get { return methodToInfo.GetValues(); }
		}

		public StringDecrypter(ModuleDefMD module) {
			this.module = module;
		}

		public void Find() {
			foreach (var type in module.Types) {
				if (type.HasNestedTypes || type.HasInterfaces)
					continue;
				if (type.HasEvents || type.HasProperties)
					continue;
				if (type.Fields.Count < 2 || type.Fields.Count > 3)
					continue;
				if ((type.Attributes & ~TypeAttributes.Sealed) != 0)
					continue;
				if (type.BaseType == null || type.BaseType.FullName != "System.Object")
					continue;
				if (HasInstanceMethods(type))
					continue;
				var cctor = type.FindStaticConstructor();
				if (cctor == null)
					continue;

				FieldDef encryptedDataFieldTmp;
				StringDataFlags stringDataFlagsTmp;
				if (!CheckCctor(cctor, out encryptedDataFieldTmp, out stringDataFlagsTmp))
					continue;

				if (!InitializeDecrypterInfos(type))
					continue;

				encryptedDataField = encryptedDataFieldTmp;
				stringDataFlags = stringDataFlagsTmp;
				decrypterType = type;
				return;
			}
		}

		static bool HasInstanceMethods(TypeDef type) {
			foreach (var method in type.Methods) {
				if (!method.IsStatic)
					return true;
				if (method.ImplMap != null)
					return true;
			}
			return false;
		}

		bool CheckCctor(MethodDef cctor, out FieldDef compressedDataField, out StringDataFlags flags) {
			flags = 0;
			var instructions = cctor.Body.Instructions;
			for (int i = 0; i < instructions.Count; i++) {
				var ldci4 = instructions[i];
				if (!ldci4.IsLdcI4())
					continue;

				var instrs = DotNetUtils.GetInstructions(instructions, i + 1, OpCodes.Newarr, OpCodes.Dup, OpCodes.Ldtoken, OpCodes.Call);
				if (instrs == null)
					continue;

				var newarr = instrs[0];
				if (newarr.Operand.ToString() != "System.Byte")
					continue;

				var field = instrs[2].Operand as FieldDef;
				if (field == null || field.InitialValue == null || field.InitialValue.Length == 0)
					continue;

				int index = i + 1 + instrs.Count;
				if (index < instructions.Count && instructions[index].OpCode.Code == Code.Call)
					flags = GetStringDataFlags(instructions[index].Operand as MethodDef);

				compressedDataField = field;
				return true;
			}

			compressedDataField = null;
			return false;
		}

		StringDataFlags GetStringDataFlags(MethodDef method) {
			if (method == null || method.Body == null)
				return 0;
			var sig = method.MethodSig;
			if (sig == null || sig.Params.Count != 1)
				return 0;
			if (!CheckClass(sig.Params[0], "System.Byte[]"))
				return 0;
			if (!CheckClass(sig.RetType, "System.Byte[]"))
				return 0;

			StringDataFlags flags = 0;

			if (HasInstruction(method, Code.Not))
				flags |= StringDataFlags.Encrypted2;
			else if (HasInstruction(method, Code.Xor))
				flags |= StringDataFlags.Encrypted1;
			else if (Check3DesCreator(method))
				flags |= StringDataFlags.Encrypted3DES;
			if (CallsDecompressor(method))
				flags |= StringDataFlags.Compressed;

			return flags;
		}

		bool Check3DesCreator(MethodDef method) {
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call)
					continue;
				var calledMethod = instr.Operand as MethodDef;
				if (calledMethod == null)
					continue;
				var sig = calledMethod.MethodSig;
				if (sig == null || sig.RetType.GetElementType() == ElementType.Void)
					continue;
				if (sig.Params.Count != 0)
					continue;
				if (!Get3DesKeyIv(calledMethod, ref key, ref iv))
					continue;

				return true;
			}
			return false;
		}

		bool Get3DesKeyIv(MethodDef method, ref byte[] key, ref byte[] iv) {
			if (!new LocalTypes(method).Exists("System.Security.Cryptography.TripleDESCryptoServiceProvider"))
				return false;

			var instrs = method.Body.Instructions;
			var arrays = ArrayFinder.GetArrays(method, module.CorLibTypes.Byte);
			if (arrays.Count != 1 && arrays.Count != 2)
				return false;

			key = arrays[0];
			if (arrays.Count == 1) {
				var pkt = PublicKeyBase.ToPublicKeyToken(module.Assembly.PublicKey);
				iv = pkt == null ? null : pkt.Data;
			}
			else
				iv = arrays[1];
			return true;
		}

		static bool CallsDecompressor(MethodDef method) {
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call)
					continue;
				var called = instr.Operand as MethodDef;
				if (called == null)
					continue;
				var sig = called.MethodSig;
				if (sig == null)
					continue;
				if (sig.RetType.GetElementType() != ElementType.I4)
					continue;
				var parameters = sig.Params;
				if (parameters.Count != 4)
					continue;
				if (!CheckClass(parameters[0], "System.Byte[]"))
					continue;
				if (parameters[1].GetElementType() != ElementType.I4)
					continue;
				if (!CheckClass(parameters[2], "System.Byte[]"))
					continue;
				if (parameters[3].GetElementType() != ElementType.I4)
					continue;

				return true;
			}
			return false;
		}

		static bool HasInstruction(MethodDef method, Code code) {
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code == code)
					return true;
			}
			return false;
		}

		static bool CheckClass(TypeSig type, string fullName) {
			return type != null && (type.ElementType == ElementType.Object || type.FullName == fullName);
		}

		static bool IsStringType(TypeSig type) {
			return type != null && (type.ElementType == ElementType.Object || type.ElementType == ElementType.String);
		}

		bool InitializeDecrypterInfos(TypeDef type) {
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				var sig = method.MethodSig;
				if (sig == null)
					continue;
				if (sig.Params.Count != 0)
					continue;
				if (!IsStringType(sig.RetType))
					continue;

				var info = CreateInfo(method);
				if (info == null)
					continue;

				methodToInfo.Add(method, info);
			}

			return methodToInfo.Count != 0;
		}

		DecrypterInfo CreateInfo(MethodDef method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var ldci4_1 = instrs[i];
				var ldci4_2 = instrs[i + 1];
				if (!ldci4_1.IsLdcI4() || !ldci4_2.IsLdcI4())
					continue;

				int offset = ldci4_1.GetLdcI4Value();
				int length = ldci4_2.GetLdcI4Value();
				return new DecrypterInfo(method, offset, length);
			}

			return null;
		}

		public void Initialize() {
			if (decrypterType == null)
				return;

			decryptedData = new byte[encryptedDataField.InitialValue.Length];
			Array.Copy(encryptedDataField.InitialValue, 0, decryptedData, 0, decryptedData.Length);

			if ((stringDataFlags & StringDataFlags.Encrypted1) != 0) {
				for (int i = 0; i < decryptedData.Length; i++)
					decryptedData[i] ^= (byte)i;
			}

			if ((stringDataFlags & StringDataFlags.Encrypted2) != 0) {
				var k = module.Assembly.PublicKey.Data;
				int mask = (byte)(~k.Length);
				for (int i = 0; i < decryptedData.Length; i++)
					decryptedData[i] ^= k[i & mask];
			}

			if ((stringDataFlags & StringDataFlags.Encrypted3DES) != 0)
				decryptedData = DeobUtils.Des3Decrypt(decryptedData, key, iv);

			if ((stringDataFlags & StringDataFlags.Compressed) != 0)
				decryptedData = QclzDecompressor.Decompress(decryptedData);
		}

		public void CleanUp() {
			if (decrypterType == null)
				return;

			encryptedDataField.InitialValue = new byte[1];
			encryptedDataField.FieldSig.Type = module.CorLibTypes.Byte;
			encryptedDataField.RVA = 0;
		}

		public string Decrypt(MethodDef method) {
			var info = methodToInfo.Find(method);
			return Encoding.Unicode.GetString(decryptedData, info.offset, info.length);
		}
	}
}
