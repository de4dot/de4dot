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

namespace de4dot.code.deobfuscators.Confuser {
	class Unpacker {
		ModuleDefinition module;
		EmbeddedResource resource;
		uint key;
		ConfuserVersion version = ConfuserVersion.Unknown;

		enum ConfuserVersion {
			Unknown,
			v10_r42915,
			v14_r58564,
		}

		public bool Detected {
			get { return resource != null; }
		}

		public Unpacker(ModuleDefinition module) {
			this.module = module;
		}

		static string[] requiredFields = new string[] {
			 "System.String",
		};
		static string[] requiredEntryPointLocals = new string[] {
			"System.Byte[]",
			"System.Int32",
			"System.IO.BinaryReader",
			"System.IO.Stream",
		};
		public void find(ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			var entryPoint = module.EntryPoint;
			if (entryPoint == null)
				return;
			if (!new LocalTypes(entryPoint).all(requiredEntryPointLocals))
				return;
			var type = entryPoint.DeclaringType;
			if (!new FieldTypes(type).all(requiredFields))
				return;
			var decyptMethod = findDecryptMethod(type);
			if (decyptMethod == null)
				return;
			if (new LocalTypes(decyptMethod).exists("System.IO.MemoryStream"))
				version = ConfuserVersion.v10_r42915;
			else
				version = ConfuserVersion.v14_r58564;

			var cctor = DotNetUtils.getMethod(type, ".cctor");
			if (cctor == null)
				return;

			if (version == ConfuserVersion.v14_r58564) {
				simpleDeobfuscator.deobfuscate(decyptMethod);
				if (!findKey(decyptMethod, out key))
					throw new ApplicationException("Could not find magic");
			}

			simpleDeobfuscator.deobfuscate(cctor);
			simpleDeobfuscator.decryptStrings(cctor, deob);

			resource = findResource(cctor);
		}

		static bool findKey(MethodDefinition method, out uint key) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 2; i++) {
				if (instrs[i].OpCode.Code != Code.Xor)
					continue;
				var ldci4 = instrs[i + 1];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Xor)
					continue;

				key = (uint)DotNetUtils.getLdcI4Value(ldci4);
				return true;
			}
			key = 0;
			return false;
		}

		EmbeddedResource findResource(MethodDefinition method) {
			return DotNetUtils.getResource(module, DotNetUtils.getCodeStrings(method)) as EmbeddedResource;
		}

		static string[] requiredDecryptLocals = new string[] {
			"System.Byte[]",
			"System.IO.Compression.DeflateStream",
		};
		static MethodDefinition findDecryptMethod(TypeDefinition type) {
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!DotNetUtils.isMethod(method, "System.Byte[]", "(System.Byte[])"))
					continue;
				if (!new LocalTypes(method).all(requiredDecryptLocals))
					continue;

				return method;
			}
			return null;
		}

		public byte[] unpack() {
			if (resource == null)
				return null;
			var data = resource.GetResourceData();
			for (int i = 0; i < data.Length; i++)
				data[i] ^= (byte)(i ^ key);
			data = DeobUtils.inflate(data, true);

			if (version == ConfuserVersion.v14_r58564) {
				var reader = new BinaryReader(new MemoryStream(data));
				data = reader.ReadBytes(reader.ReadInt32());
			}

			return data;
		}
	}
}
