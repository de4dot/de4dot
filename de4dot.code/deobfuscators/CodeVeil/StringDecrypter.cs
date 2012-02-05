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
		TypeDefinition decrypterType;
		FieldDefinition stringDataField;
		MethodDefinition initMethod;
		MethodDefinition decrypterMethod;
		string[] decryptedStrings;

		public bool Detected {
			get { return decrypterType != null; }
		}

		public MethodDefinition DecryptMethod {
			get { return decrypterMethod; }
		}

		public StringDecrypter(ModuleDefinition module) {
			this.module = module;
		}

		public StringDecrypter(ModuleDefinition module, StringDecrypter oldOne) {
			this.module = module;
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

			var instrs = cctor.Body.Instructions;
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
				break;
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
			if (stringDataField == null)
				return;

			var key = getKey(initMethod);
			if (key == null)
				throw new ApplicationException("Could not find string decrypter key");

			decryptStrings(key);
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
			decryptXxtea(encryptedData, encryptedData.Length, key);
			var decryptedData = new byte[data.Length];
			Buffer.BlockCopy(encryptedData, 0, decryptedData, 0, data.Length);

			var inflated = DeobUtils.inflate(decryptedData, 0, decryptedData.Length, true);
			var reader = new BinaryReader(new MemoryStream(inflated));
			int deflatedLength = DeobUtils.readVariableLengthInteger(reader);
			int numStrings = DeobUtils.readVariableLengthInteger(reader);
			decryptedStrings = new string[numStrings];
			var offsets = new int[numStrings];
			for (int i = 0; i < numStrings; i++)
				offsets[i] = DeobUtils.readVariableLengthInteger(reader);
			int startOffset = (int)reader.BaseStream.Position;
			for (int i = 0; i < numStrings; i++) {
				reader.BaseStream.Position = startOffset + offsets[i];
				decryptedStrings[i] = reader.ReadString();
			}
		}

		// Code converted from C implementation @ http://en.wikipedia.org/wiki/XXTEA (btea() func)
		static void decryptXxtea(uint[] v, int n, uint[] key) {
			const uint DELTA = 0x9E3779B9;
			uint rounds = (uint)(6 + 52 / n);
			uint sum = rounds * DELTA;
			uint y = v[0];
			uint z;
			//#define MX (((z >> 5 ^ y << 2) + (y >> 3 ^ z << 4)) ^ ((sum ^ y) + (key[(p & 3) ^ e] ^ z)))
			do {
				int e = (int)((sum >> 2) & 3);
				int p;
				for (p = n - 1; p > 0; p--) {
					z = v[p - 1];
					y = v[p] -= (((z >> 5 ^ y << 2) + (y >> 3 ^ z << 4)) ^ ((sum ^ y) + (key[(p & 3) ^ e] ^ z)));
				}
				z = v[n - 1];
				y = v[0] -= (((z >> 5 ^ y << 2) + (y >> 3 ^ z << 4)) ^ ((sum ^ y) + (key[(p & 3) ^ e] ^ z)));
			} while ((sum -= DELTA) != 0);
		}

		public string decrypt(int index) {
			return decryptedStrings[index];
		}
	}
}
