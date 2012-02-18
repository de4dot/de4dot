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
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Skater_NET {
	class StringDecrypter {
		ModuleDefinition module;
		TypeDefinition decrypterType;
		MethodDefinition decrypterCctor;
		byte[] key;
		byte[] iv;
		FieldDefinitionAndDeclaringTypeDict<string> fieldToDecryptedString = new FieldDefinitionAndDeclaringTypeDict<string>();
		bool canRemoveType;

		public bool Detected {
			get { return decrypterType != null; }
		}

		public bool CanRemoveType {
			get { return canRemoveType; }
		}

		public TypeDefinition Type {
			get { return decrypterType; }
		}

		public StringDecrypter(ModuleDefinition module) {
			this.module = module;
		}

		public void find() {
			foreach (var type in module.Types) {
				if (type.HasProperties || type.HasEvents)
					continue;

				var cctor = DotNetUtils.getMethod(type, ".cctor");
				if (cctor == null)
					continue;

				if (checkType(type)) {
					canRemoveType = true;
					decrypterType = type;
					decrypterCctor = cctor;
					return;
				}
			}
		}

		public void initialize() {
			if (decrypterCctor == null)
				return;

			var instrs = decrypterCctor.Body.Instructions;
			for (int i = 0; i < instrs.Count - 4; i++) {
				var ldstr = instrs[i];
				if (ldstr.OpCode.Code != Code.Ldstr)
					continue;
				var encryptedString = ldstr.Operand as string;
				if (encryptedString == null)
					continue;
				if (instrs[i + 1].OpCode.Code != Code.Stsfld)
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Ldsfld)
					continue;
				if (instrs[i + 3].OpCode.Code != Code.Call)
					continue;
				if (instrs[i + 4].OpCode.Code != Code.Stsfld)
					continue;
				var field = instrs[i + 4].Operand as FieldDefinition;
				if (field == null)
					continue;
				if (!MemberReferenceHelper.compareTypes(field.DeclaringType, decrypterType))
					continue;

				string decryptedString;
				try {
					decryptedString = Encoding.Unicode.GetString(DeobUtils.des3Decrypt(Convert.FromBase64String(encryptedString), key, iv));
				}
				catch (FormatException) {
					decryptedString = "";
				}
				fieldToDecryptedString.add(field, decryptedString);
			}
		}

		bool checkType(TypeDefinition type) {
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!DotNetUtils.isMethod(method, "System.String", "(System.String)"))
					continue;

				var salt = getSalt(method);
				if (salt == null)
					continue;

				var password = getPassword(method);
				if (string.IsNullOrEmpty(password))
					continue;

				var passwordBytes = new PasswordDeriveBytes(password, salt);
				key = passwordBytes.GetBytes(16);
				iv = passwordBytes.GetBytes(8);
				return true;
			}

			return false;
		}

		static byte[] getSalt(MethodDefinition method) {
			foreach (var s in DotNetUtils.getCodeStrings(method)) {
				var saltAry = fixSalt(s);
				if (saltAry != null)
					return saltAry;
			}

			return null;
		}

		static byte[] fixSalt(string s) {
			if (s.Length < 10 || s.Length > 30 || s.Length / 2 * 2 != s.Length)
				return null;

			var ary = s.ToCharArray();
			Array.Reverse(ary);
			for (int i = 0; i < ary.Length; i++)
				ary[i]--;
			var s2 = new string(ary);

			var saltAry = new byte[(int)Math.Round((double)s2.Length / 2 - 1) + 1];
			for (int i = 0; i < saltAry.Length; i++) {
				int result;
				if (!int.TryParse(s2.Substring(i * 2, 2), NumberStyles.AllowHexSpecifier, null, out result))
					return null;
				saltAry[i] = (byte)result;
			}

			return saltAry;
		}

		string getPassword(MethodDefinition decryptMethod) {
			foreach (var info in DotNetUtils.getCalledMethods(module, decryptMethod)) {
				var method = info.Item2;
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!MemberReferenceHelper.compareTypes(method.DeclaringType, decryptMethod.DeclaringType))
					continue;
				if (!DotNetUtils.isMethod(method, "System.String", "()"))
					continue;

				var hexChars = getPassword2(method);
				if (string.IsNullOrEmpty(hexChars))
					continue;

				var password = fixPassword(hexChars);
				if (string.IsNullOrEmpty(password))
					continue;

				return password;
			}
			return null;
		}

		string fixPassword(string hexChars) {
			var ary = hexChars.Trim().Split(' ');
			string password = "";
			for (int i = 0; i < ary.Length; i++) {
				int result;
				if (!int.TryParse(ary[i], NumberStyles.AllowHexSpecifier, null, out result))
					return null;
				password += (char)result;
			}
			return password;
		}

		string getPassword2(MethodDefinition method) {
			string password = "";
			foreach (var info in DotNetUtils.getCalledMethods(module, method)) {
				var s = getPassword3(info.Item2);
				if (string.IsNullOrEmpty(s))
					return null;

				password += s;
			}
			return password;
		}

		string getPassword3(MethodDefinition method) {
			var strings = new List<string>(DotNetUtils.getCodeStrings(method));
			if (strings.Count != 1)
				return null;

			var s = strings[0];
			if (!Regex.IsMatch(s, @"^[a-fA-F0-9]{2} $"))
				return null;

			return s;
		}

		public void deobfuscate(Blocks blocks) {
			foreach (var block in blocks.MethodBlocks.getAllBlocks()) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count; i++) {
					var instr = instrs[i];

					if (instr.OpCode.Code == Code.Call || instr.OpCode.Code == Code.Callvirt) {
						if (blocks.Method.DeclaringType == decrypterType)
							continue;
						var calledMethod = instr.Operand as MethodReference;
						if (calledMethod != null && calledMethod.DeclaringType == decrypterType)
							canRemoveType = false;
					}
					else if (instr.OpCode.Code == Code.Ldsfld) {
						if (instr.OpCode.Code != Code.Ldsfld)
							continue;
						var field = instr.Operand as FieldReference;
						if (field == null)
							continue;
						var decrypted = fieldToDecryptedString.find(field);
						if (decrypted == null)
							continue;

						instrs[i] = new Instr(Instruction.Create(OpCodes.Ldstr, decrypted));
						Log.v("Decrypted string: {0}", Utils.toCsharpString(decrypted));
					}
				}
			}
		}
	}
}
