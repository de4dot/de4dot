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
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CodeVeil {
	class StringDecrypter {
		ModuleDefinition module;
		MainType mainType;
		TypeDefinition decrypterType;
		FieldDefinition stringDataField;
		MethodDefinition initMethod;
		MethodDefinition decrypterMethod;
		string[] decryptedStrings;

		public bool Detected {
			get { return decrypterType != null; }
		}

		public TypeDefinition Type {
			get { return decrypterType; }
		}

		public MethodDefinition InitMethod {
			get { return initMethod; }
		}

		public MethodDefinition DecryptMethod {
			get { return decrypterMethod; }
		}

		public StringDecrypter(ModuleDefinition module, MainType mainType) {
			this.module = module;
			this.mainType = mainType;
		}

		public StringDecrypter(ModuleDefinition module, MainType mainType, StringDecrypter oldOne) {
			this.module = module;
			this.mainType = mainType;
			this.decrypterType = lookup(oldOne.decrypterType, "Could not find string decrypter type");
			this.stringDataField = lookup(oldOne.stringDataField, "Could not find string data field");
			this.initMethod = lookup(oldOne.initMethod, "Could not find string decrypter init method");
			this.decrypterMethod = lookup(oldOne.decrypterMethod, "Could not find string decrypter method");
		}

		T lookup<T>(T def, string errorMessage) where T : MemberReference {
			return DeobUtils.lookup(module, def, errorMessage);
		}

		public void find() {
			var cctor = DotNetUtils.getModuleTypeCctor(module);
			if (cctor == null)
				return;

			// V3-V4 calls string decrypter init method in <Module>::.cctor().
			if (find(cctor))
				return;

			findV5(cctor);
		}

		bool find(MethodDefinition method) {
			if (method == null || method.Body == null || !method.IsStatic)
				return false;

			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				var call = instrs[i];
				if (call.OpCode.Code != Code.Call)
					continue;
				var initMethodTmp = call.Operand as MethodDefinition;
				if (initMethodTmp == null || initMethodTmp.Body == null || !initMethodTmp.IsStatic)
					continue;
				if (!DotNetUtils.isMethod(initMethodTmp, "System.Void", "()"))
					continue;
				if (!checkType(initMethodTmp.DeclaringType))
					continue;

				decrypterType = initMethodTmp.DeclaringType;
				initMethod = initMethodTmp;
				return true;
			}

			return false;
		}

		// The main decrypter type calls the string decrypter init method inside its init method
		void findV5(MethodDefinition method) {
			if (!mainType.Detected)
				return;
			foreach (var calledMethod in DotNetUtils.getCalledMethods(module, mainType.InitMethod)) {
				if (find(calledMethod))
					return;
			}
		}

		bool checkType(TypeDefinition type) {
			if (!type.HasNestedTypes)
				return false;

			var stringDataFieldTmp = checkFields(type);
			if (stringDataFieldTmp == null)
				return false;
			var fieldType = DotNetUtils.getType(module, stringDataFieldTmp.FieldType);
			if (fieldType == null || type.NestedTypes.IndexOf(fieldType) < 0)
				return false;

			var decrypterMethodTmp = getDecrypterMethod(type);
			if (decrypterMethodTmp == null)
				return false;

			stringDataField = stringDataFieldTmp;
			decrypterMethod = decrypterMethodTmp;
			return true;
		}

		static MethodDefinition getDecrypterMethod(TypeDefinition type) {
			MethodDefinition foundMethod = null;
			foreach (var method in type.Methods) {
				if (method.Body == null || !method.IsStatic)
					continue;
				if (!DotNetUtils.isMethod(method, "System.String", "(System.Int32)"))
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
		FieldDefinition checkFields(TypeDefinition type) {
			if (!new FieldTypes(type).all(requiredFields))
				return null;

			FieldDefinition stringData = null;
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

		public void initialize() {
			if (initMethod == null || stringDataField == null)
				return;

			var key = getKey(initMethod);
			if (key == null)
				throw new ApplicationException("Could not find string decrypter key");

			decryptStrings(key);

			stringDataField.FieldType = module.TypeSystem.Byte;
			stringDataField.InitialValue = new byte[1];
		}

		static uint[] getKey(MethodDefinition method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var ldci4 = instrs[i];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				if (DotNetUtils.getLdcI4Value(ldci4) != 4)
					continue;

				if (instrs[i + 1].OpCode.Code != Code.Newarr)
					continue;

				i++;
				var key = ArrayFinder.getInitializedUInt32Array(4, method, ref i);
				if (key == null)
					continue;

				return key;
			}
			return null;
		}

		void decryptStrings(uint[] key) {
			var data = stringDataField.InitialValue;

			var encryptedData = new uint[data.Length / 4];
			Buffer.BlockCopy(data, 0, encryptedData, 0, data.Length);
			DeobUtils.xxteaDecrypt(encryptedData, key);
			var decryptedData = new byte[data.Length];
			Buffer.BlockCopy(encryptedData, 0, decryptedData, 0, data.Length);

			var inflated = DeobUtils.inflate(decryptedData, 0, decryptedData.Length, true);
			var reader = new BinaryReader(new MemoryStream(inflated));
			int deflatedLength = DeobUtils.readVariableLengthInt32(reader);
			int numStrings = DeobUtils.readVariableLengthInt32(reader);
			decryptedStrings = new string[numStrings];
			var offsets = new int[numStrings];
			for (int i = 0; i < numStrings; i++)
				offsets[i] = DeobUtils.readVariableLengthInt32(reader);
			int startOffset = (int)reader.BaseStream.Position;
			for (int i = 0; i < numStrings; i++) {
				reader.BaseStream.Position = startOffset + offsets[i];
				decryptedStrings[i] = reader.ReadString();
			}
		}

		public string decrypt(int index) {
			return decryptedStrings[index];
		}
	}
}
