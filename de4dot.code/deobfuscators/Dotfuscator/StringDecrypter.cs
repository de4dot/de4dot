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

using System.Collections.Generic;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Dotfuscator {
	class StringDecrypter {
		ModuleDefMD module;
		MethodDefAndDeclaringTypeDict<StringDecrypterInfo> stringDecrypterMethods = new MethodDefAndDeclaringTypeDict<StringDecrypterInfo>();

		public class StringDecrypterInfo {
			public MethodDef method;
			public int magic;
			public StringDecrypterInfo(MethodDef method, int magic) {
				this.method = method;
				this.magic = magic;
			}
		}

		public bool Detected {
			get { return stringDecrypterMethods.Count > 0; }
		}

		public IEnumerable<MethodDef> StringDecrypters {
			get {
				var list = new List<MethodDef>(stringDecrypterMethods.Count);
				foreach (var info in stringDecrypterMethods.GetValues())
					list.Add(info.method);
				return list;
			}
		}

		public IEnumerable<StringDecrypterInfo> StringDecrypterInfos {
			get { return stringDecrypterMethods.GetValues(); }
		}

		public StringDecrypter(ModuleDefMD module) {
			this.module = module;
		}

		public void Find(ISimpleDeobfuscator simpleDeobfuscator) {
			foreach (var type in module.GetTypes())
				FindStringDecrypterMethods(type, simpleDeobfuscator);
		}

		void FindStringDecrypterMethods(TypeDef type, ISimpleDeobfuscator simpleDeobfuscator) {
			foreach (var method in DotNetUtils.FindMethods(type.Methods, "System.String", new string[] { "System.String", "System.Int32" })) {
				if (method.Body.HasExceptionHandlers)
					continue;

				if (DotNetUtils.GetMethodCalls(method, "System.Char[] System.String::ToCharArray()") != 1)
					continue;
				if (DotNetUtils.GetMethodCalls(method, "System.String System.String::Intern(System.String)") != 1)
					continue;

				simpleDeobfuscator.Deobfuscate(method);
				var instrs = method.Body.Instructions;
				for (int i = 0; i < instrs.Count - 3; i++) {
					var ldarg = instrs[i];
					if (!ldarg.IsLdarg() || ldarg.GetParameterIndex() != 0)
						continue;
					var callvirt = instrs[i + 1];
					if (callvirt.OpCode.Code != Code.Callvirt)
						continue;
					var calledMethod = callvirt.Operand as MemberRef;
					if (calledMethod == null || calledMethod.FullName != "System.Char[] System.String::ToCharArray()")
						continue;
					var stloc = instrs[i + 2];
					if (!stloc.IsStloc())
						continue;
					var ldci4 = instrs[i + 3];
					if (!ldci4.IsLdcI4())
						continue;

					int magicAdd = 0;
					if (i < instrs.Count - 6) {
						var ldarg1 = instrs[i + 4];
						if (ldarg1.IsLdarg() && ldarg1.GetParameterIndex() == 1) {
							var opAdd1 = instrs[i + 5];
							if (opAdd1.OpCode == OpCodes.Add) {
								var ldci4_2 = instrs[i + 6];
								if (ldci4_2.IsLdcI4()) {
									magicAdd = ldci4_2.GetLdcI4Value();
								}
							}
						}
					}

					var info = new StringDecrypterInfo(method, ldci4.GetLdcI4Value() + magicAdd);
					stringDecrypterMethods.Add(info.method, info);
					Logger.v("Found string decrypter method: {0}, magic: 0x{1:X8}", Utils.RemoveNewlines(info.method), info.magic);
					break;
				}
			}
		}

		public string Decrypt(IMethod method, string encrypted, int value) {
			var info = stringDecrypterMethods.FindAny(method);
			char[] chars = encrypted.ToCharArray();
			byte key = (byte)(info.magic + value);
			for (int i = 0; i < chars.Length; i++) {
				char c = chars[i];
				byte b1 = (byte)((byte)c ^ key++);
				byte b2 = (byte)((byte)(c >> 8) ^ key++);
				chars[i] = (char)((b1 << 8) | b2);
			}
			return new string(chars);
		}
	}
}
