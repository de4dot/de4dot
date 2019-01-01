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
using dnlib.IO;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CodeVeil {
	class StringDecrypter {
		ModuleDefMD module;
		MainType mainType;
		TypeDef decrypterType;
		FieldDef stringDataField;
		MethodDef initMethod;
		MethodDef decrypterMethod;
		string[] decryptedStrings;

		public bool Detected => decrypterType != null;
		public TypeDef Type => decrypterType;
		public MethodDef InitMethod => initMethod;
		public MethodDef DecryptMethod => decrypterMethod;

		public StringDecrypter(ModuleDefMD module, MainType mainType) {
			this.module = module;
			this.mainType = mainType;
		}

		public StringDecrypter(ModuleDefMD module, MainType mainType, StringDecrypter oldOne) {
			this.module = module;
			this.mainType = mainType;
			decrypterType = Lookup(oldOne.decrypterType, "Could not find string decrypter type");
			stringDataField = Lookup(oldOne.stringDataField, "Could not find string data field");
			initMethod = Lookup(oldOne.initMethod, "Could not find string decrypter init method");
			decrypterMethod = Lookup(oldOne.decrypterMethod, "Could not find string decrypter method");
		}

		T Lookup<T>(T def, string errorMessage) where T : class, ICodedToken =>
			DeobUtils.Lookup(module, def, errorMessage);

		public void Find() {
			var cctor = DotNetUtils.GetModuleTypeCctor(module);
			if (cctor == null)
				return;

			// V3-V4 calls string decrypter init method in <Module>::.cctor().
			if (Find(cctor))
				return;

			FindV5(cctor);
		}

		bool Find(MethodDef method) {
			if (method == null || method.Body == null || !method.IsStatic)
				return false;

			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				var call = instrs[i];
				if (call.OpCode.Code != Code.Call)
					continue;
				var initMethodTmp = call.Operand as MethodDef;
				if (initMethodTmp == null || initMethodTmp.Body == null || !initMethodTmp.IsStatic)
					continue;
				if (!DotNetUtils.IsMethod(initMethodTmp, "System.Void", "()"))
					continue;
				if (!CheckType(initMethodTmp.DeclaringType))
					continue;

				decrypterType = initMethodTmp.DeclaringType;
				initMethod = initMethodTmp;
				return true;
			}

			return false;
		}

		// The main decrypter type calls the string decrypter init method inside its init method
		void FindV5(MethodDef method) {
			if (!mainType.Detected)
				return;
			foreach (var calledMethod in DotNetUtils.GetCalledMethods(module, mainType.InitMethod)) {
				if (Find(calledMethod))
					return;
			}
		}

		bool CheckType(TypeDef type) {
			if (!type.HasNestedTypes)
				return false;

			var stringDataFieldTmp = CheckFields(type);
			if (stringDataFieldTmp == null)
				return false;
			var fieldType = DotNetUtils.GetType(module, stringDataFieldTmp.FieldSig.GetFieldType());
			if (fieldType == null || type.NestedTypes.IndexOf(fieldType) < 0)
				return false;

			var decrypterMethodTmp = GetDecrypterMethod(type);
			if (decrypterMethodTmp == null)
				return false;

			stringDataField = stringDataFieldTmp;
			decrypterMethod = decrypterMethodTmp;
			return true;
		}

		static MethodDef GetDecrypterMethod(TypeDef type) {
			MethodDef foundMethod = null;
			foreach (var method in type.Methods) {
				if (method.Body == null || !method.IsStatic)
					continue;
				if (!DotNetUtils.IsMethod(method, "System.String", "(System.Int32)"))
					continue;
				if (foundMethod != null)
					return null;
				foundMethod = method;
			}
			return foundMethod;
		}

		static string[] requiredFields = new string[] {
			"System.Byte[]",
			"System.Int32",
			"System.Int32[]",
			"System.String[]",
			"System.UInt32[]",
		};
		FieldDef CheckFields(TypeDef type) {
			if (!new FieldTypes(type).All(requiredFields))
				return null;

			FieldDef stringData = null;
			foreach (var field in type.Fields) {
				if (field.RVA != 0) {
					if (stringData != null)
						return null;
					stringData = field;
					continue;
				}
			}

			if (stringData == null)
				return null;

			var data = stringData.InitialValue;
			if (data == null || data.Length == 0 || data.Length % 4 != 0)
				return null;

			return stringData;
		}

		public void Initialize() {
			if (initMethod == null || stringDataField == null)
				return;

			var key = GetKey(initMethod);
			if (key == null)
				throw new ApplicationException("Could not find string decrypter key");

			DecryptStrings(key);

			stringDataField.FieldSig.Type = module.CorLibTypes.Byte;
			stringDataField.InitialValue = new byte[1];
			stringDataField.RVA = 0;
		}

		static uint[] GetKey(MethodDef method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var ldci4 = instrs[i];
				if (!ldci4.IsLdcI4())
					continue;
				if (ldci4.GetLdcI4Value() != 4)
					continue;

				if (instrs[i + 1].OpCode.Code != Code.Newarr)
					continue;

				i++;
				var key = ArrayFinder.GetInitializedUInt32Array(4, method, ref i);
				if (key == null)
					continue;

				return key;
			}
			return null;
		}

		void DecryptStrings(uint[] key) {
			var data = stringDataField.InitialValue;

			var encryptedData = new uint[data.Length / 4];
			Buffer.BlockCopy(data, 0, encryptedData, 0, data.Length);
			DeobUtils.XxteaDecrypt(encryptedData, key);
			var decryptedData = new byte[data.Length];
			Buffer.BlockCopy(encryptedData, 0, decryptedData, 0, data.Length);

			var inflated = DeobUtils.Inflate(decryptedData, 0, decryptedData.Length, true);
			var reader = ByteArrayDataReaderFactory.CreateReader(inflated);
			/*int deflatedLength = (int)*/reader.ReadCompressedUInt32();
			int numStrings = (int)reader.ReadCompressedUInt32();
			decryptedStrings = new string[numStrings];
			var offsets = new int[numStrings];
			for (int i = 0; i < numStrings; i++)
				offsets[i] = (int)reader.ReadCompressedUInt32();
			int startOffset = (int)reader.Position;
			for (int i = 0; i < numStrings; i++) {
				reader.Position = (uint)(startOffset + offsets[i]);
				decryptedStrings[i] = reader.ReadSerializedString();
			}
		}

		public string Decrypt(int index) => decryptedStrings[index];
	}
}
