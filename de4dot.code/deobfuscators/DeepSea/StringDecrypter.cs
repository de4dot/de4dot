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
using de4dot.blocks;

namespace de4dot.code.deobfuscators.DeepSea {
	class StringDecrypter {
		ModuleDefinition module;
		MethodDefinitionAndDeclaringTypeDict<IDecrypterInfo> methodToInfo = new MethodDefinitionAndDeclaringTypeDict<IDecrypterInfo>();
		DecrypterVersion version = DecrypterVersion.Unknown;

		public enum DecrypterVersion {
			Unknown,
			V1_3,
			V4,
		}

		interface IDecrypterInfo {
			DecrypterVersion Version { get; }
			MethodDefinition Method { get; }
			string decrypt(object[] args);
			void cleanup();
		}

		static short[] findKey(MethodDefinition initMethod, FieldDefinition keyField) {
			var instrs = initMethod.Body.Instructions;
			for (int i = 0; i < instrs.Count - 2; i++) {
				var ldci4 = instrs[i];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				var newarr = instrs[i + 1];
				if (newarr.OpCode.Code != Code.Newarr)
					continue;
				if (newarr.Operand.ToString() != "System.Char")
					continue;

				var stloc = instrs[i + 2];
				if (!DotNetUtils.isStloc(stloc))
					continue;
				var local = DotNetUtils.getLocalVar(initMethod.Body.Variables, stloc);

				int startInitIndex = i;
				i++;
				var array = ArrayFinder.getInitializedInt16Array(DotNetUtils.getLdcI4Value(ldci4), initMethod, ref i);
				if (array == null)
					continue;

				var field = getStoreField(initMethod, startInitIndex, local);
				if (field == null)
					continue;
				if (MemberReferenceHelper.compareFieldReferenceAndDeclaringType(keyField, field))
					return array;
			}

			return null;
		}

		static FieldDefinition getStoreField(MethodDefinition method, int startIndex, VariableDefinition local) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var ldloc = instrs[i];
				if (!DotNetUtils.isLdloc(ldloc))
					continue;
				if (DotNetUtils.getLocalVar(method.Body.Variables, ldloc) != local)
					continue;

				var stsfld = instrs[i + 1];
				if (stsfld.OpCode.Code != Code.Stsfld)
					continue;
				return stsfld.Operand as FieldDefinition;
			}

