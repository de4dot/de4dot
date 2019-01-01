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
using de4dot.blocks;
using dnlib.DotNet.Emit;

namespace de4dot.code.deobfuscators.CryptoObfuscator {
	static class CoUtils {
		public static EmbeddedResource GetResource(ModuleDefMD module, MethodDef method) {
			if (method == null || method.Body == null)
				return null;
			return GetResource(module, DotNetUtils.GetCodeStrings(method));
		}

		public static EmbeddedResource GetResource(ModuleDefMD module, IEnumerable<string> names) {
			foreach (var name in names) {
				if (DotNetUtils.GetResource(module, name) is EmbeddedResource resource)
					return resource;
				try {
					resource = DotNetUtils.GetResource(module, Encoding.UTF8.GetString(Convert.FromBase64String(name))) as EmbeddedResource;
					if (resource != null)
						return resource;
				}
				catch {
				}
			}
			return null;
		}

		public static string XorCipher(string text, int key) {
			var array = text.ToCharArray();
			int len = array.Length;
			char cKey = Convert.ToChar(key);
			while (--len >= 0)
				array[len] ^= cKey;
			return new string(array);
		}

		public static string DecryptResourceName(string resourceName, int key, byte[] coddedBytes) {
			int len = resourceName.Length;
			var array = resourceName.ToCharArray();
			while (--len >= 0)
				array[len] = (char)((int)array[len] ^ ((int)coddedBytes[key & 15] | key));
			return new string(array);
		}

		public static string DecryptResourceName(ModuleDefMD module, MethodDef method) {
			string resourceName = "";
			MethodDef cctor = method, orginalResMethod = null;
			// retrive key and encrypted resource name 
			int key = 0;
			var instrs = cctor.Body.Instructions;
			for (int i = 0; i < instrs.Count - 2; i++) {
				if (instrs[i].OpCode != OpCodes.Ldstr)
					continue;
				if (!instrs[i + 1].IsLdcI4())
					break;
				key = instrs[i + 1].GetLdcI4Value();
				resourceName = instrs[i].Operand as String;
				cctor = instrs[i + 2].Operand as MethodDef;
				break;
			}

			// Find the method that contains resource name
			while (orginalResMethod == null) {
				foreach (var instr in cctor.Body.Instructions) {
					if (instr.OpCode == OpCodes.Ldftn) {
						var tempMethod = instr.Operand as MethodDef;
						if (tempMethod.ReturnType.FullName != "System.String")
							continue;
						orginalResMethod = tempMethod;
						break;
					}
					else if (instr.OpCode == OpCodes.Callvirt) {
						cctor = instr.Operand as MethodDef;
						cctor = cctor.DeclaringType.FindStaticConstructor();
						break;
					}
				}
			}

			// Get encrypted Resource name
			string encResourcename = DotNetUtils.GetCodeStrings(orginalResMethod)[0];
			// get Decryption key
			int xorKey = 0;
			for (int i = 0; i < orginalResMethod.Body.Instructions.Count; i++) {
				if (orginalResMethod.Body.Instructions[i].OpCode == OpCodes.Xor)
					xorKey = orginalResMethod.Body.Instructions[i - 1].GetLdcI4Value();
			}

			encResourcename = XorCipher(encResourcename, xorKey);
			var firstResource = GetResource(module, new string[] { encResourcename });
			resourceName = DecryptResourceName(resourceName, key, firstResource.CreateReader().ToArray());
			return resourceName;
		}
	}
}
