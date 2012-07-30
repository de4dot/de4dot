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
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Confuser {
	class StringDecrypter {
		ModuleDefinition module;
		MethodDefinition decryptMethod;
		EmbeddedResource resource;
		uint magic1, magic2;
		BinaryReader reader;

		public EmbeddedResource Resource {
			get { return resource; }
		}

		public MethodDefinition Method {
			get { return decryptMethod; }
		}

		public bool Detected {
			get { return decryptMethod != null; }
		}

		public StringDecrypter(ModuleDefinition module) {
			this.module = module;
		}

		static string[] requiredLocals = new string[] {
			"System.Byte[]",
			"System.IO.BinaryReader",
			"System.Random",
			"System.Reflection.Assembly",
		};
		public void find(ISimpleDeobfuscator simpleDeobfuscator) {
			var type = DotNetUtils.getModuleType(module);
			if (type == null)
				return;
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!DotNetUtils.isMethod(method, "System.String", "(System.Int32)"))
					continue;
				if (!new LocalTypes(method).all(requiredLocals))
					continue;

				simpleDeobfuscator.deobfuscate(method);

				var tmpResource = findResource(method);
				if (tmpResource == null)
					continue;
				if (!findMagic1(method, out magic1))
					continue;
				if (!findMagic2(method, out magic2))
					continue;

				resource = tmpResource;
				decryptMethod = method;
				break;
			}
		}

		EmbeddedResource findResource(MethodDefinition method) {
			return DotNetUtils.getResource(module, DotNetUtils.getCodeStrings(method)) as EmbeddedResource;
		}

		static bool findMagic1(MethodDefinition method, out uint magic) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				int index = ConfuserUtils.findCallMethod(instrs, i, Code.Callvirt, "System.Byte[] System.IO.BinaryReader::ReadBytes(System.Int32)");
				if (index < 0)
					break;
				if (index < 4)
					continue;

				index -= 4;
				if (!DotNetUtils.isLdarg(instrs[index]))
					continue;
				if (instrs[index + 1].OpCode.Code != Code.Xor)
					continue;
				var ldci4 = instrs[index + 2];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				if (instrs[index + 3].OpCode.Code != Code.Sub)
					continue;

				magic = (uint)DotNetUtils.getLdcI4Value(ldci4);
				return true;
			}
			magic = 0;
			return false;
		}

		static bool findMagic2(MethodDefinition method, out uint magic) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				int index = ConfuserUtils.findCallMethod(instrs, i, Code.Callvirt, "System.UInt32 System.IO.BinaryReader::ReadUInt32()");
				if (index < 0)
					break;
				if (index + 4 >= instrs.Count)
					continue;

				if (instrs[index + 1].OpCode.Code != Code.Not)
					continue;
				var ldci4 = instrs[index + 2];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				if (instrs[index + 3].OpCode.Code != Code.Xor)
					continue;
				if (!DotNetUtils.isStloc(instrs[index + 4]))
					continue;

				magic = (uint)DotNetUtils.getLdcI4Value(ldci4);
				return true;
			}
			magic = 0;
			return false;
		}

		public void initialize() {
			if (decryptMethod == null)
				return;
			reader = new BinaryReader(new MemoryStream(DeobUtils.inflate(resource.GetResourceData(), true)));
		}

		public string decrypt(MethodDefinition caller, int magic) {
			reader.BaseStream.Position = (caller.MetadataToken.ToInt32() ^ magic) - magic1;
			var bytes = reader.ReadBytes(reader.ReadInt32() ^ (int)~magic2);
			var rand = new Random(caller.MetadataToken.ToInt32());
			int mask = 0;
			for (int i = 0; i < bytes.Length; i++) {
				byte b = bytes[i];
				bytes[i] = (byte)(b ^ (rand.Next() & mask));
				mask += b;
			}
			return Encoding.UTF8.GetString(bytes);
		}
	}
}