			return null;
		}

		class DecrypterInfo4 : IDecrypterInfo {
			MethodDefinition cctor;
			public MethodDefinition Method { get; set; }
			int magic;
			FieldDefinition cachedStringsField;
			FieldDefinition keyField;
			FieldDefinition encryptedStringsField;
			FieldDefinition encryptedDataField;
			short[] key;
			ushort[] encryptedData;

			public DecrypterVersion Version {
				get { return DecrypterVersion.V4; }
			}

			public DecrypterInfo4(MethodDefinition cctor, MethodDefinition method) {
				this.cctor = cctor;
				this.Method = method;
			}

			public bool initialize() {
				if (!findMagic(Method, out magic))
					return false;

				var charArrayFields = findFields();
				if (charArrayFields == null || charArrayFields.Count != 2)
					return false;

				encryptedStringsField = findEncryptedStrings(cctor, charArrayFields, out encryptedDataField);
				if (encryptedStringsField == null)
					return false;
				if (encryptedDataField.InitialValue.Length % 2 == 1)
					return false;
				encryptedData = new ushort[encryptedDataField.InitialValue.Length / 2];
				Buffer.BlockCopy(encryptedDataField.InitialValue, 0, encryptedData, 0, encryptedDataField.InitialValue.Length);

				charArrayFields.Remove(encryptedStringsField);
				keyField = charArrayFields[0];

				key = findKey();
				if (key == null || key.Length == 0)
					return false;

				return true;
			}

			static bool findMagic(MethodDefinition method, out int magic) {
				var instrs = method.Body.Instructions;
				for (int i = 0; i < instrs.Count - 2; i++) {
					var ldarg = instrs[i];
					if (DotNetUtils.getArgIndex(ldarg) < 0)
						continue;
					var ldci4 = instrs[i + 1];
					if (!DotNetUtils.isLdcI4(ldci4))
						continue;
					if (instrs[i + 2].OpCode.Code != Code.Xor)
						continue;
					magic = DotNetUtils.getLdcI4Value(ldci4);
					return true;
				}
				magic = 0;
				return false;
			}

			List<FieldDefinition> findFields() {
				var charArrayFields = new List<FieldDefinition>();

				foreach (var instr in Method.Body.Instructions) {
					if (instr.OpCode.Code != Code.Stsfld && instr.OpCode.Code != Code.Ldsfld)
						continue;
					var field = instr.Operand as FieldDefinition;
					if (field == null)
						continue;
					if (!MemberReferenceHelper.compareTypes(Method.DeclaringType, field.DeclaringType))
						continue;
					switch (field.FieldType.FullName) {
					case "System.Char[]":
						if (!charArrayFields.Contains(field))
							charArrayFields.Add(field);
						break;

					case "System.String[]":
						if (cachedStringsField != null && cachedStringsField != field)
							return null;
						cachedStringsField = field;
						break;

					default:
						break;
					}
				}

				if (cachedStringsField == null)
					return null;

				return charArrayFields;
			}

			static FieldDefinition findEncryptedStrings(MethodDefinition initMethod, List<FieldDefinition> ourFields, out FieldDefinition dataField) {
				for (int i = 0; i < initMethod.Body.Instructions.Count; i++) {
					var instrs = DotNetUtils.getInstructions(initMethod.Body.Instructions, i, OpCodes.Ldtoken, OpCodes.Call, OpCodes.Stsfld);
					if (instrs == null)
						continue;

					dataField = instrs[0].Operand as FieldDefinition;
					if (dataField == null || dataField.InitialValue == null || dataField.InitialValue.Length == 0)
						continue;

					var savedField = instrs[2].Operand as FieldDefinition;
					if (savedField == null || !matches(ourFields, savedField))
						continue;

					return savedField;
				}

				dataField = null;
				return null;
			}

			static bool matches(IEnumerable<FieldDefinition> ourFields, FieldReference field) {
				foreach (var ourField in ourFields) {
					if (MemberReferenceHelper.compareFieldReferenceAndDeclaringType(ourField, field))
						return true;
				}
				return false;
			}

			short[] findKey() {
				var pkt = cctor.Module.Assembly.Name.PublicKeyToken;
				if (pkt != null && pkt.Length > 0)
					return getPublicKeyTokenKey(pkt);
				return findKey(cctor);
			}

			short[] findKey(MethodDefinition initMethod) {
				return StringDecrypter.findKey(initMethod, keyField);
			}

			static short[] getPublicKeyTokenKey(byte[] publicKeyToken) {
				var key = new short[publicKeyToken.Length];
				for (int i = 0; i < publicKeyToken.Length; i++) {
					int b = publicKeyToken[i];
					key[i] = (short)((b << 4) ^ b);
				}
				return key;
			}

			public string decrypt(object[] args) {
				return decrypt((int)args[0], (int)args[1]);
			}

			string decrypt(int magic2, int magic3) {
				int index = magic ^ magic2 ^ magic3;
				int cachedIndex = (ushort)encryptedData[index++];
				int stringLen = (int)(ushort)encryptedData[index++] + ((int)(ushort)encryptedData[index++] << 16);
				var sb = new StringBuilder(stringLen);
				for (int i = 0; i < stringLen; i++)
					sb.Append((char)(encryptedData[index++] ^ key[cachedIndex++ % key.Length]));
				return sb.ToString();
			}

			public void cleanup() {
				encryptedDataField.InitialValue = new byte[0];
			}
		}

		class DecrypterInfo3 : IDecrypterInfo {
			MethodDefinition cctor;
			public MethodDefinition Method { get; set; }
			FieldDefinition cachedStringsField;
			FieldDefinition keyField;
			int magic;
			string[] encryptedStrings;
			short[] key;

			public DecrypterVersion Version {
				get { return DecrypterVersion.V1_3; }
			}

			public DecrypterInfo3(MethodDefinition cctor, MethodDefinition method) {
				this.cctor = cctor;
				this.Method = method;
			}

			public string decrypt(object[] args) {
				return decrypt((int)args[0]);
			}

			string decrypt(int magic2) {
				var es = encryptedStrings[magic ^ magic2];
				var sb = new StringBuilder(es.Length);
				for (int i = 0; i < es.Length; i++)
					sb.Append((char)(es[i] ^ key[(magic2 + i) % key.Length]));
				return sb.ToString();
			}

			public bool initialize() {
				if (!findMagic(Method, out magic))
					return false;

				if (!findFields())
					return false;

				if (!findEncryptedStrings(Method))
					return false;

				key = findKey();
				if (key.Length == 0)
					return false;

				return true;
			}

			bool findFields() {
				foreach (var instr in Method.Body.Instructions) {
					if (instr.OpCode.Code != Code.Stsfld && instr.OpCode.Code != Code.Ldsfld)
						continue;
					var field = instr.Operand as FieldDefinition;
					if (field == null)
						continue;
					if (!MemberReferenceHelper.compareTypes(Method.DeclaringType, field.DeclaringType))
						continue;
					switch (field.FieldType.FullName) {
					case "System.Char[]":
						if (keyField != null && keyField != field)
							return false;
						keyField = field;
						break;

					case "System.String[]":
						if (cachedStringsField != null && cachedStringsField != field)
							return false;
						cachedStringsField = field;
						break;

					default:
						break;
					}
				}

				return keyField != null && cachedStringsField != null;
			}

			short[] findKey() {
				var pkt = cctor.Module.Assembly.Name.PublicKeyToken;
				if (pkt != null && pkt.Length > 0)
					return getPublicKeyTokenKey(pkt);
				return findKey(cctor);
			}

			short[] findKey(MethodDefinition initMethod) {
				return StringDecrypter.findKey(initMethod, keyField);
			}

			static short[] getPublicKeyTokenKey(byte[] publicKeyToken) {
				var key = new short[publicKeyToken.Length];
				for (int i = 0; i < publicKeyToken.Length; i++) {
					int b = publicKeyToken[i];
					key[i] = (short)((b << 4) ^ b);
				}
				return key;
			}

			static bool findMagic(MethodDefinition method, out int magic) {
				var instrs = method.Body.Instructions;
				for (int i = 0; i < instrs.Count - 2; i++) {
					var ldarg = instrs[i];
					if (DotNetUtils.getArgIndex(ldarg) < 0)
						continue;
					var ldci4 = instrs[i + 1];
					if (!DotNetUtils.isLdcI4(ldci4))
						continue;
					if (instrs[i + 2].OpCode.Code != Code.Xor)
						continue;
					magic = DotNetUtils.getLdcI4Value(ldci4);
					return true;
				}
				magic = 0;
				return false;
			}

			bool findEncryptedStrings(MethodDefinition method) {
				var switchInstr = getOnlySwitchInstruction(method);
				if (switchInstr == null)
					return false;
				var targets = (Instruction[])switchInstr.Operand;
				encryptedStrings = new string[targets.Length];
				for (int i = 0; i < targets.Length; i++) {
					var target = targets[i];
					if (target.OpCode.Code != Code.Ldstr)
						return false;
					encryptedStrings[i] = (string)target.Operand;
				}
				return true;
			}

			static Instruction getOnlySwitchInstruction(MethodDefinition method) {
				Instruction switchInstr = null;
				foreach (var instr in method.Body.Instructions) {
					if (instr.OpCode.Code != Code.Switch)
						continue;
					if (switchInstr != null)
						return null;
					switchInstr = instr;
				}
				return switchInstr;
			}

			public void cleanup() {
			}

			public override string ToString() {
				return string.Format("M:{0:X8} N:{1}", magic, encryptedStrings.Length);
			}
		}

		public bool Detected {
			get { return methodToInfo.Count != 0; }
		}

		public DecrypterVersion Version {
			get { return version; }
		}

		public List<MethodDefinition> DecrypterMethods {
			get {
				var methods = new List<MethodDefinition>(methodToInfo.Count);
				foreach (var info in methodToInfo.getValues())
					methods.Add(info.Method);
				return methods;
			}
		}

		public StringDecrypter(ModuleDefinition module) {
			this.module = module;
		}

		public void find(ISimpleDeobfuscator simpleDeobfuscator) {
			bool hasPublicKeyToken = module.Assembly.Name.PublicKeyToken != null && module.Assembly.Name.PublicKeyToken.Length != 0;
			foreach (var type in module.GetTypes()) {
				if (!checkFields(type.Fields))
					continue;
				var cctor = DotNetUtils.getMethod(type, ".cctor");
				if (cctor == null)
					continue;
				if (!hasPublicKeyToken)
					simpleDeobfuscator.deobfuscate(cctor);

				foreach (var method in type.Methods) {
					if (method.Body == null)
						continue;

					IDecrypterInfo info = null;

					if (DotNetUtils.isMethod(method, "System.String", "(System.Int32)")) {
						simpleDeobfuscator.deobfuscate(method);
						info = getInfoV3(cctor, method);
					}
					else if (DotNetUtils.isMethod(method, "System.String", "(System.Int32,System.Int32)")) {
						simpleDeobfuscator.deobfuscate(method);
						info = getInfoV4(cctor, method);
					}

					if (info == null)
						continue;
					methodToInfo.add(method, info);
					version = info.Version;
				}

				foreach (var method in DotNetUtils.findMethods(type.Methods, "System.String", new string[] { "System.Int32" }, true)) {
					simpleDeobfuscator.deobfuscate(method);
					var info = getInfoV3(cctor, method);
					if (info == null)
						continue;
					methodToInfo.add(method, info);
				}
			}
		}

		static bool checkFields(IEnumerable<FieldDefinition> fields) {
			bool foundCharAry = false, foundStringAry = false;
			foreach (var field in fields) {
				if (foundCharAry && foundStringAry)
					break;
				switch (field.FieldType.FullName) {
				case "System.Char[]":
					foundCharAry = true;
					break;
				case "System.String[]":
					foundStringAry = true;
					break;
				}
			}
			return foundCharAry && foundStringAry;
		}

		DecrypterInfo3 getInfoV3(MethodDefinition cctor, MethodDefinition method) {
			var info = new DecrypterInfo3(cctor, method);
			if (!info.initialize())
				return null;
			return info;
		}

		DecrypterInfo4 getInfoV4(MethodDefinition cctor, MethodDefinition method) {
			var info = new DecrypterInfo4(cctor, method);
			if (!info.initialize())
				return null;
			return info;
		}

		public string decrypt(MethodReference method, object[] args) {
			var info = methodToInfo.find(method);
			return info.decrypt(args);
		}

		public void cleanup() {
			foreach (var info in methodToInfo.getValues())
				info.cleanup();
		}
	}
}
