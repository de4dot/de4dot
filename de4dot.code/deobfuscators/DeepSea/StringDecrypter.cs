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
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.DeepSea {
	class StringDecrypter {
		ModuleDefinition module;
		MethodDefinitionAndDeclaringTypeDict<DecrypterInfo> methodToInfo = new MethodDefinitionAndDeclaringTypeDict<DecrypterInfo>();

		class DecrypterInfo {
			MethodDefinition cctor;
			public MethodDefinition method;
			FieldDefinition cachedStringsField;
			FieldDefinition keyField;
			int magic;
			string[] encryptedStrings;
			short[] key;
			public DecrypterInfo(MethodDefinition cctor, MethodDefinition method) {
				this.cctor = cctor;
				this.method = method;
			}

			public string decrypt(int magic2) {
				var es = encryptedStrings[magic ^ magic2];
				var sb = new StringBuilder(es.Length);
				for (int i = 0; i < es.Length; i++)
					sb.Append((char)(es[i] ^ key[(magic2 + i) % key.Length]));
				return sb.ToString();
			}

			public bool initialize() {
				if (!findMagic(method, out magic))
					return false;

				if (!findFields())
					return false;

				if (!findEncryptedStrings(method))
					return false;

				key = findKey();
				if (key.Length == 0)
					return false;

				return true;
			}

			bool findFields() {
				foreach (var instr in method.Body.Instructions) {
					if (instr.OpCode.Code != Code.Stsfld && instr.OpCode.Code != Code.Ldsfld)
						continue;
					var field = instr.Operand as FieldDefinition;
					if (field == null)
						continue;
					if (!MemberReferenceHelper.compareTypes(method.DeclaringType, field.DeclaringType))
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
				var instrs = initMethod.Body.Instructions;
				for (int i = 0; i < instrs.Count - 1; i++) {
					var ldci4 = instrs[i];
					if (!DotNetUtils.isLdcI4(ldci4))
						continue;
					var newarr = instrs[i + 1];
					if (newarr.OpCode.Code != Code.Newarr)
						continue;
					if (newarr.Operand.ToString() != "System.Char")
						continue;

					i++;
					var array = ArrayFinder.getInitializedInt16Array(DotNetUtils.getLdcI4Value(ldci4), initMethod, ref i);
					if (array == null)
						continue;
					i++;
					if (i >= instrs.Count)
						return null;
					var stsfld = instrs[i];
					if (stsfld.OpCode.Code != Code.Stsfld)
						continue;
					if (MemberReferenceHelper.compareFieldReferenceAndDeclaringType(keyField, stsfld.Operand as FieldReference))
						return array;
				}

				return null;
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
					if (DotNetUtils.getArgIndex(ldarg) != 0)
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

			public override string ToString() {
				return string.Format("M:{0:X8} N:{1}", magic, encryptedStrings.Length);
			}
		}

		public bool Detected {
			get { return methodToInfo.Count != 0; }
		}

		public List<MethodDefinition> DecrypterMethods {
			get {
				var methods = new List<MethodDefinition>(methodToInfo.Count);
				foreach (var info in methodToInfo.getValues())
					methods.Add(info.method);
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
				foreach (var method in DotNetUtils.findMethods(type.Methods, "System.String", new string[] { "System.Int32" }, true)) {
					simpleDeobfuscator.deobfuscate(method);
					var info = getInfo(cctor, method);
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

		DecrypterInfo getInfo(MethodDefinition cctor, MethodDefinition method) {
			if (method == null || method.Body == null)
				return null;

			var info = new DecrypterInfo(cctor, method);
			if (!info.initialize())
				return null;

			return info;
		}

		public string decrypt(MethodReference method, int magic2) {
			var info = methodToInfo.find(method);
			return info.decrypt(magic2);
		}
	}
}
